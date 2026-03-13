using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Alife.Speech.Synthesis;

public class LocalSpeechSynthesizer : IDisposable
{
    private WaveOutEvent? _outputDevice;
    private AudioFileReader? _audioFile;
    private readonly string _tempAudioFile;

    public LocalSpeechSynthesizer()
    {
        _tempAudioFile = Path.Combine(Path.GetTempPath(), "Alife_speech_temp.mp3");
    }

    /// <summary>
    /// Speaks the given text asynchronously by invoking Python edge-tts and playing with NAudio.
    /// </summary>
    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        Stop(); // Ensure any previous speech is stopped

        if (string.IsNullOrWhiteSpace(text)) return;

        // 1. Generate MP3 via Python edge-tts
        bool generated = await GenerateSpeechFileAsync(text, _tempAudioFile, cancellationToken);
        if (!generated || cancellationToken.IsCancellationRequested) return;

        // 2. Play MP3 via NAudio
        await PlayAudioAsync(_tempAudioFile, cancellationToken);
    }

    private async Task<bool> GenerateSpeechFileAsync(string text, string outputPath, CancellationToken cancellationToken)
    {
        // Delete old file if exists
        if (File.Exists(outputPath))
        {
            try { File.Delete(outputPath); } catch { /* Ignore locked file for now */ }
        }

        // We escape double quotes to avoid breaking the command line
        string safeText = text.Replace("\"", "\\\"");
        string voice = "zh-CN-XiaoyiNeural"; // Lively/Youthful Edge Voice

        var psi = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"-m edge_tts --text \"{safeText}\" --voice {voice} --write-media \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return false;

        try
        {
            // Wait for it to finish or be canceled
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 && File.Exists(outputPath);
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            return false;
        }
    }

    private Task PlayAudioAsync(string filePath, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();

        try
        {
            _audioFile = new AudioFileReader(filePath);
            _outputDevice = new WaveOutEvent();
            _outputDevice.Init(_audioFile);

            EventHandler<StoppedEventArgs>? handler = null;
            handler = (s, e) =>
            {
                _outputDevice.PlaybackStopped -= handler;
                DisposeAudioResources();
                
                if (cancellationToken.IsCancellationRequested) tcs.TrySetCanceled();
                else if (e.Exception != null) tcs.TrySetException(e.Exception);
                else tcs.TrySetResult();
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
            tcs.TrySetException(ex);
        }

        return tcs.Task;
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
}
