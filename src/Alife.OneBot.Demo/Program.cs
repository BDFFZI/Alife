using System.Net.WebSockets;
using System.Text;
using Alife;
using Alife.Abstractions;
using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.OneBotDemo;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== Alife OneBot AI Plugin Demo ===");

        // 1. 初始化 Alife 系统
        var storageSystem = new StorageSystem();
        var pluginSystem = new PluginSystem(storageSystem);
        var configSystem = new ConfigurationSystem(storageSystem);
        var chatWindow = new ChatWindow();

        // 2. 配置 (针对 OneBotService 插件)
        configSystem.SetConfiguration(typeof(OneBotService), new OneBotConfig {
            Url = "ws://127.0.0.1:3001",
            OwnerId = 1330958515L // 请替换为实际 QQ
        });

        // 3. 配置角色 (真央)
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
                typeof(ChatService)
            }
        };

        // 4. 创建 AI 会话环境
        Console.WriteLine("[System] 正在加载 AI 核心及插件...");
        var chatActivity = await ChatActivity.Create(
            character,
            configSystem,
            null,
            new object[] { configSystem, storageSystem, chatWindow }
        );

        // 订阅历史记录变化，实时在控制台输出完整对话流
        chatActivity.ChatBot.ChatHistoryAdd += (msg) => {
            string roleColor = msg.Role == AuthorRole.User ? "User" : (msg.Role == AuthorRole.Assistant ? "AI" : "System");
            Console.WriteLine($"\n>>> [{roleColor}] {msg.Content}");
        };

        Console.WriteLine("[System] OneBot 插件已启动喵！");
        
        // 5. 保持运行
        await Task.Delay(-1);
    }
}
