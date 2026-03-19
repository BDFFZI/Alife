using System.ComponentModel;
using System.Text;
using Alife.Abstractions;
using Alife.Interpreter;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneBotClient = Alife.OneBot.OneBotClient;
using OneBotConfig = Alife.OneBot.OneBotConfig;
using OneBotEvent = Alife.OneBot.OneBotEvent;

namespace Alife.OfficialPlugins;

[Plugin("OneBot-QQ聊天", "连接 OneBot 服务器，实现 QQ 消息收发、群聊监听及 @ 响应。")]
[Description("此服务让获得接收QQ消息，以及向QQ发送消息的能力。")]
public class QChatService : Plugin, IAsyncDisposable
{
    [XmlHandler]
    [Description("发送 QQ 文本消息。")]
    public async Task QChat(XmlTagContext ctx, [Description("内容"), XmlTagContent] string message = "", [Description("QQ/群号")] long target = 0, [Description("'private'/'group'")] string type = "")
    {
        if (ctx.Status != TagStatus.Closing && ctx.Status != TagStatus.OneShot) return;

        string msgToSend = !string.IsNullOrEmpty(message) ? message : ctx.FullContent;
        if (string.IsNullOrWhiteSpace(msgToSend) || _client == null) return;

        // 确定类型：显式指定 > 上次互动类型
        string finalType = !string.IsNullOrEmpty(type) ? type : _lastType;

        // 确定目标：显式指定 > 该类型下最后一次互动的目标
        long finalTarget = target != 0 ? target : (finalType == "group" ? _lastGroupTarget : _lastPrivateTarget);

        if (finalTarget == 0)
        {
            Console.WriteLine($"[QChatService] 发送失败：无法确定 {finalType} 类型下的目标 ID。");
            return;
        }

        // 程序端去重：如果 AI 试图对同一目标重复完全相同的话，直接拦截
        if (finalTarget == _lastSentTarget && msgToSend == _lastSentMessage)
        {
            Console.WriteLine($"[QChatService] 拦截重复发送请求 (Target: {finalTarget}): {msgToSend.Replace("\n", " ")}");
            return;
        }
        _lastSentMessage = msgToSend;
        _lastSentTarget = finalTarget;

        string action = finalType == "group" ? "send_group_msg" : "send_private_msg";

        var @params = new Dictionary<string, object> {
            { finalType == "group" ? "group_id" : "user_id", finalTarget },
            { "message", msgToSend }
        };

        await _client.SendActionAsync(action, @params);
        Console.WriteLine($"[QChatService] 已通过 {action} 发送至 {finalTarget}: {msgToSend}");
    }

    [XmlHandler]
    [Description("发送 QQ 图片。")]
    public async Task QImage(XmlTagContext ctx, [Description("表情名称 (文件名或分类名)")] string file = "", [Description("QQ/群号")] long target = 0, [Description("'private'/'group'")] string type = "",
        [XmlTagContent] string _ = "")
    {
        if (ctx.Status != TagStatus.Closing && ctx.Status != TagStatus.OneShot) return;

        if (string.IsNullOrWhiteSpace(file) || _client == null) return;

        string emoteRoot = Path.Combine(storageSystem.GetStoragePath(), "Emotes");
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
                //Console.WriteLine($"[QChatService] 从分类 '{file}' 中随机选择表情: {Path.GetFileName(finalFile)}");
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
            Console.WriteLine($"[QChatService] 图片发送失败：无法确定 {finalType} 类型下的目标 ID。");
            return;
        }

        string message = $"[CQ:image,file={finalFile}]";
        string action = finalType == "group" ? "send_group_msg" : "send_private_msg";

        var @params = new Dictionary<string, object> {
            { finalType == "group" ? "group_id" : "user_id", finalTarget },
            { "message", message }
        };

