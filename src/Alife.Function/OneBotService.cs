using System.ComponentModel;
using System.Net.WebSockets;
using System.Text;
using Alife.Abstractions;
using Alife.Interpreter;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.OfficialPlugins;

[Plugin("OneBot-QQ聊天", "连接 OneBot 服务器，实现 QQ 消息收发、群聊监听及 @ 响应。")]
public class OneBotService : Plugin
{
    private ClientWebSocket _ws = new();
    private ChatActivity _chatActivity = null!;
    private string _wsUrl = "ws://127.0.0.1:3001";
    private long _ownerId = 0;
    private long _botId = 0;
    private bool _isGroupEnabled = true;

    // 运行时上下文，分类型缓存最后一次互动的目标
    private long _lastPrivateTarget = 0;
    private long _lastGroupTarget = 0;
    private string _lastType = "private"; // 整体最后一次互动的类型

    // 群消息缓存：groupId -> 内容序列
    private readonly Dictionary<long, StringBuilder> _groupBuffers = new();

    private readonly ConfigurationSystem _configurationSystem;

    public OneBotService(ConfigurationSystem configurationSystem, InterpreterService interpreterService)
    {
        _configurationSystem = configurationSystem;
        interpreterService.RegisterHandler(this);
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        var config = _configurationSystem.GetConfiguration(typeof(OneBotService)) as OneBotConfig ?? new OneBotConfig();
        _isGroupEnabled = config.IsGroupEnabled;
        Console.WriteLine($"[OneBot] 插件唤醒，群消息监控状态: {(_isGroupEnabled ? "开启" : "关闭")}");
        return Task.CompletedTask;
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        _chatActivity = chatActivity;
        
        var config = _configurationSystem.GetConfiguration(typeof(OneBotService)) as OneBotConfig ?? new OneBotConfig();
        _wsUrl = config.Url ?? "ws://127.0.0.1:3001";
        _ownerId = config.OwnerId;

        _ = Task.Run(GlobalFlushLoop);
        await ConnectAsync();
    }

    private Task FlushGroupBuffer(long groupId)
    {
        string batch;
        lock (_groupBuffers)
        {
            if (!_groupBuffers.TryGetValue(groupId, out var sb)) return Task.CompletedTask;
            batch = sb.ToString();
            _groupBuffers.Remove(groupId);
        }
        Console.WriteLine($"[OneBot] 缓冲区强制刷新：推送群 {groupId} 的聚合消息。");
        _chatActivity.ChatBot.Poke(batch);
        return Task.CompletedTask;
    }

    private async Task GlobalFlushLoop()
    {
        while (true)
        {
            await Task.Delay(16000);
            
            Dictionary<long, string> batches = new();
            lock (_groupBuffers)
            {
                if (_groupBuffers.Count > 0)
                {
                    foreach (var pair in _groupBuffers)
                    {
                        batches[pair.Key] = pair.Value.ToString();
                    }
                    _groupBuffers.Clear();
                }
            }

            foreach (var batch in batches.Values)
            {
                Console.WriteLine("[OneBot] 全局周期同步：批量推送已缓存的群消息。");
                _chatActivity.ChatBot.Poke(batch);
            }
        }
    }

