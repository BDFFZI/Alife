using System.Text;
using Alife;
using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Alife.Speech;
using Microsoft.SemanticKernel;
using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace Alife.Speech.Test;

class Program
{
    private static ChatActivity? _chatActivity;
    private static SpeechService? _speechService;
    private static bool _isRecognitionEnabled = false;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("========================================");
        Console.WriteLine("   Alife AI 语音助手 (插件集成模式)");
        Console.WriteLine("========================================");

        // 1. 初始化环境
        var storageSystem = new StorageSystem();
        var configSystem = new ConfigurationSystem(storageSystem);

        // 2. 配置角色与插件
        // 注意：SpeechService 内部会自动初始化自己的 Recognizer 和 Synthesizer
        var character = new Character {
            ID = "SpeechMao",
            Name = "真央",
            Prompt = "你是一个桌面上名为真央的 AI 语音助手。你非常活泼，喜欢模仿猫娘（说话带喵）。\n" +
                     "主人正在通过语音与你交流。请保持回答简短有力（回复控制在 30 字以内），适合语音播报。\n" +
                     "你非常在意主人的隐私，只有当主人插上耳机时，你才会开启‘听力’喵！",
            Plugins = new HashSet<Type> {
                typeof(OpenAIChatService),
                typeof(InterpreterService),
                typeof(ChatService),
                typeof(DialogContext),
                typeof(SpeechService) // 核心：使用语音插件
            }
        };

        Console.WriteLine("[系统] 正在准备 AI 大脑及插件环境...");
        
        // 确保模型文件夹在运行目录下可用，或者 SpeechService 能找到
        // SpeechService.cs:33 行会从 AppDomain.CurrentDomain.BaseDirectory 寻找 "model"
        
        _chatActivity = await ChatActivity.Create(character, configSystem, null, new object[] {
            configSystem, storageSystem
        });

        // 获取语插件实例以便控制
        _speechService = _chatActivity.Plugins.OfType<SpeechService>().FirstOrDefault();

        if (_speechService == null)
        {
            Console.WriteLine("[错误] 无法初始化语音服务插件。");
            return;
        }

        // 3. 初始状态设置为关闭（等待耳机）
        _speechService.StopRecognition();
        _isRecognitionEnabled = false;

        // 4. 开启耳机监控
        StartHeadphoneMonitoring();

        Console.WriteLine("\n[提示] 程序已启动。");
        Console.WriteLine("[规则] 插上耳机激活语音识别，拔掉耳机自动待机保护隐私。");
        Console.WriteLine("[提示] 按 Ctrl+C 退出。\n");

        await Task.Delay(-1);
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
                        Console.WriteLine("\n[状态] 检测到耳机拔插：开启语音识别流程喵！");
                        SendNotification("语音助手已上线", "真央检测到耳机，已通过 SpeechService 开启实时识别喵！");
                    }
                    else if (!hasHeadphones && _isRecognitionEnabled)
                    {
                        _isRecognitionEnabled = false;
                        _speechService?.StopRecognition();
                        Console.WriteLine("\n[状态] 检测到耳机拔插：已进入保护隐私的待机模式。");
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
