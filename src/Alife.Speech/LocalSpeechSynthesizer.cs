using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Alife.Speech;

public class LocalSpeechSynthesizer : IDisposable
{
    private WaveOutEvent? _outputDevice;
    private AudioFileReader? _audioFile;

    /// <summary>
    /// Speaks the given text asynchronously by invoking Python edge-tts and playing with NAudio.
    /// </summary>
    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        Stop(); // Ensure any previous speech is stopped

        if (string.IsNullOrWhiteSpace(text)) return;

        // 1. Generate MP3 via Python edge-tts
        string? outputFile = await GenerateSpeechFileAsync(text, cancellationToken);
        if (outputFile == null) return;

        // 2. Play MP3 via NAudio
        await PlayAudioAsync(outputFile, cancellationToken);
    }

    public static string RemoveInvalidFileNameChars(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return fileName;

        char[] invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Where(ch => !invalidChars.Contains(ch)));
    }

    public async Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        // We escape double quotes to avoid breaking the command line
        string safeText = RemoveInvalidFileNameChars(text).Trim();
        if (string.IsNullOrWhiteSpace(safeText))
            return null;

        string voice = "zh-CN-XiaoyiNeural"; // Lively/Youthful Edge Voice
        string outputPath = Path.Combine(Path.GetTempPath(), safeText + ".mp3");

        var psi = new ProcessStartInfo {
            FileName = "python",
            Arguments = $"-m edge_tts --text \"{safeText}\" --voice {voice} --write-media \"{outputPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process == null)
            return null;

        try
        {
            Task timeoutTask = Task.Delay(5000, cancellationToken); //进程可能卡死，需要超时判断
            Task processTask = process.WaitForExitAsync(cancellationToken);
            Task completedTask = await Task.WhenAny(timeoutTask, processTask);

            if (completedTask == timeoutTask)
                throw new TimeoutException();
            if (process.ExitCode != 0)
                throw new Exception($"{await process.StandardOutput.ReadToEndAsync(cancellationToken)}\n{await process.StandardError.ReadToEndAsync(cancellationToken)}");
            if (File.Exists(outputPath) == false)
                throw new Exception($"Speech file {outputPath} does not exist");

            return outputPath;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            process.Kill();
            Console.WriteLine(e);
        }

        return null;
    }

    public async Task PlayAudioAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource();

        try
        {
            var reader = new AudioFileReader(filePath);
            // 包装一层，自动修建开头空白
            var trimmed = new SilenceTrimmingSampleProvider(reader);

            _audioFile = reader;

            if (_outputDevice == null)
            {
                _outputDevice = new WaveOutEvent();
            }
            else if (_outputDevice.PlaybackState != PlaybackState.Stopped)
            {
                _outputDevice.Stop();
            }

            _outputDevice.Init(trimmed);

            EventHandler<StoppedEventArgs>? handler = null;
            handler = (s, e) => {
                _outputDevice.PlaybackStopped -= handler;

                if (cancellationToken.IsCancellationRequested)
                    tcs.SetCanceled();
                else if (e.Exception != null)
                    tcs.SetException(e.Exception);
                else
                    tcs.SetResult();

                // 不要在这里 Dispose 设备，以便复用
                reader.Dispose();
            };

            _outputDevice.PlaybackStopped += handler;

            // Stop playing if cancelled
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => { Stop(); });
            }

            _outputDevice.Play();
        }
        catch (Exception ex)
        {
            DisposeAudioResources();
            tcs.SetException(ex);
        }

        await tcs.Task;
    }

    /// <summary>
    /// Immediately stops all ongoing speech synthesis playback.
    /// </summary>
    public void Stop()
    {
        try
        {
            _outputDevice?.Stop();
        }
        catch { }
    }

    private void DisposeAudioResources()
    {
        _outputDevice?.Dispose();
        _outputDevice = null;

        _audioFile?.Dispose();
        _audioFile = null;
    }

    public void Dispose()
    {
        Stop();
        DisposeAudioResources();
        GC.SuppressFinalize(this);
    }

    private class SilenceTrimmingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _threshold;
        private bool _leadingSilenceTrimmed;

        // 用于检测尾部静音的缓冲区（约 100ms 的窗口）
        private readonly float[] _lookaheadBuffer;
        private int _lookaheadOffset;
        private int _lookaheadCount;
        private bool _sourceEof;

        public SilenceTrimmingSampleProvider(ISampleProvider source, float threshold = 0.01f)
        {
            _source = source;
            _threshold = threshold;
            // 默认 16kHz, 100ms 大约为 1600 个采样
            _lookaheadBuffer = new float[source.WaveFormat.SampleRate / 10];
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            // 1. 处理开头静音
            if (!_leadingSilenceTrimmed)
            {
                while (true)
                {
                    int samples = _source.Read(buffer, offset, count);
                    if (samples == 0) return 0;

                    for (int n = 0; n < samples; n++)
                    {
                        if (Math.Abs(buffer[offset + n]) > _threshold)
                        {
                            _leadingSilenceTrimmed = true;
                            // 找到了非静音，跳过之前的静音
                            int remaining = samples - n;
                            for (int i = 0; i < remaining; i++)
                            {
                                buffer[offset + i] = buffer[offset + n + i];
                            }

                            // 这里不立即返回，我们要进入 lookahead 阶段
                            // 把刚读取的数据作为输入源的一部分继续处理
                            return ProcessWithLookahead(buffer, offset, remaining, count);
                        }
                    }
                }
            }

            return ProcessWithLookahead(buffer, offset, 0, count);
        }

        private int ProcessWithLookahead(float[] buffer, int offset, int alreadyInSync, int count)
        {
            int written = alreadyInSync;

            while (written < count)
            {
                // 先从 lookahead 缓冲区取数据（如果有的话）
                if (_lookaheadCount > 0)
                {
                    int toCopy = Math.Min(_lookaheadCount, count - written);
                    for (int i = 0; i < toCopy; i++)
                    {
                        buffer[offset + written + i] = _lookaheadBuffer[_lookaheadOffset + i];
                    }
                    _lookaheadOffset += toCopy;
                    _lookaheadCount -= toCopy;
                    written += toCopy;
                    if (written == count) return written;
                }

                if (_sourceEof) return written;

                // 填充 lookahead 缓冲区
                _lookaheadOffset = 0;
                _lookaheadCount = _source.Read(_lookaheadBuffer, 0, _lookaheadBuffer.Length);

                if (_lookaheadCount == 0)
                {
                    _sourceEof = true;
                    return written;
                }

                // 检查 lookahead 缓冲区是否全是静音
                bool allSilence = true;
                for (int i = 0; i < _lookaheadCount; i++)
                {
                    if (Math.Abs(_lookaheadBuffer[i]) > _threshold)
                    {
                        allSilence = false;
                        break;
                    }
                }

                if (allSilence)
                {
                    // 如果接下来全是静音，我们保持在缓冲区里不输出，
                    // 继续尝试读取直到找到非静音或者文件结束
                    _lookaheadCount = 0;
                }
                else
                {
                    // 包含有效声音，可以输出
                    // 循环会回到开头，把 _lookaheadBuffer 的内容拷入 buffer
                }
            }

            return written;
        }
    }
}
