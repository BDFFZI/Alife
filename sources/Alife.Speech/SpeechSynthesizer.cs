using System.Diagnostics;
using NAudio.Wave;

namespace Alife.Speech;

public class SpeechSynthesizer
{
    public bool IsSpeaking => currentTask is { Status: TaskStatus.Running };
    public Task LastSpeaking => currentTask;

    public async Task SpeakAsync(string text)
    {
        if (IsSpeaking)
            StopSpeak();
        if (string.IsNullOrWhiteSpace(text))
            return;

        cancellationToken = new CancellationTokenSource();
        currentTask = Task.Run(async () => {
            string? outputFile = await GenerateSpeechFileAsync(text, cancellationToken.Token);
            if (outputFile == null)
                return; //计算后发现没有可朗读的文本
            await PlayAudioAsync(outputFile, cancellationToken.Token);
        });

        await currentTask;
    }

    /// <summary>
    /// 不进行语音合成，直接将已存在的音频文件作为说话内容，借助该函数，可以将合成与播放分离，从而实现预加载等功能。
    /// </summary>
    public async Task SpeakAudioAsync(string file)
    {
        if (IsSpeaking)
            StopSpeak();

        cancellationToken = new CancellationTokenSource();
        currentTask = PlayAudioAsync(file, cancellationToken.Token);

        await currentTask;
    }
    public void StopSpeak()
    {
        if (IsSpeaking == false)
            throw new InvalidOperationException("当前没有语音中。");

        cancellationToken!.Cancel();
    }

    /// <summary>
    /// 通过edge-tts生成音频文件
    /// </summary>
    public async Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        //计算输出位置
        string fileSafeText = string.Concat(text.Where(ch => invalidChars.Contains(ch) == false));
        if (string.IsNullOrWhiteSpace(fileSafeText))
            return null;
        string outputPath = Path.Combine(Path.GetTempPath(), fileSafeText + ".mp3");

        ProcessStartInfo psi = new() {
            FileName = PathEnvironment.PythonExecutablePath,
            Arguments = $"-m edge_tts --text \"{fileSafeText}\" --voice {voiceTone} --write-media \"{outputPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using Process? process = Process.Start(psi);
        if (process == null)
            return null;

        try
        {
            // 进程可能卡死，需要超时判断
            await Task.WhenAny(
                process.WaitForExitAsync(cancellationToken),
                Task.Delay(5000, cancellationToken));
            if (process.HasExited == false)
                throw new TimeoutException("进程卡死或需要用户输入");
            if (process.ExitCode != 0)
                throw new Exception($"{await process.StandardOutput.ReadToEndAsync(cancellationToken)}\n{await process.StandardError.ReadToEndAsync(cancellationToken)}");
            if (File.Exists(outputPath) == false)
                throw new Exception($"语音文件未生成：{outputPath}");

            return outputPath;
        }
        finally
        {
            if (process.HasExited == false)
                process.Kill();
        }
    }

    /// <summary>
    /// 播放指定位置的音频文件
    /// </summary>
    public async Task PlayAudioAsync(string filePath, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource tcs = new();
        {
            AudioFileReader reader = new(filePath);
            WaveOutEvent speaker = new();
            speaker.Init(new LeadingSilenceTrimmer(reader));
            speaker.PlaybackStopped += OnPlaybackStopped;
            speaker.Play();

            void OnPlaybackStopped(object? _, StoppedEventArgs e)
            {
                reader.Dispose();
                speaker.Dispose();

                if (cancellationToken.IsCancellationRequested)
                    tcs.SetCanceled(cancellationToken);
                else if (e.Exception != null)
                    tcs.SetException(e.Exception);
                else
                    tcs.SetResult();
            }
        }

        await tcs.Task;
    }


    // 修剪开头静音（edge-tts 生成的音频开头普遍有空白）
    class LeadingSilenceTrimmer(ISampleProvider source, float threshold = 0.01f) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (trimmed) return source.Read(buffer, offset, count);

            while (true)
            {
                int samples = source.Read(buffer, offset, count);
                if (samples == 0) return 0;

                for (int n = 0; n < samples; n++)
                {
                    if (Math.Abs(buffer[offset + n]) > threshold)
                    {
                        trimmed = true;
                        int remaining = samples - n;
                        for (int i = 0; i < remaining; i++)
                            buffer[offset + i] = buffer[offset + n + i];
                        return remaining;
                    }
                }
            }
        }

        bool trimmed;
    }


    readonly char[] invalidChars;
    readonly string voiceTone;
    Task currentTask;
    CancellationTokenSource? cancellationToken;

    public SpeechSynthesizer()
    {
        invalidChars = Path.GetInvalidFileNameChars();
        voiceTone = "zh-CN-XiaoyiNeural";
        currentTask = Task.CompletedTask;
    }
}
