using System.ComponentModel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;

namespace Alife.OfficialPlugins;

using Alife.Abstractions;
using Alife.Interpreter;
using Alife.Speech;
using Microsoft.SemanticKernel;

[Plugin("语音对话", "为AI增加语音识别和语音转文字输出的能力。")]
public class SpeechService : Plugin, IAsyncDisposable
{
    readonly LocalSpeechSynthesizer synthesizer;
    readonly LocalSpeechRecognizer recognizer;
    readonly DialogContext dialogContext;
    Task lastSynthesizer;
    ChatCompletionAgent validator = null!;
    ChatHistoryAgentThread validatorThread = null!;
    CancellationTokenSource? validationCancelSource;
    CancellationTokenSource? synthesizerCancelSource;

    bool IsSpeaking => lastSynthesizer.Status == TaskStatus.Running;

    public SpeechService(InterpreterService interpreterService, DialogContext dialogContext)
    {
        interpreterService.RegisterHandler(this);
        this.dialogContext = dialogContext;

        //初始化语音识别
        string assemblyDir = Path.GetDirectoryName(typeof(SpeechService).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        string modelPath1 = Path.Combine(assemblyDir, "model");
        recognizer = new LocalSpeechRecognizer(modelPath1);
        recognizer.OnRecognized += OnRecognized;
        //初始化语音合成
        synthesizer = new LocalSpeechSynthesizer();
        lastSynthesizer = Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        //创建语音识别验证器
        validator = new ChatCompletionAgent() {
            Kernel = kernel.Clone(),
            Name = "语音识别鉴定器",
            Instructions = @"你是一个语音识别鉴定器，用于鉴定语音识别结果中是否有用户的声音。
我会给你提供对话背景和语音识别数据。语音识别结果中很可能有：
1. 误识别（常见的如单字或极短的无明显含义的词）
2. 参杂对方说话的声音，这些识别结果通常意义不明、语序混乱，或不符合背景。
3. 是用户说话，但部分误识别，这类结果通过读音可以理解出含义，且通常带有主动要求性的语气。
4. 多次收到内容基本相同的消息，这通常是用户语音识别失败，所以其尝试再次说话，因此此类消息要放低检测要求。
你要在检测出这些情况，尤其是otherSpeaking期间，这里面是肯定会混合他人声音的，注意分析角色立场和语境。
当你检测出用户在发言时，返回‘true’，否则返回‘false’。"
        };
        validator.Kernel.Plugins.Clear();
        //创建语音识别上下文
        validatorThread = new ChatHistoryAgentThread();
        validatorThread.ChatHistory.AddUserMessage("本次聊天中，对方的设定 > " + chatActivity.Character.Prompt);

        //持续追加上下文
        chatActivity.ChatBot.ChatHistoryAdd += OnChatHistoryAdd;
        chatActivity.ChatBot.ChatSent += OnChatSent;

        //开始语音识别
        StartRecognition();

        return Task.CompletedTask;
    }

    public void StartRecognition()
    {
        recognizer.Start();
    }

    public void StopRecognition()
    {
        recognizer.Stop();
    }
    void OnChatSent(string _)
    {
        StopSynthesizer(); //新对话打断旧对话
    }
    void OnChatHistoryAdd(ChatMessageContent obj)
    {
        if (obj.Role == AuthorRole.Assistant || obj.Role == AuthorRole.User)
            validatorThread.ChatHistory.AddUserMessage($"聊天上下文 > {AuthorRole.Assistant}：{obj.Content}");
    }

    void StopSynthesizer()
    {
        synthesizer.Stop();
        synthesizerCancelSource?.Cancel();
    }

    async void OnRecognized(string text, float confidence)
    {
        try
        {
            Console.WriteLine($"{text} : {confidence} ");

            if (confidence < 0.75) return;

            if (validationCancelSource != null)
                await validationCancelSource.CancelAsync();

            //ai验证语音识别内容是否正确
            // bool isValidated = false;
            // validatorThread.ChatHistory.AddUserMessage("待识别内容 > " + JsonConvert.SerializeObject(new {
            //     text,
            //     confidence,
            //     otherSpeaking = IsSpeaking,
            // }));
            // validationCancelSource = new CancellationTokenSource();
            // await foreach (var item in validator.InvokeAsync(validatorThread, cancellationToken: validationCancelSource.Token))
            // {
            //     bool.TryParse(item.Message.Content, out isValidated);
            // }
            // Console.WriteLine($"{text} : {confidence} : {isValidated}");

            // if (isValidated)
            {
                StopSynthesizer(); //用户说话，打断
                dialogContext.AddMessage(new ChatMessage() {
                    content = text,
                    isUser = true
                });
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    [XmlHandler("speak")]
    [Description("使用语音的方式向用户发送消息（优先使用speak和用户发送消息！）")]
    public async Task Speak(string content)
    {
        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        ChatMessage chatMessage = new ChatMessage() {
            tool = "speak",
            content = content,
            isInputting = true
        };
        dialogContext.AddMessage(chatMessage);

        try
        {
            synthesizerCancelSource = new CancellationTokenSource();
            Task<string?> task = synthesizer.GenerateSpeechFileAsync(content, synthesizerCancelSource.Token);
            await lastSynthesizer;
            lastSynthesizer = Task.Run(async () => {
                string? output = await task;
                if (output != null)
                    await synthesizer.PlayAudioAsync(output);
            });
        }
        finally
        {
            chatMessage.isInputting = false;
            dialogContext.UpdateMessage(chatMessage);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lastSynthesizer;
        await CastAndDispose(synthesizer);
        await CastAndDispose(recognizer);
        await CastAndDispose(lastSynthesizer);
        if (validationCancelSource != null) await CastAndDispose(validationCancelSource);
        if (synthesizerCancelSource != null) await CastAndDispose(synthesizerCancelSource);
        return;

        static async ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
                await resourceAsyncDisposable.DisposeAsync();
            else
                resource.Dispose();
        }
    }
}
