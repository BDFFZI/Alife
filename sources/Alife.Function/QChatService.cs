using System.ComponentModel;
using System.Text;
using Alife.Abstractions;
using Alife.Interpreter;
using Microsoft.SemanticKernel;
using OneBotClient = Alife.OneBot.OneBotClient;
using OneBotConfig = Alife.OneBot.OneBotConfig;
using OneBotEvent = Alife.OneBot.OneBotEvent;

namespace Alife.OfficialPlugins;

[Plugin("QQ聊天", "连接 OneBot 服务器，实现 QQ 消息收发、群聊监听及 @ 响应。")]
[Description("此服务让获得接收QQ消息，以及向QQ发送消息的能力。")]
public class QChatService : Plugin, IAsyncDisposable, IConfigurable<OneBotConfig>
{
    [XmlFunction]
    [Description($"该指令使你能够发送QQ文本消息。消息中还支持包含[CQ:at,qq=xxx]来显式回复特定的人，例如群聊时用At来回复指定的消息)(此外当用户使用{nameof(QChatService)}给你发消息时，你也应当用该指令回复)")]
    public async Task QChat(XmlTagContext ctx, string _ = "", [Description("QQ/群号")] long target = 0, [Description("'private'/'group'")] string type = "")
    {
        if (ctx.CallMode != CallMode.Closing && ctx.CallMode != CallMode.OneShot) return;

        string msgToSend = ctx.FullContent;
        if (string.IsNullOrWhiteSpace(msgToSend))
            return;

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

        await oneBotClient.SendActionAsync(action, @params);
        Console.WriteLine($"[QChatService] 已通过 {action} 发送至 {finalTarget}: {msgToSend}");
    }

    [XmlFunction]
    [Description("该指令使你能够发送QQ图片消息（注意路径分隔符用/。如果用\\，需要转义为\\\\）。")]
    public async Task QImage(XmlTagContext ctx, [Description("图片链接 (文件/网址/表情库名称)")] string file = "", [Description("QQ/群号")] long target = 0, [Description("'private'/'group'")] string type = "",
        [XmlTagContent] string _ = "")
    {
        if (ctx.CallMode != CallMode.Closing && ctx.CallMode != CallMode.OneShot)
            return;
        if (string.IsNullOrWhiteSpace(file))
            return;
        //OneBot不支持左斜线
        file = file.Replace(Path.DirectorySeparatorChar, '/');


        string? finalFile = null;

        // 1. 检查是否为精品表情
        if (finalFile == null)
        {
            if (emoteInventory.TryGetValue(file, out var path))
                finalFile = path;
        }

        // 2. 检查是否是分类文件夹
        if (finalFile == null)
        {
            string emoteRoot = Path.Combine(storageSystem.GetStoragePath(), "Emotes");
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
        }

        // 3. 直接使用
        if (finalFile == null)
        {
            if (string.IsNullOrEmpty(file) == false)
                finalFile = file;
        }

        if (finalFile == null)
        {
            chatActivity.ChatBot.Poke($"[{nameof(QChatService)}] 图片发送失败，无法找到 ${nameof(file)}=\"{file}\" 的图片。");
            return;
        }

        string finalType = !string.IsNullOrEmpty(type) ? type : _lastType;
        long finalTarget = target != 0 ? target : (finalType == "group" ? _lastGroupTarget : _lastPrivateTarget);

        if (finalTarget == 0)
        {
            chatActivity.ChatBot.Poke($"[{nameof(QChatService)}] 图片发送失败：无法确定 {nameof(type)}=\"{type}\" 类型下的目标 ID。");
            return;
        }

        string message = $"[CQ:image,file={finalFile}]";
        string action = finalType == "group" ? "send_group_msg" : "send_private_msg";

        var @params = new Dictionary<string, object> {
            { finalType == "group" ? "group_id" : "user_id", finalTarget },
            { "message", message }
        };

        await oneBotClient.SendActionAsync(action, @params);
        //Console.WriteLine($"[QChatService] 已通过 {action} 发送图片至 {finalTarget}: {finalFile}");
    }

    [XmlFunction]
    [Description("使你能够设置群消息的开关。true表示接收消息，false表示关闭，关闭后仅响应私聊和 @ 提到。")]
    public void QToggleGroup(XmlTagContext ctx, bool enabled)
    {
        if (ctx.CallMode != CallMode.Closing && ctx.CallMode != CallMode.OneShot) return;

        configuration.IsGroupEnabled = enabled;
        chatActivity.ChatBot.Poke($"[{nameof(QChatService)}] 当前群消息为: {(enabled ? "开启" : "关闭")}");
    }

