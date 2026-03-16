using System.Net.WebSockets;
using System.Text;
using Alife;
using Alife.Abstractions;
using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.OneBotDemo;

class Program
{
    private static ClientWebSocket _ws = new();
    private static ChatWindow _chatWindow = new();
    private static ChatActivity? _chatActivity;
    private const string WsUrl = "ws://127.0.0.1:3001";

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== Alife OneBot AI Bridge Demo ===");

        // 1. 初始化 Alife 系统
        var storageSystem = new StorageSystem();
        var pluginSystem = new PluginSystem(storageSystem);
        var configSystem = new ConfigurationSystem(storageSystem);

        // 2. 配置角色 (真央)
        var character = new Character {
            ID = "OneBotMao",
            Name = "真央",
            Prompt = "你是一个集成在 QQ 中的 AI 助手，名叫真央。你非常活泼，喜欢用猫娘语说话（每句话带喵）。" +
                     "你正在通过 OneBot 协议与用户交流。",
            Plugins = new HashSet<Type> {
                typeof(InterpreterService),
                typeof(OpenAIChatService),
                typeof(ChatService)
            }
        };

        // 3. 创建 AI 会话环境
        Console.WriteLine("[System] 正在加载 AI 核心...");
        _chatActivity = await ChatActivity.Create(
            character,
            configSystem,
            null,
            new object[] { configSystem, storageSystem, _chatWindow }
        );

        // 5. 连接 OneBot WebSocket
        await ConnectWs();

        Console.WriteLine("[System] 已准备就绪！请向机器人发送私聊消息开始对话喵。");
        
        // 保持运行
        while (true)
        {
            await Task.Delay(1000);
            if (_ws.State != WebSocketState.Open)
            {
                Console.WriteLine("[System] 连接断开，尝试重连...");
                await ConnectWs();
            }
        }
    }

    private static async Task ConnectWs()
    {
        try {
            if (_ws.State == WebSocketState.Open) return;
            _ws = new ClientWebSocket();
            Console.WriteLine($"[WS] 正在连接 {WsUrl}...");
            await _ws.ConnectAsync(new Uri(WsUrl), CancellationToken.None);
            Console.WriteLine("[WS] 连接成功！");

            _ = Task.Run(ReceiveLoop);
        } catch (Exception ex) {
            Console.WriteLine($"[WS] 连接失败: {ex.Message}");
        }
    }

    private static async Task ReceiveLoop()
    {
        var buffer = new byte[1024 * 8];
        try {
            while (_ws.State == WebSocketState.Open) {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await HandleOneBotMessage(message);
            }
        } catch (Exception ex) {
            Console.WriteLine($"[WS] 接收循环异常: {ex.Message}");
        }
    }

    private static async Task HandleOneBotMessage(string json)
    {
        try {
            var data = JObject.Parse(json);
            
            // 只需要私聊消息
            if (data["post_type"]?.ToString() == "message" && 
                data["message_type"]?.ToString() == "private")
            {
                string? content = data["message"]?.ToString();
                string? userId = data["user_id"]?.ToString();

                if (!string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(userId))
                {
                    Console.WriteLine($"[QQ] 收到来自 {userId} 的消息: {content}");
                    
                    // 直接调用 ChatBot 进行对话（不需要 ChatWindow 转发）
                    if (_chatActivity != null)
                    {
                        string response = await _chatActivity.ChatBot.ChatAsync(content);
                        Console.WriteLine($"[AI] 回复 {userId}: {response}");
                        await SendToQq(response, userId);
                    }
                }
            }
        } catch { /* 忽略解析错误和心跳包 */ }
    }

    private static async Task SendToQq(string content, string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return;

        try {
            var payload = new Dictionary<string, object>
            {
                { "action", "send_private_msg" },
                { "params", new Dictionary<string, object>
                    {
                        { "user_id", long.Parse(userId) },
                        { "message", content }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(payload);
            Console.WriteLine($"[QQ] 正在回复 {userId}: {content}");
            await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), 
                               WebSocketMessageType.Text, true, CancellationToken.None);
        } catch (Exception ex) {
            Console.WriteLine($"[QQ] 发送失败: {ex.Message}");
        }
    }
}