        await _client.SendActionAsync(action, @params);
        //Console.WriteLine($"[QChatService] 已通过 {action} 发送图片至 {finalTarget}: {finalFile}");
    }

    [XmlHandler]
    [Description("开启或关闭普通群聊消息监听。关闭后仅响应私聊和 @ 提到。（默认为开启状态）")]
    public void QToggleGroup(XmlTagContext ctx, bool enabled)
    {
        if (ctx.Status != TagStatus.Closing && ctx.Status != TagStatus.OneShot) return;

        UpdateGroupMonitoring(enabled);

        string stateStr = enabled ? "开启" : "关闭";
        Console.WriteLine($"[QChatService] 群聊监听已人工切换为: {stateStr}");
    }

    OneBotClient? _client;
    ChatActivity _chatActivity = null!;
    long _ownerId = 0;
    bool _isGroupEnabled = true;

    // 表情包资产：文件名 -> 完整路径
    readonly Dictionary<string, string> _emoteInventory = new();
    readonly List<string> _emoteCategories = new();

    // 运行时上下文，分类型缓存最后一次互动的目标
    long _lastPrivateTarget = 0;
    long _lastGroupTarget = 0;
    string _lastType = "private"; // 整体最后一次互动的类型

    // 简单的出站去重：防止 AI 在短时间内对同一目标发送完全相同的消息
    string _lastSentMessage = "";
    long _lastSentTarget = 0;

    // 群消息缓存：groupId -> 内容序列
    readonly Dictionary<long, StringBuilder> _groupBuffers = new();

    readonly ConfigurationSystem configurationSystem;
    readonly StorageSystem storageSystem;

    public QChatService(ConfigurationSystem configurationSystem, InterpreterService interpreterService, StorageSystem storageSystem)
    {
        this.configurationSystem = configurationSystem;
        this.storageSystem = storageSystem;

        interpreterService.RegisterHandler(this);
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        var config = configurationSystem.GetConfiguration(typeof(QChatService)) as OneBotConfig ?? new OneBotConfig();
        _isGroupEnabled = config.IsGroupEnabled;

        // 扫描并注入表情包库说明
        string emotePrompt = ScanEmotesToPrompt();
        if (!string.IsNullOrEmpty(emotePrompt))
        {
            context.contextBuilder.ChatHistory.AddSystemMessage(emotePrompt);
        }

        Console.WriteLine($"[QChatService] 插件唤醒，群消息监控状态: {(_isGroupEnabled ? "开启" : "关闭")}");
        return Task.CompletedTask;
    }
    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        _chatActivity = chatActivity;

        var config = configurationSystem.GetConfiguration(typeof(QChatService)) as OneBotConfig ?? new OneBotConfig();
        _ownerId = config.OwnerId;

        _client = new OneBotClient(config);
        _client.OnMessageReceived += async (e) => await HandleMessage(e);
        _client.OnConnectionStatusChanged += (connected) => {
            Console.WriteLine($"[QChatService] 连接状态: {(connected ? "已连接" : "已断开")}");
        };

        _ = Task.Run(GlobalFlushLoop);
        await _client.ConnectAsync();
    }
    public async ValueTask DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
        }
    }

    string ScanEmotesToPrompt()
    {
        try
        {
            string emotePath = Path.Combine(storageSystem.GetStoragePath(), "Emotes");
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
            sb.AppendLine("# QChatService 表情包使用指南");
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

            Console.WriteLine($"[QChatService] 表情库扫描完成：{_emoteInventory.Count} 个独立表情，{_emoteCategories.Count} 个分类。");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QChatService] 扫描表情包异常: {ex.Message}");
            return string.Empty;
        }
    }

    async Task GlobalFlushLoop()
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
                //Console.WriteLine($"[QChatService] 循环推送群 {pair.Key} 缓存：\n---\n{pair.Value}\n---");
                _chatActivity.ChatBot.Poke(pair.Value);
            }
        }
    }

    void UpdateGroupMonitoring(bool enabled)
    {
        _isGroupEnabled = enabled;
        var config = configurationSystem.GetConfiguration(typeof(QChatService)) as OneBotConfig ?? new OneBotConfig();
        config.IsGroupEnabled = enabled;
        configurationSystem.SetConfiguration(typeof(QChatService), config);
    }

    async Task HandleMessage(OneBotEvent e)
    {
        try
        {
            string type = e.MessageType;

            // 归一化提取消息内容
            string message = _client?.StringifyMessage(e.Message) ?? "";

            long userId = e.UserId;
            long groupId = e.GroupId;

            // 绝杀屏蔽：如果是机器人自己发的消息，直接无视
            if (_client != null && _client.BotId != 0 && userId == _client.BotId) return;

            // 更新细分上下文 ID
            _lastType = type;
            if (type == "group")
            {
                _lastGroupTarget = groupId;
            }
            else
            {
                _lastPrivateTarget = userId;
            }

            // 格式化消息标题
            string contextTag = type == "group"
                ? $"[QChatService][群聊 {groupId}, 发言人 ID: {userId}]"
                : $"[QChatService][私聊 {userId}]";

            string formattedMsg = $"{contextTag} {message}";

            bool isAtMe = _client != null && _client.BotId != 0 && (message.Contains($"[CQ:at,qq={_client.BotId}]") || message.Contains($"[CQ:at,qq={_client.BotId},"));

            if (type == "private")
            {
                //Console.WriteLine($"[QChatService] 转发私聊实时消息 -> AI: {formattedMsg}");
                await _chatActivity.ChatBot.ChatAsync(formattedMsg);
            }
            else if (type == "group" && (_isGroupEnabled || isAtMe))
            {
                lock (_groupBuffers)
                {
                    if (!_groupBuffers.TryGetValue(groupId, out var sb))
                    {
                        sb = new StringBuilder();
                        _groupBuffers[groupId] = sb;
                    }

                    if (isAtMe && !_isGroupEnabled)
                    {
                        UpdateGroupMonitoring(true);
                        Console.WriteLine($"[QChatService] 群 {groupId} 的 @ 提到触发了自动唤醒。");
                        sb.AppendLine($"[QChatService][系统] 已由针对你的艾特触发自动开启群聊监听。");
                    }

                    //Console.WriteLine($"[QChatService] 已记录来自群 {groupId} 的缓存消息 (已进入 Poke 队列)。");
                    sb.AppendLine(formattedMsg);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QChatService] 处理消息异常: {ex.Message}");
        }
    }
}
