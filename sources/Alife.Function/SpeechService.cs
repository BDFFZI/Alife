using System.ComponentModel;
using System.Diagnostics;
using Alife.Abstractions;
using Alife.Interpreter;
using Alife.Speech;
using Microsoft.SemanticKernel;
using NAudio.CoreAudioApi;

namespace Alife.OfficialPlugins;

[Plugin("语音对话", "为AI增加语音识别和语音转文字输出的能力。")]
[Description("此服务让你获得能将文字以语音形式输出的能力。")]
public class SpeechService : Plugin, IAsyncDisposable
{
    public event Action<string, Task>? Speaking;

    [XmlHandler("speak")]
    [Description("使用语音的方式向用户发送消息。")]
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

                if (hasHeadphones)
                {
                    await speak;
                }
                else
                {
                    StopRecognition();
                    await speak;
                    StartRecognition();
                }
            }
        });
    }

    readonly LocalSpeechRecognizer recognizer;
    readonly LocalSpeechSynthesizer synthesizer;
    CancellationTokenSource? synthesizerCancelSource;
    Task lastSynthesizer;
    ChatBot chatBot = null!;
    bool hasHeadphones;
    bool isRecognitionEnabled;

    public SpeechService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);

        //创建识别器
        recognizer = new LocalSpeechRecognizer(PathEnvironment.ModelsPath);
        recognizer.OnRecognized += (text, conf) => OnRecognized(text, conf);

        //创建合成器
        synthesizer = new LocalSpeechSynthesizer();
        lastSynthesizer = Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        chatActivity.ChatBot.ChatSent += _ => StopSynthesizer(); //增加打断功能                             
        StartRecognition(); //默认打开语音识别
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

    void StartRecognition()
    {
        isRecognitionEnabled = true;
        recognizer.Start();
    }
    void StopRecognition()
    {
        isRecognitionEnabled = false;
        recognizer.Stop();
    }
    void StopSynthesizer()
    {
        synthesizerCancelSource?.Cancel();
    }
    void OnRecognized(string text, float confidence)
    {
        if (confidence < 0.75)
            return;
        chatBot.Chat("[SpeechService] " + text);
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
                    hasHeadphones = device.FriendlyName.Contains("耳机") ||
                                    device.FriendlyName.Contains("Headphones") ||
                                    device.FriendlyName.Contains("Headset") ||
                                    device.FriendlyName.Contains("Earphone");

                    if (hasHeadphones && !isRecognitionEnabled)
                    {
                        StartRecognition();
                        SendNotification("语音输入常驻开启", "真央检测到耳机，已通过 SpeechService 开启实时识别喵！");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
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
