using System.ComponentModel;
using System.Diagnostics;
using Alife.Abstractions;
using Alife.Interpreter;
using Alife.Speech;
using Alife.Test;
using Microsoft.SemanticKernel;
using NAudio.CoreAudioApi;

namespace Alife.OfficialPlugins;

[Plugin("语音对话", "为AI增加语音识别和语音转文字输出的能力。")]
[Description("此服务让你获得能将文字以语音形式输出的能力。")]
public class SpeechService : Plugin, IAsyncDisposable
{
    [XmlFunction("speak")]
    [Description("使用语音的方式向用户发送消息。")]
    public async Task Speak(XmlExecutorContext context, [XmlContent] string content)
    {
        if (hasHeadphones == false)
        {
            if (context.CallMode == CallMode.Opening)
            {
                //当没有耳机播放音频时，需要关闭语音识别，避免冲突
                if (recognizer.IsRecognizing)
                    recognizer.Stop();
            }
            else if (context.CallMode == CallMode.Closing)
            {
                //当停止说话时，等待当前语音结束后，恢复语音识别
                await WhenSpeechEnd();
                if (recognizer.IsRecognizing == false)
                    recognizer.Start();
            }
        }

        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        //收到新的语音播报任务，先进行语音合成
        audioFileSynthesizingCancellation = new CancellationTokenSource();
        Task<string?> audioSynthesizingTask = synthesizer.GenerateSpeechFileAsync(content, audioFileSynthesizingCancellation.Token);
        //如果当前有音频在播放，则等待占用结束
        await WhenSpeechEnd();

        //可以播放音频
        string? audioFile = null;
        try
        {
            audioFile = await audioSynthesizingTask; //等待合成任务完成
        }
        catch (Exception e)
        {
            //因为输入文本和网络原因，合成并不一定成功，但基本稳定，大部分错误都是难以处理的，所以直接忽略即可
            Terminal.LogWarning(e.ToString());
        }

        if (audioFile == null)
            return; //计算后发现没有可朗读的文本

        //不等待播放任务，继续接收下一次函数调用，从而实现预加载
        _ = synthesizer.SpeakAudioAsync(audioFile).ContinueWith(_ => {
            try
            {
                //播放完成后，尝试删除语音
                File.Delete(audioFile);
            }
            catch (Exception e)
            {
                Terminal.LogWarning(e.ToString());
            }
        });
    }

    readonly SpeechRecognizer recognizer;
    readonly SpeechSynthesizer synthesizer;
    ChatBot? chatBot;
    CancellationTokenSource? autoRecognizerSwitchCancellation;
    CancellationTokenSource? audioFileSynthesizingCancellation;
    bool hasHeadphones;

    public SpeechService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);

        //创建识别器
        recognizer = new SpeechRecognizer(PathEnvironment.ModelsPath);
        recognizer.Recognized += OnRecognized;

        //创建合成器
        synthesizer = new SpeechSynthesizer();

        void OnRecognized(string text)
        {
            if (chatBot != null)
                chatBot.Chat("[SpeechService] " + text);
        }
    }
    public async ValueTask DisposeAsync()
    {
        //停止语音识别
        recognizer.Dispose();
        if (autoRecognizerSwitchCancellation != null)
            await autoRecognizerSwitchCancellation.CancelAsync();

        //等待语音说完
        await WhenSpeechEnd();
    }
    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;

        //增加语音合成打断功能
        chatActivity.ChatBot.ChatSent += _ => {
            if (synthesizer.IsSpeaking)
                synthesizer.StopSpeak();
        };

        //打开语音识别
        recognizer.Start();
        //后续根据耳机情况自动开关语音识别
        autoRecognizerSwitchCancellation = new CancellationTokenSource();
        AutoRecognizerSwitch(autoRecognizerSwitchCancellation.Token);

        return Task.CompletedTask;

        /// <summary>
        /// 根据耳机状态自动开启语音识别
        /// </summary>
        async void AutoRecognizerSwitch(CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Run(async () => {
                    MMDeviceEnumerator enumerator = new();
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        MMDevice? device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        hasHeadphones = device.FriendlyName.Contains("耳机") ||
                                        device.FriendlyName.Contains("Headphones") ||
                                        device.FriendlyName.Contains("Headset") ||
                                        device.FriendlyName.Contains("Earphone");

                        if (hasHeadphones && recognizer.IsRecognizing == false)
                        {
                            recognizer.Start();
                            SendNotification("语音输入常驻开启", "检测到耳机，已通过 SpeechService 开启实时识别。");

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

                        await Task.Delay(1000, cancellationToken);
                    }
                    // ReSharper disable once FunctionNeverReturns 持续检测耳机状态，直到任务取消
                }, cancellationToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    async Task WhenSpeechEnd()
    {
        try
        {
            if (synthesizer.IsSpeaking)
                await synthesizer.LastSpeaking;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Terminal.LogError(e.ToString());
        }
    }
}
