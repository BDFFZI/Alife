using System.Text;
using Alife;
using Alife.Abstractions;
using Alife.Modules.Context;
using Alife.OneBot;
using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Alife.Test;
using Microsoft.SemanticKernel;

Terminal.Log("========================================", ConsoleColor.Magenta);
Terminal.Log("   Alife OneBot AI Plugin 集成验证 Demo", ConsoleColor.Magenta);
Terminal.Log("========================================", ConsoleColor.Magenta);

// 1. 配置角色 (真央)
var character = new Character {
    ID = "OneBotMao",
    Name = "真央",
    Prompt = "你是一个集成在 QQ 中的 AI 助手，名叫真央。你非常活泼，喜欢用猫娘语说话（每句话带喵）。\n" +
              "你正在通过 OneBot 协议与用户交流。如果你想发送消息，请使用 <qchat> 标签；如果你想发送图片，请使用 <qimage file=\"url或路径\" /> 标签。\n" +
              "在群聊中，如果你想回复特定的人，请在消息开头使用 OneBot 的 CQ 码格式，例如：[CQ:at,qq=发送者ID]。\n" +
              "示例：\n" +
              "1. 发送文字：<Interpreter><qchat target=\"123456\" type=\"group\">[CQ:at,qq=789] 你好喵！我也在看这个喵~</qchat></Interpreter>\n" +
              "2. 发送图片：<Interpreter><qimage file=\"url或路径\" /></Interpreter>",
    Plugins = new HashSet<Type> {
        typeof(OpenAIChatService),
        typeof(InterpreterService),
        typeof(OneBotService),
        typeof(ChatService),
        typeof(DialogContext)
    }
};

// 2. 初始化套件
var suite = await DemoSuite.InitializeAsync(character);

// 3. OneBot 特有配置 (从 suite 的 ConfigSystem 获取)
suite.ConfigSystem.SetConfiguration(typeof(OneBotService), new OneBotConfig {
    Url = "ws://127.0.0.1:3001",
    OwnerId = 1330958515L 
});

Terminal.LogInfo("提示：OneBot 插件已加载。您可以直接在此输入消息模拟 QQ 互动，所有发送到 OneBot 的消息都会在日志中拦截显示喵！");
Terminal.Log("--------------------------------------------------\n", ConsoleColor.Gray);

// 4. 开启交互循环
await suite.RunAsync();

Terminal.Log("演示结束，再见喵！", ConsoleColor.Magenta);