    [XmlHandler]
    [Description("发送 QQ 消息。target: QQ/群号 (可选，默认回复当前类型)；type: 'private'/'group' (可选)。内容写在标签中间。")]
    public async Task QChat(XmlTagContext ctx, long target = 0, string type = "")
    {
        if (ctx.Status != TagStatus.Closing && ctx.Status != TagStatus.OneShot) return;
        
        string message = ctx.FullContent;
        if (string.IsNullOrWhiteSpace(message)) return;

        // 确定类型：显式指定 > 上次互动类型
        string finalType = !string.IsNullOrEmpty(type) ? type : _lastType;
        
        // 确定目标：显式指定 > 该类型下最后一次互动的目标
        long finalTarget = target != 0 ? target : (finalType == "group" ? _lastGroupTarget : _lastPrivateTarget);

        if (finalTarget == 0)
        {
            Console.WriteLine($"[OneBot] 发送失败：无法确定 {finalType} 类型下的目标 ID (请告知 AI 明确目标)。");
            return;
        }

        string action = finalType == "group" ? "send_group_msg" : "send_private_msg";
        
        var payload = new {
            action = action,
            @params = new Dictionary<string, object> {
                { finalType == "group" ? "group_id" : "user_id", finalTarget },
                { "message", message }
            }
        };

        var json = JsonConvert.SerializeObject(payload);
        if (_ws.State == WebSocketState.Open)
        {
            await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), 
                WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine($"[OneBot] 已通过 {action} 发送至 {finalTarget}: {message}");
        }
        else
        {
            Console.WriteLine($"[OneBot] 发送失败，WebSocket 连接未开启: {finalTarget}");
        }
    }

    [XmlHandler]
    [Description("开启或关闭普通群聊消息监听。关闭后仅响应私聊和 @ 提到。")]
    public void QToggleGroup(XmlTagContext ctx, bool enabled)
    {
        if (ctx.Status != TagStatus.Closing && ctx.Status != TagStatus.OneShot) return;

        UpdateGroupMonitoring(enabled);
        
        string stateStr = enabled ? "开启" : "关闭";
        Console.WriteLine($"[OneBot] 群聊监听已人工切换为: {stateStr}");
        _chatActivity.ChatBot.Poke($"[系统] 群聊监听已成功{stateStr}");
    }

    private void UpdateGroupMonitoring(bool enabled)
    {
        _isGroupEnabled = enabled;
        var config = _configurationSystem.GetConfiguration(typeof(OneBotService)) as OneBotConfig ?? new OneBotConfig();
        config.IsGroupEnabled = enabled;
        _configurationSystem.SetConfiguration(typeof(OneBotService), config);
    }

    private async Task ConnectAsync()
    {
        try {
            if (_ws.State == WebSocketState.Open) return;
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(_wsUrl), CancellationToken.None);
            Console.WriteLine($"[OneBot] 成功连接至 WebSocket: {_wsUrl}");
            _ = Task.Run(ReceiveLoop);
        } catch (Exception ex) {
            Console.WriteLine($"[OneBot] 连接失败: {ex.Message}");
        }
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[1024 * 64];
        try {
            while (_ws.State == WebSocketState.Open) {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await HandleMessage(json);
            }
        } catch (Exception ex) {
            Console.WriteLine($"[OneBot] 链路异常: {ex.Message}");
            await Task.Delay(5000);
            _ = ConnectAsync();
        }
    }

    private async Task HandleMessage(string json)
    {
        try {
            var data = JObject.Parse(json);
            
            // 基础调试：检测 Bot 账号 ID
            if (data["self_id"] != null) {
                long sid = data["self_id"]?.Value<long>() ?? 0;
                if (sid != 0 && _botId != sid) {
                    _botId = sid;
                    Console.WriteLine($"[OneBot] 识别到登录账号 ID: {_botId}");
                }
            }

            string postType = data["post_type"]?.ToString() ?? "unknown";
            if (postType != "message") return;

            string type = data["message_type"]?.ToString() ?? "";
            
            // 归一化提取消息内容
            string message = StringifyMessage(data["message"]);

            long userId = data["user_id"]?.Value<long>() ?? 0;
            long groupId = data["group_id"]?.Value<long>() ?? 0;

            // 重要：如果是机器人自己发的同步/回显消息，必须拦截，否则会造成对话死循环
            if (_botId != 0 && userId == _botId) return;

            // 更新细分上下文 ID（只要是他人消息就更新）
            _lastType = type;
            if (type == "group") {
                _lastGroupTarget = groupId;
            } else {
                _lastPrivateTarget = userId;
            }

            // 格式化消息标题
            string contextTag = type == "group" 
                ? $"[消息来源: 群聊 {groupId}, 正在发言的人 ID: {userId}]（注：如果需要回复此人，请在回复开头加上 [CQ:at,qq={userId}]）"
                : $"[消息来源: 私聊 {userId}]";
                
            string formattedMsg = $"{contextTag} {message}";

            bool isOwner = userId == _ownerId;
            bool isAtMe = _botId != 0 && (message.Contains($"[CQ:at,qq={_botId}]") || message.Contains($"[CQ:at,qq={_botId},"));

            if (isAtMe)
            {
                if (!_isGroupEnabled)
                {
                    UpdateGroupMonitoring(true);
                    Console.WriteLine($"[OneBot] 群 {groupId} 的 @ 提到触发了自动唤醒。");
                }
                
                // 被 @ 时立即清空该群之前的未处理缓存
                if (type == "group") await FlushGroupBuffer(groupId);
            }

            if (type == "private" || isOwner || isAtMe)
            {
                Console.WriteLine($"[OneBot] 转发实时指令 -> AI: {formattedMsg}");
                await _chatActivity.ChatBot.ChatAsync(formattedMsg);
            }
            else if (type == "group" && _isGroupEnabled)
            {
                lock (_groupBuffers)
                {
                    if (!_groupBuffers.TryGetValue(groupId, out var sb))
                    {
                        sb = new StringBuilder();
                        _groupBuffers[groupId] = sb;
                    }
                    sb.AppendLine(formattedMsg);
                }
                Console.WriteLine($"[OneBot] 已记录群 {groupId} 缓存消息。");
            }
        } catch (Exception ex) {
            Console.WriteLine($"[OneBot] 处理消息异常: {ex.Message}");
        }
    }

    private string StringifyMessage(JToken? token)
    {
        if (token == null) return string.Empty;
        if (token.Type == JTokenType.String) return token.ToString();
        if (token.Type == JTokenType.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in token)
            {
                string segmentType = item["type"]?.ToString() ?? "text";
                var data = item["data"];
                if (segmentType == "text")
                {
                    sb.Append(data?["text"]?.ToString());
                }
                else if (segmentType == "at")
                {
                    sb.Append($"[CQ:at,qq={data?["qq"]}]");
                }
                else if (segmentType == "face")
                {
                    sb.Append($"[CQ:face,id={data?["id"]}]");
                }
                else if (segmentType == "image")
                {
                    sb.Append($"[CQ:image,file={data?["file"]}]");
                }
                else
                {
                    // 通用 CQ 码转化
                    var args = data != null ? string.Join(",", data.Children<JProperty>().Select(p => $"{p.Name}={p.Value}")) : "";
                    sb.Append($"[CQ:{segmentType}{(string.IsNullOrEmpty(args) ? "" : "," + args)}]");
                }
            }
            return sb.ToString();
        }
        return token.ToString();
    }
}

public class OneBotConfig
{
    public string Url { get; set; } = "ws://127.0.0.1:3001";
    public long OwnerId { get; set; }
    public bool IsGroupEnabled { get; set; } = true;
}
