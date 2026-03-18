using System.Text;
using Alife.Test;
using Alife.Abstractions;
using Alife.Modules.Context;
using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Alife.Speech;
using Microsoft.SemanticKernel;
using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace Alife.Speech.Test;

class Program
{
    private static DemoSuite? _suite;
    private static SpeechService? _speechService;
    private static bool _isRecognitionEnabled = false;

    static async Task Main(string[] args)
    {
        // 1. 配置角色与插件
        var character = new Character {
            ID = "SpeechMao",
            Name = "真央",
            Prompt = "你是一个桌面上名为真央的 AI 语音助手。你非常活泼，喜欢模仿猫娘（说话带喵）。\n" +
                     "主人正在通过语音或文字与你交流。请保持回答简短有力（回复控制在 30 字以内），适合语音播报。\n" +
                     "你非常在意主人的隐私，只有当主人插上耳机时，你才会开启‘听力识别’喵！",
            Plugins = new HashSet<Type> {
                typeof(OpenAIChatService),
                typeof(InterpreterService),
                typeof(ChatService),
                typeof(DialogContext),
                typeof(SpeechService)
            }
        };

        // 2. 使用 DemoSuite 标准化启动
        _suite = await DemoSuite.InitializeAsync(character);
        
        _speechService = _suite.Activity.Plugins.OfType<SpeechService>().FirstOrDefault();

        if (_speechService == null)
        {
            Terminal.LogError("无法从套件中提取 SpeechService 插件。");
            return;
        }

        // 3. 初始状态
        _speechService.StopRecognition();
        _isRecognitionEnabled = false;

        // 4. 开启耳机监控
        StartHeadphoneMonitoring();

        Terminal.LogInfo("规则：插上耳机激活语音识别，拔掉耳机自动待机保护隐私。");
        Terminal.Log("----------------------------------------", ConsoleColor.Gray);

        await _suite.RunAsync();
    }

    private static void StartHeadphoneMonitoring()
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

                    if (hasHeadphones && !_isRecognitionEnabled)
                    {
                        _isRecognitionEnabled = true;
                        _speechService?.StartRecognition();
                        Terminal.LogSuccess("检测到耳机插拔：开启语音识别流程喵！");
                        SendNotification("语音助手已上线", "真央检测到耳机，已通过 SpeechService 开启实时识别喵！");
                    }
                    else if (!hasHeadphones && _isRecognitionEnabled)
                    {
                        _isRecognitionEnabled = false;
                        _speechService?.StopRecognition();
                        Terminal.LogInfo("检测到耳机插拔：已进入保护隐私的待机模式。");
                        SendNotification("语音助手已离线", "真央因为未检测到耳机，已自动关闭语音识别喵！");
                    }
                }
                catch { }
                await Task.Delay(1000);
            }
        });
    }

    private static void SendNotification(string title, string message)
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
