using Alife.Abstractions;
using Alife.Interpreter;
using Alife.Speech;
using Microsoft.SemanticKernel;
using NAudio.CoreAudioApi;
using System.ComponentModel;
using System.Diagnostics;

namespace Alife.OfficialPlugins;

[Plugin("语音对话", "为AI增加语音识别和语音转文字输出的能力。")]
public class SpeechService : Plugin, IAsyncDisposable
{
    public event Action<string, Task>? Speaking;

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

    [XmlHandler("speak")]
    [Description("使用语音的方式向用户发送消息（优先使用speak和用户发送消息！）")]
    public async Task Speak(string content)
    {
        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        synthesizerCancelSource = new CancellationTokenSource();
        Task<string?> task = synthesizer.GenerateSpeechFileAsync(content, synthesizerCancelSource.Token);
        await lastSynthesizer;
        lastSynthesizer = Task.Run(async () => {
            string? output = await task;
            if (output != null)
            {
                Task speak = synthesizer.PlayAudioAsync(output);
                Speaking?.Invoke(output, speak);
                await speak;
            }
        });
    }

    readonly LocalSpeechRecognizer recognizer;
    bool isRecognitionEnabled = false;
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
        StartHeadphoneMonitoring(); //根据耳机情况开关语音识别

        return Task.CompletedTask;
    }
    public async ValueTask DisposeAsync()
    {
        await lastSynthesizer;
        synthesizer.Dispose();
        recognizer.Dispose();
        synthesizerCancelSource?.Dispose();
    }


    void OnRecognized(string text, float confidence)
    {
        if (confidence < 0.75)
            return;
        chatBot.Chat(text);
    }
    void StartHeadphoneMonitoring()
    {
        var enumerator = new MMDeviceEnumerator();
        Task.Run(async () => {
            while (true)
            {
                try
                {
                    var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    bool hasHeadphones = device.FriendlyName.Contains("耳机") ||
                                         device.FriendlyName.Contains("Headphones") ||
                                         device.FriendlyName.Contains("Headset");

                    if (hasHeadphones && !isRecognitionEnabled)
                    {
                        isRecognitionEnabled = true;
                        StartRecognition();
                        SendNotification("语音助手已上线", "真央检测到耳机，已通过 SpeechService 开启实时识别喵！");
                    }
                    else if (!hasHeadphones && isRecognitionEnabled)
                    {
                        isRecognitionEnabled = false;
                        StopRecognition();
                        SendNotification("语音助手已离线", "真央因为未检测到耳机，已自动关闭语音识别喵！");
                    }
                }
                catch { }
                await Task.Delay(1000);
            }
        });
    }
    void SendNotification(string title, string message)
    {
        try
        {
            string script = $"$Title='{title}'; $Message='{message}'; " +
                            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
                            "$Template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                            "$TextNodes = $Template.GetElementsByTagName('text'); " +
                            "$TextNodes.Item(0).AppendChild($Template.CreateTextNode($Title)) | Out-Null; " +
                            "$TextNodes.Item(1).AppendChild($Template.CreateTextNode($Message)) | Out-Null; " +
                            "$Toast = [Windows.UI.Notifications.ToastNotification]::new($Template); " +
                            "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('AlifeSpeechAssist').Show($Toast);";

            Process.Start(new ProcessStartInfo {
                FileName = "powershell",
                Arguments = $"-Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notification failed: {ex.Message}");
        }
    }
}
