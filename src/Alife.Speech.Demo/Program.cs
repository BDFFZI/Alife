using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Alife.Test;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace Alife.Speech.Test;

class Program
{
    private static DemoSuite? _suite;
    private static SpeechService? _speechService;

    static async Task Main(string[] args)
    {
        // 1. 配置角色与插件
        var character = new Character
        {
            ID = "SpeechMao",
            Name = "真央",
            Prompt = "你是一个桌面上名为真央的 AI 语音助手。你非常活泼，喜欢模仿猫娘（说话带喵）。\n" +
                     "主人正在通过语音或文字与你交流。请保持回答简短有力（回复控制在 30 字以内），适合语音播报。\n" +
                     "你非常在意主人的隐私，只有当主人插上耳机时，你才会开启‘听力识别’喵！",
            Plugins = new HashSet<Type> {
                typeof(OpenAIChatService),
                typeof(InterpreterService),
                typeof(SpeechService)
            }
        };

        // 2. 使用 DemoSuite 标准化启动
        _suite = await DemoSuite.InitializeAsync(character);

        _speechService = _suite.Activity.Plugins.OfType<SpeechService>().First();
        _speechService.AudioPlay += (msg) => Terminal.LogInfo("合成声音：" + msg);

        if (_speechService == null)
        {
            Terminal.LogError("无法从套件中提取 SpeechService 插件。");
            return;
        }

        Terminal.LogInfo("规则：插上耳机激活语音识别，拔掉耳机自动待机保护隐私。");
        Terminal.Log("----------------------------------------", ConsoleColor.Gray);

        await _suite.RunAsync();
    }


}
