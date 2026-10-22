using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Speech;

[Module("语音说话",
    "为AI增加文字转语音输出的能力。",
    defaultCategory: "Alife 官方/交互方式",
    EditorUI = typeof(SpeechServiceUI))]
[Description("此服务让你获得能将文字以语音形式输出的能力。")]
public partial class SpeechService(
    XmlFunctionCaller functionService,
    global::Alife.Function.AIModelUtility.ISpeechModel speechModel,
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
        InitializeAudioPlayer();

        XmlHandler xmlHandler = new(this);
        functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);
        functionService.ChatCalledAsync += OnChatCalledAsync;

        return Task.CompletedTask;
    }

    protected override async Task OnDestroy()
    {
        functionService.ChatCalledAsync -= OnChatCalledAsync;
        audioPlayer?.Stop();
        try
        {
            await Task.WhenAll(audioSynthesizingTask, playAudioTask);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            logger.LogWarning(e, "停止语音任务失败");
        }
    }

    async Task OnChatCalledAsync()
    {
        using (ChatBot.ResourceOccupiedReason.Rent("等待语音结束"))
        {
            try
            {
                await playAudioTask;
            }
            catch (OperationCanceledException) { }
        }
    }

    async Task QueueSpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, DestroyCancellationToken);
        // 收到新的语音播报任务，先进行语音合成
        audioSynthesizingTask = speechModel.GenerateSpeechFileAsync(text, lifetime.Token);
        // 如果当前有音频在播放，则等待占用结束
        if (IsSpeaking)
        {
            try
            {
                await playAudioTask;
            }
            catch (OperationCanceledException)
            {
                lifetime.Cancel();
                _ = audioSynthesizingTask.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                return; // 语音被打断，那么后续语音显然也不用播放了
            }
        }

        // 可以播放音频
        string? audioFile = null;
        try
        {
            audioFile = await audioSynthesizingTask; // 等待合成任务完成
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "语言合成失败");
        }

        if (audioFile == null)
            return; // 没有可朗读的文本

        DestroyCancellationToken.ThrowIfCancellationRequested();
        playAudioTask = PlayAudioAsync(audioFile, cancellationToken);
    }

    async Task PlayAudioAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, DestroyCancellationToken);
        cancellationToken = lifetime.Token;
        cancellationToken.ThrowIfCancellationRequested();
        if (audioPlayer != null)
        {
            await PlayWithPlatformPlayerAsync(filePath, cancellationToken);
            return;
        }

        await PlayBufferedAudioAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// 平台播放器通道：包络仍在插件侧计算，播放交给客户端注入的实现（如 Android MediaPlayer）。
    /// </summary>
    async Task PlayWithPlatformPlayerAsync(string filePath, CancellationToken cancellationToken)
    {
        float[] envelope = CreateEnvelope(filePath);

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

    /// <summary>初始化当前平台的音频播放器。</summary>
    partial void InitializeAudioPlayer();

    /// <summary>计算用于口型同步的音量包络；无法计算时返回空数组。</summary>
    private partial float[] CreateEnvelope(string filePath);

    /// <summary>使用插件自带的缓冲播放器播放音频。</summary>
    private partial Task PlayBufferedAudioAsync(string filePath, CancellationToken cancellationToken);
}