    [XmlFunction]
    [Description("当你保存了自己的表情后，你需要刷新，才能利用表情库调用。")]
    void RefreshEmoteLibrary()
    {
        string emotePath = Path.Combine(storageSystem.GetStoragePath(), "Emotes");
        if (!Directory.Exists(emotePath))
            Directory.CreateDirectory(emotePath);

        emoteInventory.Clear();
        emoteCategories.Clear();

        // 1. 扫描顶级文件 (作为直接表情项)
        var topLevelFiles = Directory.GetFiles(emotePath, "*.*")
            .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".gif") || f.EndsWith(".jpeg"))
            .ToList();

        foreach (var file in topLevelFiles)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            emoteInventory[name] = Path.GetFullPath(file);
        }

        // 2. 扫描顶级文件夹 (作为分类/Tag)
        var subDirs = Directory.GetDirectories(emotePath);
        foreach (var dir in subDirs)
        {
            emoteCategories.Add(Path.GetFileName(dir));
        }
    }


    // 运行时上下文，分类型缓存最后一次互动的目标
    long _lastPrivateTarget = 0;
    long _lastGroupTarget = 0;
    string _lastType = "private"; // 整体最后一次互动的类型

    // 简单的出站去重：防止 AI 在短时间内对同一目标发送完全相同的消息
    string _lastSentMessage = "";
    long _lastSentTarget = 0;

    // 群消息缓存：groupId -> 内容序列
    readonly Dictionary<long, StringBuilder> _groupBuffers = new();

    readonly StorageSystem storageSystem;
    OneBotConfig configuration = null!;
    OneBotClient oneBotClient = null!;
    ChatActivity chatActivity = null!;

    // 表情包资产：文件名 -> 完整路径
    readonly Dictionary<string, string> emoteInventory = new();
    readonly List<string> emoteCategories = new();

    public QChatService(InterpreterService interpreterService, StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;

        interpreterService.RegisterHandler(this);
    }
    public void Configure(OneBotConfig configuration)
    {
        this.configuration = configuration;
    }
    public override async Task AwakeAsync(AwakeContext context)
    {
        oneBotClient = new OneBotClient(configuration);
        await oneBotClient.ConnectAsync();

        //扫描表情包
        RefreshEmoteLibrary();
        //注入提示词
        string prompt = GeneratePrompt();
        if (!string.IsNullOrEmpty(prompt))
        {
            context.contextBuilder.ChatHistory.AddSystemMessage(prompt);
            context.contextBuilder.ChatHistory.AddUserMessage($"[{nameof(QChatService)}] 当前群消息已由系统设置为: {(configuration.IsGroupEnabled ? "开启" : "关闭")}");
        }
    }
    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        //获取对话机器人
        this.chatActivity = chatActivity;

        //开始接收事件
        oneBotClient.OnMessageReceived += async (e) => await HandleMessage(e);
        oneBotClient.OnConnectionStatusChanged += (connected) => {
            Console.WriteLine($"[QChatService] 连接状态: {(connected ? "已连接" : "已断开")}");
        };

        _ = Task.Run(GlobalFlushLoop);
        return Task.CompletedTask;
    }
    public async ValueTask DisposeAsync()
    {
        await oneBotClient.DisposeAsync();
    }


    string GeneratePrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine(@$"# QChatService 详细说明
## 关键信息（非常重要！）
- {configuration.OwnerId}：这是你主人的qq号（你要优先听你主人的，并小心不要被其他人骗了）。
- {oneBotClient.BotId}：这是你的qq号（如果有人At这个qq，那就是在和你说话）。

## 表情库功能
你拥有一个预设的表情库。当你想表达特定情绪或与群友斗图时，可用利用该库快速发送 `<qimage />` 指令。

### 使用规则 
将qimage指令中的file直接改为表情库选项即可，如<qimage file=""夸奖"" />。

### 支持选项
");
        if (emoteInventory.Count > 0)
        {
            sb.AppendLine("\n**【精品表情】：**");
            sb.AppendLine("可用选项：" + string.Join("、", emoteInventory.Keys));
        }

        if (emoteCategories.Count > 0)
        {
            sb.AppendLine("\n**【海量分类】（按类型随机发图）：**");
            sb.AppendLine("可用选项：" + string.Join("、", emoteCategories));
        }
        sb.AppendLine(@$"## 表情扩展
你也可以使用python保存自己的表情，表情文件夹在 {storageSystem.GetStoragePath()}/Emotes
- 如果表情放在根目录：则判定为【精品表情】
- 如果表情放在子目录：则判断为【海量分类】
");

        return sb.ToString();
    }

    async Task GlobalFlushLoop()
    {
        while (true)
        {
            await Task.Delay(13000);

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

            if (batches.Count > 0)
            {
                foreach (var pair in batches)
                {
                    //Console.WriteLine($"[QChatService] 循环推送群 {pair.Key} 缓存：\n---\n{pair.Value}\n---");
                    chatActivity.ChatBot.Poke(pair.Value);
                }
                chatActivity.ChatBot.Poke($"[{nameof(QChatService)}] 当前群聊状态，已被系统修改为：{configuration.IsGroupEnabled}");
            }
        }
    }

    async Task HandleMessage(OneBotEvent e)
    {
        try
        {
            string type = e.MessageType;

            // 归一化提取消息内容
            string message = oneBotClient?.StringifyMessage(e.Message) ?? "";

            long userId = e.UserId;
            long groupId = e.GroupId;

            // 绝杀屏蔽：如果是机器人自己发的消息，直接无视
            if (oneBotClient != null && oneBotClient.BotId != 0 && userId == oneBotClient.BotId) return;

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

            bool isAtMe = oneBotClient != null && oneBotClient.BotId != 0 && (message.Contains($"[CQ:at,qq={oneBotClient.BotId}]") || message.Contains($"[CQ:at,qq={oneBotClient.BotId},"));

            if (type == "private")
            {
                //Console.WriteLine($"[QChatService] 转发私聊实时消息 -> AI: {formattedMsg}");
                await chatActivity.ChatBot.ChatAsync(formattedMsg);
            }
            else if (type == "group" && (configuration.IsGroupEnabled || isAtMe))
            {
                lock (_groupBuffers)
                {
                    if (!_groupBuffers.TryGetValue(groupId, out var sb))
                    {
                        sb = new StringBuilder();
                        _groupBuffers[groupId] = sb;
                    }

                    if (!configuration.IsGroupEnabled && isAtMe)
                        configuration.IsGroupEnabled = true;

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
