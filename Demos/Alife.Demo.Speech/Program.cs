using Alife.Basic;
using Alife.Framework;
using Alife.Implement;


Character character = new() {
    ID = "SpeechMao",
    Name = "真央",
    Prompt = "你是一个桌面上名为真央的 AI 语音助手。你非常活泼，喜欢模仿猫娘（说话带喵）。\n" +
             "主人正在通过语音或文字与你交流。请保持回答简短有力（回复控制在 30 字以内），适合语音播报。\n",
    Plugins = [
        typeof(OpenAIChatService),
        typeof(InterpreterService),
        typeof(SpeechService)
    ]
};

DemoSuite suite = await DemoSuite.InitializeAsync(character);

Terminal.LogInfo("规则：插上耳机激活语音识别，拔掉耳机自动待机保护隐私。");
Terminal.Log("----------------------------------------");

await suite.RunAsync();
