using Alife.Basic;
using Alife.Framework;
using Alife.Implement;


Terminal.Log("========================================", ConsoleColor.Magenta);
Terminal.Log("   真央桌宠 AI 交互集成验证 Demo", ConsoleColor.Magenta);
Terminal.Log("========================================", ConsoleColor.Magenta);

// 1. 配置演示角色
var character = new Character {
    ID = "PetDemoMao",
    Name = "真央",
    Prompt = "你是一个桌宠，名叫真央。你非常活泼，喜欢用猫娘语说话（每句话带喵）。\n" +
             "你可以通过控制桌宠应用来表达情感。请在对话中适时使用表情和动作喵！",
    Plugins = new HashSet<Type> {
        typeof(InterpreterService),
        typeof(DeskPetService),
        typeof(OpenAIChatService),
    }
};

// 2. 初始化标准演示套件
var suite = await DemoSuite.InitializeAsync(character);

Terminal.LogInfo("提示：输入文字开始与真央交流，输入 'exit' 退出。所有的表情/动作指令将在日志中高亮显示。");
Terminal.Log("--------------------------------------------------\n", ConsoleColor.Gray);

// 3. 运行交互循环
await suite.RunAsync();

Terminal.Log("演示结束，再见喵！", ConsoleColor.Magenta);
