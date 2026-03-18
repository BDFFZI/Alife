using System.ComponentModel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using Alife.Abstractions;
using Alife.Interpreter;
using Alife.Speech;
using Microsoft.SemanticKernel;

namespace Alife.OfficialPlugins;

[Plugin("语音对话", "为AI增加语音识别和语音转文字输出的能力。")]
public class SpeechService : Plugin, IAsyncDisposable
{
    public void StartRecognition()
    {
        recognizer.Start();
    }
    public void StopRecognition()
    {
        recognizer.Stop();
    }
    public void StopSynthesizer()
    {
        synthesizer.Stop();
        synthesizerCancelSource?.Cancel();
    }

    readonly LocalSpeechRecognizer recognizer;
    readonly LocalSpeechSynthesizer synthesizer;
    CancellationTokenSource? synthesizerCancelSource;
    Task lastSynthesizer;
    ChatBot chatBot = null!;

    public SpeechService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);

        //创建识别器
        string assemblyDir = Path.GetDirectoryName(typeof(SpeechService).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        string modelPath1 = Path.Combine(assemblyDir, "model");
        recognizer = new LocalSpeechRecognizer(modelPath1);
        recognizer.OnRecognized += (text, conf) => OnRecognized(text, conf);

        //创建合成器
        synthesizer = new LocalSpeechSynthesizer();
        lastSynthesizer = Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        chatActivity.ChatBot.ChatSent += _ => StopSynthesizer(); //增加打断功能

        return Task.CompletedTask;
    }


    void OnRecognized(string text, float confidence)
    {
        if (confidence < 0.75)
            return;
        chatBot.Chat(text);
    }

    [XmlHandler("speak")]
    [Description("使用语音的方式向用户发送消息（优先使用speak和用户发送消息！）")]
    public async Task Speak(string content)
    {
        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content)) return;

        object token = new object();
        OnSpeakOutput?.Invoke(token, content);

        try
        {
            synthesizerCancelSource = new CancellationTokenSource();
            Task<string?> task = synthesizer.GenerateSpeechFileAsync(content, synthesizerCancelSource.Token);
            await lastSynthesizer;
            lastSynthesizer = Task.Run(async () => {
                string? output = await task;
                if (output != null) await synthesizer.PlayAudioAsync(output);
            });
        }
        catch { }
        finally
        {
            OnSpeakFinished?.Invoke(token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lastSynthesizer;
        synthesizer.Dispose();
        recognizer.Dispose();
        validationCancelSource?.Dispose();
        synthesizerCancelSource?.Dispose();
    }
}
