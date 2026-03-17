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

    // 表情包资产：文件名 -> 完整路径
    private readonly Dictionary<string, string> _emoteInventory = new();
    private readonly List<string> _emoteCategories = new();

    // 运行时上下文，分类型缓存最后一次互动的目标
    private long _lastPrivateTarget = 0;
    private long _lastGroupTarget = 0;
    private string _lastType = "private"; // 整体最后一次互动的类型

    // 简单的出站去重：防止 AI 在短时间内对同一目标发送完全相同的消息
    private string _lastSentMessage = "";
    private long _lastSentTarget = 0;

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
        
        // 扫描并注入表情包库说明
        string emotePrompt = ScanEmotesToPrompt();
        if (!string.IsNullOrEmpty(emotePrompt))
        {
            context.contextBuilder.ChatHistory.AddSystemMessage(emotePrompt);
        }

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

    private string ScanEmotesToPrompt()
    {
        try
        {
            string emotePath = Path.Combine(AppContext.BaseDirectory, "Storage", "Emotes");
            if (!Directory.Exists(emotePath)) Directory.CreateDirectory(emotePath);

            _emoteInventory.Clear();
            _emoteCategories.Clear();

            // 1. 扫描顶级文件 (作为直接表情项)
            var topLevelFiles = Directory.GetFiles(emotePath, "*.*")
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".gif") || f.EndsWith(".jpeg"))
                .ToList();

            foreach (var file in topLevelFiles)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                _emoteInventory[name] = Path.GetFullPath(file);
            }

            // 2. 扫描顶级文件夹 (作为分类/Tag)
            var subDirs = Directory.GetDirectories(emotePath);
            foreach (var dir in subDirs)
            {
                _emoteCategories.Add(Path.GetFileName(dir));
            }

            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                { "angry", "讨厌/不屑" },
                { "tongue", "调皮" },
                { "judge", "审视" },
                { "eh", "无语" },
                { "shock", "震惊" },
                { "cult", "威严" },
                { "害怕", "害怕/怂了" },
                { "讨厌", "讨厌/不想要" }
            };

            var sb = new StringBuilder();
            sb.AppendLine("# QQ 表情包使用指南");
            sb.AppendLine("你拥有一个强大的表情包库。当你想表达特定情绪或与群友斗图时，请使用 `<qimage />` 标签。");
            sb.AppendLine("**使用规则：**");
            sb.AppendLine("1. **标签格式**：必须使用 `<Interpreter><qimage file=\"名称\" /></Interpreter>`。");
            sb.AppendLine("2. **智能解析**：");
            sb.AppendLine("   - 如果“名称”是一个**具体文件名**，系统会发送该图。");
            sb.AppendLine("   - 如果“名称”是一个**分类文件夹**，系统会从中随机挑一张图发送。");
            
            if (_emoteInventory.Count > 0)
            {
                sb.AppendLine("\n**【精品表情】（指定文件名即可调用）：**");
                foreach (var kvp in _emoteInventory)
                {
                    string desc = descriptions.TryGetValue(kvp.Key, out var d) ? d : "通用";
                    sb.AppendLine($"- **{kvp.Key}**: (用途: {desc})");
                }
            }

            if (_emoteCategories.Count > 0)
            {
                sb.AppendLine("\n**【海量分类】（指定分类名将随机发图）：**");
                sb.AppendLine("可用分类：" + string.Join("、", _emoteCategories));
            }

            sb.AppendLine("\n**综合示例：**");
            sb.AppendLine("- 发送一张讨厌的图：`<Interpreter><qimage file=\"angry\" /></Interpreter>`");
            sb.AppendLine("- 发送一张程序员相关的随机图：`<Interpreter><qimage file=\"程序员\" /></Interpreter>`");
            
            Console.WriteLine($"[OneBot] 表情库扫描完成：{_emoteInventory.Count} 个独立表情，{_emoteCategories.Count} 个分类。");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OneBot] 扫描表情包异常: {ex.Message}");
            return string.Empty;
        }
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
        Console.WriteLine($"[OneBot] 强制刷新群 {groupId} 缓存：\n---\n{batch}\n---");
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

            foreach (var pair in batches)
            {
                Console.WriteLine($"[OneBot] 循环推送群 {pair.Key} 缓存：\n---\n{pair.Value}\n---");
                _chatActivity.ChatBot.Poke(pair.Value);
            }
        }
    }

    [XmlHandler]
    [Description("发送 QQ 文本消息。")]
    public async Task QChat(XmlTagContext ctx, [Description("内容"), XmlTagContent] string message = "", [Description("QQ/群号")] long target = 0, [Description("'private'/'group'")] string type = "")
    {
        if (ctx.Status != TagStatus.Closing && ctx.Status != TagStatus.OneShot) return;
        
        string msgToSend = !string.IsNullOrEmpty(message) ? message : ctx.FullContent;
        if (string.IsNullOrWhiteSpace(msgToSend)) return;

        // 确定类型：显式指定 > 上次互动类型
        string finalType = !string.IsNullOrEmpty(type) ? type : _lastType;
        
        // 确定目标：显式指定 > 该类型下最后一次互动的目标
        long finalTarget = target != 0 ? target : (finalType == "group" ? _lastGroupTarget : _lastPrivateTarget);

        if (finalTarget == 0)
        {
            Console.WriteLine($"[OneBot] 发送失败：无法确定 {finalType} 类型下的目标 ID。");
            return;
        }

        // 程序端去重：如果 AI 试图对同一目标重复完全相同的话，直接拦截
        if (finalTarget == _lastSentTarget && msgToSend == _lastSentMessage)
        {
            Console.WriteLine($"[OneBot] 拦截重复发送请求 (Target: {finalTarget}): {msgToSend.Replace("\n", " ")}");
            return;
        }
        _lastSentMessage = msgToSend;
        _lastSentTarget = finalTarget;

        string action = finalType == "group" ? "send_group_msg" : "send_private_msg";
        
        var payload = new {
            action = action,
            @params = new Dictionary<string, object> {
                { finalType == "group" ? "group_id" : "user_id", finalTarget },
                { "message", msgToSend }
            }
        };

        var json = JsonConvert.SerializeObject(payload);
        if (_ws.State == WebSocketState.Open)
        {
            await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), 
                WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine($"[OneBot] 已通过 {action} 发送至 {finalTarget}: {msgToSend}");
        }
        else
        {
            Console.WriteLine($"[OneBot] 发送失败，WebSocket 连接未开启: {finalTarget}");
        }
    }

    [XmlHandler]
    [Description("发送 QQ 图片。")]
    public async Task QImage(XmlTagContext ctx, [Description("表情名称 (文件名或分类名)")] string file = "", [Description("QQ/群号")] long target = 0, [Description("'private'/'group'")] string type = "", [XmlTagContent] string _ = "")
    {
        if (ctx.Status != TagStatus.Closing && ctx.Status != TagStatus.OneShot) return;

        if (string.IsNullOrWhiteSpace(file)) return;

        string emoteRoot = Path.Combine(AppContext.BaseDirectory, "Storage", "Emotes");
        string finalFile = "";

        // 1. 优先检查是否是分类文件夹
        string categoryPath = Path.Combine(emoteRoot, file);
        if (Directory.Exists(categoryPath))
        {
            var files = Directory.GetFiles(categoryPath, "*.*")
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".gif") || f.EndsWith(".jpeg"))
                .ToList();
            
            if (files.Count > 0)
            {
                finalFile = files[new Random().Next(files.Count)];
                Console.WriteLine($"[OneBot] 从分类 '{file}' 中随机选择表情: {Path.GetFileName(finalFile)}");
            }
        }
        
        // 2. 检查是否是精确文件名
        if (string.IsNullOrEmpty(finalFile))
        {
            if (_emoteInventory.TryGetValue(file, out var path))
            {
                finalFile = path;
            }
            else
            {
                // 尝试补全后缀查找
                var possibleExts = new[] { ".jpg", ".png", ".gif", ".jpeg" };
                foreach (var ext in possibleExts)
                {
                    string tryPath = Path.Combine(emoteRoot, file + ext);
                    if (File.Exists(tryPath))
                    {
                        finalFile = tryPath;
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(finalFile)) return;

        string finalType = !string.IsNullOrEmpty(type) ? type : _lastType;
        long finalTarget = target != 0 ? target : (finalType == "group" ? _lastGroupTarget : _lastPrivateTarget);

        if (finalTarget == 0)
        {
            Console.WriteLine($"[OneBot] 图片发送失败：无法确定 {finalType} 类型下的目标 ID。");
            return;
        }

        string message = $"[CQ:image,file={finalFile}]";
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
            Console.WriteLine($"[OneBot] 已通过 {action} 发送图片至 {finalTarget}: {finalFile}");
        }
        else
        {
            Console.WriteLine($"[OneBot] 图片发送失败，WebSocket 未开启: {finalTarget}");
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
            
            // 主动请求登录信息以获取 Bot ID
            var getInfo = new { action = "get_login_info", @params = new { }, echo = "init_bot_id" };
            var json = JsonConvert.SerializeObject(getInfo);
            await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), 
                WebSocketMessageType.Text, true, CancellationToken.None);

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
            if (_ws.State != WebSocketState.Aborted)
                Console.WriteLine($"[OneBot] 链路异常: {ex.Message}");
            await Task.Delay(5000);
            _ = ConnectAsync();
        }
    }

    private async Task HandleMessage(string json)
    {
        try {
            var data = JObject.Parse(json);
            
            // 处理 API 响应（如 get_login_info）
            if (data["echo"]?.ToString() == "init_bot_id")
            {
                var userIdNode = data["data"]?["user_id"];
                if (userIdNode != null)
                {
                    _botId = userIdNode.Value<long>();
                    Console.WriteLine($"[OneBot] 初始化成功，检测到 Bot ID: {_botId}");
                }
                return;
            }

            // 基础调试：从任何包含 self_id 的推送中同步 Bot ID
            if (data["self_id"] != null) {
                long sid = data["self_id"]?.Value<long>() ?? 0;
                if (sid != 0 && _botId != sid) {
                    _botId = sid;
                    Console.WriteLine($"[OneBot] 同步 Bot 账号 ID: {_botId}");
                }
            }

            string postType = data["post_type"]?.ToString() ?? "unknown";
            if (postType != "message") return;

            string type = data["message_type"]?.ToString() ?? "";
            
            // 归一化提取消息内容
            string message = StringifyMessage(data["message"]);

            long userId = data["user_id"]?.Value<long>() ?? 0;
            long groupId = data["group_id"]?.Value<long>() ?? 0;
            long selfIdInPacket = data["self_id"]?.Value<long>() ?? 0;

            // 绝杀屏蔽：如果是机器人自己发的消息（不论是回显还是同步），直接无视屏蔽
            // 优先使用数据包自带的 self_id 进行对比，这是最瞬时且可靠的判断方式
            if (selfIdInPacket != 0 && userId == selfIdInPacket) return;
            if (_botId != 0 && userId == _botId) return;
            
            // 兜底：某些 OneBot 版本的 sender 结构体检查
            var senderId = data["sender"]?["user_id"]?.Value<long>() ?? 0;
            if (selfIdInPacket != 0 && senderId == selfIdInPacket) return;
            if (_botId != 0 && senderId == _botId) return;

            // 更新细分上下文 ID（只要是他人消息就更新）
            _lastType = type;
            if (type == "group") {
                _lastGroupTarget = groupId;
            } else {
                _lastPrivateTarget = userId;
            }

            // 格式化消息标题：去除冗余的回复指令，让 AI 根据 System Prompt 里的规则自行判断
            string contextTag = type == "group" 
                ? $"[群聊 {groupId}, 发言人 ID: {userId}]"
                : $"[私聊 {userId}]";
                
            string formattedMsg = $"{contextTag} {message}";

            bool isOwner = userId == _ownerId;
            bool isAtMe = _botId != 0 && (message.Contains($"[CQ:at,qq={_botId}]") || message.Contains($"[CQ:at,qq={_botId},"));

            if (type == "private")
            {
                Console.WriteLine($"[OneBot] 转发私聊实时消息 -> AI: {formattedMsg}");
                await _chatActivity.ChatBot.ChatAsync(formattedMsg);
            }
            else if (type == "group" && (_isGroupEnabled || isAtMe))
            {
                if (isAtMe && !_isGroupEnabled)
                {
                    UpdateGroupMonitoring(true);
                    Console.WriteLine($"[OneBot] 群 {groupId} 的 @ 提到触发了自动唤醒。");
                    _chatActivity.ChatBot.Poke($"[系统] 已由针对你的艾特触发自动开启群聊监听。");
                }

                lock (_groupBuffers)
                {
                    if (!_groupBuffers.TryGetValue(groupId, out var sb))
                    {
                        sb = new StringBuilder();
                        _groupBuffers[groupId] = sb;
                    }
                    sb.AppendLine(formattedMsg);
                }
                Console.WriteLine($"[OneBot] 已记录来自群 {groupId} 的缓存消息 (已进入 Poke 队列)。");
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
                var dataObj = item["data"] as JObject;
                if (segmentType == "text")
                {
                    sb.Append(dataObj?["text"]?.ToString());
                }
                else if (segmentType == "at")
                {
                    sb.Append($"[CQ:at,qq={dataObj?["qq"]}]");
                }
                else if (segmentType == "face")
                {
                    sb.Append($"[CQ:face,id={dataObj?["id"]}]");
                }
                else if (segmentType == "image")
                {
                    sb.Append($"[CQ:image,file={dataObj?["file"]}]");
                }
                else
                {
                    // 通用字段展开
                    var props = dataObj?.Properties();
                    var args = props != null ? string.Join(",", props.Select(p => $"{p.Name}={p.Value}")) : "";
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
