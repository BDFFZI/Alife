#if WINDOWS
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Alife.Function.Speech;

public partial class SpeechService
{
    // 桌面默认走 NAudio；若外部注入了平台播放器则优先使用。
    partial void InitializeAudioPlayer()
        => audioPlayer = ChatActivity.Container.Instances.OfType<ISpeechAudioPlayer>().FirstOrDefault();

    private partial float[] CreateEnvelope(string filePath)
    {
        using AudioFileReader reader = new(filePath);
        SpeechSilenceTrimmer silenceTrimmer = new(reader);
        return silenceTrimmer.GetEnvelope(AudioWindowMs);
    }

    private partial async Task PlayBufferedAudioAsync(string filePath, CancellationToken cancellationToken)
    {
        TaskCompletionSource tcs = new();

        if (File.Exists(filePath) == false)
            Console.WriteLine(filePath);

        await using AudioFileReader reader = new(filePath); //音频读取
        SpeechSilenceTrimmer silenceTrimmer = new(reader); //音频预处理
        // 预计算整段音量包络，出声后再连同起跑时间戳一起发布，前端按本机时钟查表驱动口型
        float[] envelope = silenceTrimmer.GetEnvelope(AudioWindowMs);
        using WaveOutEvent speaker = new();
        speaker.Init(silenceTrimmer);
        speaker.PlaybackStopped += OnPlaybackStopped;
        speaker.Play();
        try
        {
            AudioStarted?.Invoke(new SpeechAudioContext {
                Envelope = envelope,
                WindowMs = AudioWindowMs,
                StartUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "发布语音包络失败");
        }

        // ReSharper disable once AccessToDisposedClosure
        await using CancellationTokenRegistration registration = cancellationToken.Register(() => speaker.Stop());
        await tcs.Task; //等待播放完毕

        void OnPlaybackStopped(object? _, StoppedEventArgs e)
        {
            try
            {
                AudioStopped?.Invoke();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "发布语音停止事件失败");
            }
            if (e.Exception != null)
                tcs.TrySetException(e.Exception);
            else
                tcs.TrySetResult();
        }
    }
}
#endif
