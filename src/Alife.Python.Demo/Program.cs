using System.Text;
using Alife;
using Alife.Abstractions;
using Alife.Modules.Context;
using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Alife.Test;
using Microsoft.SemanticKernel;

Terminal.Log("========================================", ConsoleColor.Magenta);
Terminal.Log("   真央 Python 脚本集成验证 Demo", ConsoleColor.Magenta);
Terminal.Log("========================================", ConsoleColor.Magenta);

// 1. 配置角色
var character = new Character {
    ID = "PythonDemoMao",
    Name = "真央",
    Prompt = "你是一个桌宠，名叫真央。你非常活泼，喜欢用猫娘语说话（每句话带喵）。\n" +
             "你拥有运行 Python 脚本的能力。你可以通过调用 PythonService 来解决数学问题、处理文件或获取系统信息。\n" +
             "在对话中，如果你发现需要进行复杂计算或系统操作，请主动使用 Python 脚本喵！",
    Plugins = new HashSet<Type> {
        typeof(OpenAIChatService),
        typeof(InterpreterService),
        typeof(PythonService),
        typeof(ChatService),
        typeof(DialogContext)
    }
};

// 2. 初始化套件
var suite = await DemoSuite.InitializeAsync(character);

Terminal.LogInfo("提示：您可以让真央为你写一段 Python 代码并执行。输入 'exit' 退出。");
Terminal.Log("--------------------------------------------------\n", ConsoleColor.Gray);

// 3. 开启文字输入循环
await suite.RunAsync();

Terminal.Log("演示结束，再见喵！", ConsoleColor.Magenta);
