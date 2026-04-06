using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Alife.Test;
using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace Alife.Speech.Test;

class Program
{
    static async Task Main(string[] args)
    {
        Character character = new()
        {
            ID = "SpeechMao",
            Name = "真央",
            Prompt = "你是一个桌面上名为真央的 AI 语音助手。你非常活泼，喜欢模仿猫娘（说话带喵）。\n" +
                     "主人正在通过语音或文字与你交流。请保持回答简短有力（回复控制在 30 字以内），适合语音播报。\n" +
                     "你非常在意主人的隐私，只有当主人插上耳机时，你才会开启'听力识别'喵！",
            Plugins = new HashSet<Type>
            {
                typeof(OpenAIChatService),
                typeof(InterpreterService),
                typeof(SpeechService)
            }
        };

        DemoSuite suite = await DemoSuite.InitializeAsync(character);
        SpeechService speechService = suite.Activity.Plugins.OfType<SpeechService>().First();
        speechService.AudioFileGenerated += (msg, _) => Terminal.LogInfo("合成声音：" + msg);

        Terminal.LogInfo("规则：插上耳机激活语音识别，拔掉耳机自动待机保护隐私。");
        Terminal.Log("----------------------------------------", ConsoleColor.Gray);

        await suite.RunAsync();
    }
}
