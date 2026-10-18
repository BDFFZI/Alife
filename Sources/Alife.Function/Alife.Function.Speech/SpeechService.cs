using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
#if !ANDROID
using NAudio.Wave;
#endif

namespace Alife.Function.Speech;

[Module("语音说话",
    "为AI增加文字转语音输出的能力。",
    defaultCategory: "Alife 官方/交互方式",
    EditorUI = typeof(SpeechServiceUI))]
[Description("此服务让你获得能将文字以语音形式输出的能力。")]
public class SpeechService(
    XmlFunctionCaller functionService,
    AIModelUtility.ISpeechModel speechModel,
    ILogger<SpeechService> logger) :
    ChatBehaviour,
    ISpeechService
{
    public event Action<SpeechAudioContext>? AudioStarted;
    public event Action? AudioStopped;

    public bool IsSpeaking => playAudioTask is { IsCompleted: false };

    [XmlFunction(FunctionMode.Content, order: -10, parallel: true)]
    [Description("将文本以语音方式输出（这应该是你默认对外的交互方式）")]
    public async Task Speak(XmlExecutorContext context, CancellationToken cancellationToken)
    {
        try
        {
            switch (context.CallMode)
            {
                case CallMode.Opening:
                    break;
                case CallMode.Closing:
                    try
                    {
                        if (IsSpeaking)
                            await playAudioTask;
                    }
                    catch (OperationCanceledException) { }
                    break;
                case CallMode.Content:
                {
                    string content = context.Content.Trim();
                    if (string.IsNullOrWhiteSpace(content))
                        break;
                    await QueueSpeakAsync(content, cancellationToken);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    ISpeechAudioPlayer? audioPlayer;
    Task<string?> audioSynthesizingTask = Task.FromResult<string?>(null);
    Task playAudioTask = Task.CompletedTask;
    const int AudioWindowMs = 40;

    protected override Task OnAwake()
    {
#if ANDROID
        // Android 直接使用进程内 MediaPlayer，无需客户端注入。
        audioPlayer = new AndroidSpeechAudioPlayer();
#else
        // 桌面默认走 NAudio；若外部注入了平台播放器则优先使用。
        audioPlayer = ChatActivity.Container.Instances.OfType<ISpeechAudioPlayer>().FirstOrDefault();
#endif

        XmlHandler xmlHandler = new(this);
        functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);
        functionService.ChatCalledAsync += OnChatCalledAsync;

        return Task.CompletedTask;
    }

    async Task OnChatCalledAsync()
    {
        using (ChatBot.ResourceOccupiedReason.Rent("等待语音结束"))
        {
            await playAudioTask;
        }
    }

    async Task QueueSpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        // 收到新的语音播报任务，先进行语音合成
        audioSynthesizingTask = speechModel.GenerateSpeechFileAsync(text, cancellationToken);
        // 如果当前有音频在播放，则等待占用结束
        if (IsSpeaking)
        {
            try
            {
                await playAudioTask;
            }
            catch (OperationCanceledException)
            {
                return; // 语音被打断，那么后续语音显然也不用播放了
            }
        }

        // 可以播放音频
        string? audioFile = null;
        try
        {
            audioFile = await audioSynthesizingTask; // 等待合成任务完成
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "语言合成失败");
        }

        if (audioFile == null)
            return; // 没有可朗读的文本

        playAudioTask = PlayAudioAsync(audioFile, cancellationToken);
    }
    async Task PlayAudioAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (audioPlayer != null)
        {
            await PlayWithPlatformPlayerAsync(filePath, cancellationToken);
            return;
        }

#if !ANDROID
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
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>
    /// 平台播放器通道：包络仍在插件侧计算，播放交给客户端注入的实现（如 Android MediaPlayer）。
    /// </summary>
    async Task PlayWithPlatformPlayerAsync(string filePath, CancellationToken cancellationToken)
    {
#if !ANDROID
        float[] envelope;
        using (AudioFileReader reader = new(filePath))
        {
            SpeechSilenceTrimmer silenceTrimmer = new(reader);
            envelope = silenceTrimmer.GetEnvelope(AudioWindowMs);
        }
#else
        float[] envelope = [];
#endif

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

        try
        {
            await audioPlayer!.PlayAsync(filePath, cancellationToken);
        }
        finally
        {
            try
            {
                AudioStopped?.Invoke();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "发布语音停止事件失败");
            }
        }
    }
}