using System.Reflection;
using System.Text;
using Alife;
using Alife.Abstractions;
using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

Console.WriteLine("=== 真央桌宠 AI 交互演示 (Headless) ===");

if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
{
    // 检查 Windows 控制台是否为 UTF-8 (65001)
    if (System.Text.Encoding.Default.CodePage != 65001)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[Warning] 检测到当前控制台不是 UTF-8 编码，中文输入可能会出现乱码喵！");
        Console.WriteLine("[Hint] 建议在运行前执行 `chcp 65001`，或使用 Windows Terminal 喵。");
        Console.ResetColor();
    }
}

// 1. 初始化核心系统
var storageSystem = new StorageSystem();
var pluginSystem = new PluginSystem(storageSystem);
var configSystem = new ConfigurationSystem(storageSystem);

// 2. 模拟 ChatWindow (Console 模式)
var chatWindow = new ChatWindow(); 
chatWindow.MessageAdded += (msg) => {
    // 只有非输入状态的消息或者用户消息才打印，避免干扰控制台
    if (!msg.isInputting || msg.isUser)
    {
        string role = msg.isUser ? "【用户】" : "【真央】";
        if (!string.IsNullOrEmpty(msg.content)) {
            Console.WriteLine($"{role}: {msg.content}");
        }
    }
};
chatWindow.MessageUpdated += (msg) => {
    // 当消息完成输入时打印（或者你可以根据需求做流式打印，这里简单处理）
    if (!msg.isInputting && !msg.isUser)
    {
        Console.WriteLine($"【真央】: {msg.content}");
    }
};

// 3. 配置 OpenAI
var openAiConfig = configSystem.GetConfiguration(typeof(OpenAIChatService)) as OpenAIChatServiceConfig;
if (openAiConfig != null) {
    Console.WriteLine($"[Config] 使用模型: {openAiConfig.modelId} @ {openAiConfig.endpoint}");
}

// 4. 创建演示角色
var character = new Character
{
    ID = "PetDemoMao",
    Name = "真央",
    Prompt = "你是一个桌宠，名叫真央。你非常活泼，喜欢用猫娘语说话（每句话带喵）。" + 
             "你可以通过控制桌宠应用来表达情感。" +
             "可用指令：\n" +
             "- <pet_bubble>文字</pet_bubble>: 显示气泡内容\n" +
             "- <pet_exp>01-08</pet_exp>: 改变表情 (01微笑, 06羞涩, 04晕, 08生气, 05难过, 07吃惊, 03闭眼)\n" +
             "- <pet_mtn>0-5</pet_mtn>: 执行动作 (0害羞, 1摇头, 2点头, 3欢迎, 4旋转, 5跳舞)\n" +
             "请在对话中适时嵌入这些指令喵！",
    Plugins = new HashSet<Type> 
    { 
        typeof(InterpreterService), 
        typeof(PetService),
        typeof(OpenAIChatService),
        typeof(ChatService) // 添加 ChatService 实现自动对话逻辑
    }
};

// 5. 创建 ChatActivity
Console.WriteLine("[System] 正在初始化 AI 环境，请稍候...");
var chatActivity = await ChatActivity.Create(
    character, 
    configSystem, 
    null,
    new object[] { 
        configSystem, 
        storageSystem, 
        chatWindow
    }
);

// 显示当前的提示词
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("\n[Combined System Prompt]");
foreach (var msg in chatActivity.ChatBot.ChatHistory.Where(m => m.Role == AuthorRole.System))
{
    Console.WriteLine(msg.Content);
}
Console.ResetColor();
Console.WriteLine("--------------------------------------------------\n");

Console.WriteLine("[System] AI 真央已苏醒！你可以开始和她聊天了喵。(输入 'exit' 退出)");
Console.WriteLine("--------------------------------------------------");

// 6. 交互循环
while (true)
{
    Console.Write("> ");
    string? input = Console.ReadLine();
    
    if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "exit")
        break;

    // 只需要将消息添加到 ChatWindow，ChatService 会自动处理并调用 ChatBot
    chatWindow.AddMessage(new ChatMessage { content = input, isUser = true });
    
    // 给一点时间让异步对话开始
    await Task.Delay(500);
}

// 7. 清理
Console.WriteLine("[System] 正在关闭...");
await chatActivity.DisposeAsync();
Console.WriteLine("[System] 再见喵！");
