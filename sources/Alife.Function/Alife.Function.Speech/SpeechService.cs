using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Alife.Function.Speech;

[Module("语音说话",
    "为AI增加语音转文字输出的能力。",
    defaultCategory: "Alife 官方/交互方式",
    EditorUI = typeof(SpeechServiceUI))]
[Description("此服务让你获得能将文字以语音形式输出的能力。")]
public class SpeechService(
    XmlFunctionCaller functionService,
    ISpeechModel speechModel,
    ILogger<SpeechService> logger) :
    ChatBehaviour
{
    public bool IsSpeaking => playAudioTask is { IsCompleted: false };

    [XmlFunction(FunctionMode.Content, order: -10)]
    [Description("将文本以语音方式输出（这应该是你默认对外的交互方式）")]
    public async Task Speak(XmlExecutorContext context, CancellationToken cancellationToken)
    {
        try
        {
            switch (context.CallMode)
            {
                case CallMode.Opening:
                    try
                    {
                        if (IsSpeaking)
                            await playAudioTask;
                    }
                    catch (OperationCanceledException) {}
                    break;
                case CallMode.Closing:
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
        catch (OperationCanceledException) {}
    }

    Task<string?> audioSynthesizingTask = Task.FromResult<string?>(null);
    Task playAudioTask = Task.CompletedTask;

    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new();
        functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);
        functionService.ChatCalled += OnChatCalled;

        return Task.CompletedTask;
    }

    async Task OnChatCalled()
    {
        await playAudioTask;
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
                return;// 语音被打断，那么后续语音显然也不用播放了
            }
        }

        // 可以播放音频
        string? audioFile = null;
        try
        {
            audioFile = await audioSynthesizingTask;// 等待合成任务完成
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "语言合成失败");
        }

        if (audioFile == null)
            return;// 没有可朗读的文本

        playAudioTask = PlayAudioAsync(audioFile, cancellationToken);
    }
    async Task PlayAudioAsync(string filePath, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource tcs = new();

        await using AudioFileReader reader = new(filePath);//音频读取
        SpeechSilenceTrimmer silenceTrimmer = new(reader);//音频预处理
        using WaveOutEvent speaker = new();
        speaker.Init(silenceTrimmer);
        speaker.PlaybackStopped += OnPlaybackStopped;
        speaker.Play();

        // ReSharper disable once AccessToDisposedClosure
        await using CancellationTokenRegistration registration = cancellationToken.Register(() => speaker.Stop());
        await tcs.Task;//等待播放完毕

        void OnPlaybackStopped(object? _, StoppedEventArgs e)
        {
            if (e.Exception != null)
                tcs.TrySetException(e.Exception);
            else
                tcs.TrySetResult();
        }
    }
}
