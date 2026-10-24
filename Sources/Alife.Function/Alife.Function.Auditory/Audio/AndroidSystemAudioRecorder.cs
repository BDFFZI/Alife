#if ANDROID
using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Client.Android;
using Alife.Foundation;
using Android.App;
using Android.Content;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Microsoft.Maui.ApplicationModel;

namespace Alife.Function.Auditory;

/// <summary>Plugin-owned Android capture, emitting 16kHz mono float samples.</summary>
public sealed class SystemAudioRecorder : IDisposable
{
    public event Action<float[], int>? WaveformReady;
    public bool IsRecording => running;
    public string Source { get; private set; } = "";
    public Exception? LastError { get; private set; }
    volatile bool running;
    readonly object sync = new();
    readonly SemaphoreSlim startGate = new(1, 1);
    AudioRecord? recorder;
    AccessLease? access;
    Task? reader;
    global::Android.Media.Audiofx.AcousticEchoCanceler? echoCanceler;

    // Kept for callers that already acquired permission; async callers should use StartAsync.
    public void Start(string source = "system") => StartAsync(source).GetAwaiter().GetResult();
    public async Task StartAsync(string source = "system", CancellationToken cancellationToken = default)
    {
        if (source != "system" && source != "mic") throw new ArgumentException("声源必须是 system 或 mic。", nameof(source));
        await startGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRecording) throw new InvalidOperationException("录音器已在运行。");
            Stop();

            PermissionStatus permission = await MainThread.InvokeOnMainThreadAsync(
                () => Permissions.RequestAsync<Permissions.Microphone>());
            if (permission != PermissionStatus.Granted)
                throw new UnauthorizedAccessException("需要麦克风权限才能录音。");

            MediaProjection? projection = null;
            IDisposable? foreground = null;
            AccessLease? lease = null;
            try
            {
                if (source == "system")
                {
                    if (!OperatingSystem.IsAndroidVersionAtLeast(29))
                        throw new PlatformNotSupportedException("系统音频采集需要 Android 10 或更高版本。");

                    MediaProjectionManager manager = await MainThread.InvokeOnMainThreadAsync(() => {
                        Activity activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? throw new InvalidOperationException("请先打开 Alife 主界面。");
                        return (MediaProjectionManager)activity.GetSystemService(Context.MediaProjectionService)!;
                    });
                    Intent intent = manager.CreateScreenCaptureIntent();
                    (Android.App.Result resultCode, Intent? data) =
                        await AndroidAlifePlatform.RequestActivityResultAsync(intent, cancellationToken);
                    if (resultCode != Android.App.Result.Ok || data == null)
                        throw new UnauthorizedAccessException("未授权系统音频采集。");

                    foreground = await AndroidAlifePlatform.AcquireForegroundServiceAsync(
                        "Alife 系统音频采集", microphone: false, mediaProjection: true, cancellationToken);
                    projection = manager.GetMediaProjection((int)resultCode, data)
                                 ?? throw new InvalidOperationException("系统音频授权已失效。");
                }
                else
                {
                    foreground = await AndroidAlifePlatform.AcquireForegroundServiceAsync(
                        "Alife 麦克风录音", microphone: true, mediaProjection: false, cancellationToken);
                }

                lease = new AccessLease(projection, foreground);
                lock (sync)
                {
                    access = lease;
                    int size = Math.Max(64000, AudioRecord.GetMinBufferSize(16000, ChannelIn.Mono, Encoding.Pcm16bit));
                    // Android's .NET binding types this mask as ChannelOut even for AudioRecord.
                    using var format = new AudioFormat.Builder().SetSampleRate(16000).SetChannelMask((ChannelOut)(int)ChannelIn.Mono).SetEncoding(Encoding.Pcm16bit).Build()!;
                    using var builder = new AudioRecord.Builder().SetAudioFormat(format).SetBufferSizeInBytes(size);
                    if (source == "system")
                    {
                        using var capture = new AudioPlaybackCaptureConfiguration.Builder(projection!)
                            .AddMatchingUsage(AudioUsageKind.Media).AddMatchingUsage(AudioUsageKind.Game).AddMatchingUsage(AudioUsageKind.Unknown)
                            .ExcludeUid(global::Android.OS.Process.MyUid()).Build()!;
                        builder.SetAudioPlaybackCaptureConfig(capture);
                    }
                    else builder.SetAudioSource(AudioSource.VoiceRecognition);
                    recorder = builder.Build()!;
                    if (recorder.State != State.Initialized) throw new InvalidOperationException("Android 录音器初始化失败。");
                    if (source == "mic" && global::Android.Media.Audiofx.AcousticEchoCanceler.IsAvailable)
                        echoCanceler = global::Android.Media.Audiofx.AcousticEchoCanceler.Create(recorder.AudioSessionId);
                    echoCanceler?.SetEnabled(true);
                    recorder.StartRecording();
                    if (recorder.RecordingState != RecordState.Recording) throw new InvalidOperationException("Android 拒绝启动录音。");
                    Source = source; LastError = null; running = true;
                    reader = Task.Run(ReadLoop);
                    _ = reader.ContinueWith(completed => {
                        lock (sync) { if (ReferenceEquals(reader, completed)) Stop(); }
                    }, TaskScheduler.Default);
                }
            }
            catch
            {
                access = null;
                lease?.Dispose();
                projection?.Stop();
                projection?.Dispose();
                foreground?.Dispose();
                Stop();
                throw;
            }
        }
        finally { startGate.Release(); }
    }

    void ReadLoop()
    {
        try
        {
            short[] pcm = new short[1600];
            float[] samples = new float[pcm.Length];
            while (running)
            {
                int count = recorder!.Read(pcm, 0, pcm.Length);
                if (!running) break;
                if (count < 0) throw new InvalidOperationException($"Android 音频读取失败: {count}");
                if (count == 0) continue;
                for (int i = 0; i < count; i++) samples[i] = pcm[i] / 32768f;
                WaveformReady?.Invoke(samples, count);
            }
        }
        catch (Exception e) { if (running) { LastError = e; AlifeLog.LogError(e); } }
        finally { running = false; }
    }

    public void Stop()
    {
        lock (sync)
        {
            running = false;
            if (recorder?.RecordingState == RecordState.Recording) recorder.Stop();
            // Finish all callbacks before a consumer disposes its recognizer.
            reader?.GetAwaiter().GetResult(); reader = null;
            echoCanceler?.Release(); echoCanceler?.Dispose(); echoCanceler = null;
            recorder?.Release(); recorder?.Dispose(); recorder = null;
            access?.Dispose(); access = null;
        }
    }
    public void Dispose() => Stop();

    sealed class AccessLease : IDisposable
    {
        public AccessLease(MediaProjection? projection, IDisposable foreground)
        {
            this.projection = projection;
            this.foreground = foreground;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            projection?.Stop();
            projection?.Dispose();
            foreground.Dispose();
        }

        readonly MediaProjection? projection;
        readonly IDisposable foreground;
        int disposed;
    }
}
#endif
