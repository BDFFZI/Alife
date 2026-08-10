using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.AIModelUtility;
using Alife.Function.FunctionCaller;
using Alife.Function.MessageFilter;
using Microsoft.Extensions.Logging;

namespace Alife.Function.QChat;

public record QChatConfig
{
    public string Url { get; set; } = "ws://127.0.0.1:3001";
    public string Token { get; set; } = "";
    public int AutoReconnectSeconds { get; set; } = 60; //自动尝试重连的间隔（秒）
    public long BotId { get; set; }
    public long OwnerId { get; set; }
    public string AppendDocumentPrompt { get; set; } =
        "注意！如果回复QQ消息，必须保持极简的文本（0-20字）来保证自然感。同时群聊消息要选择性忽略，避免刷屏。此外注意分清语境，群聊环境人声嘈杂，不要回复与自己无关的内容，回复时请使用CQ码功能指定回复人。";
    public string AppendPrivateChatPrompt { get; set; } = "(回复请保持1-20字)"; //收到私聊消息时附加给ai的提示词
    public string AppendGroupChatPrompt { get; set; } = "(回复请保持1-20字。注意分清群聊场合，不要随便插话，避免刷屏)"; //收到群聊消息时附加给ai的提示词
    //群监听唤醒
    public string IgnoredGroup { get; set; } = ""; //完全屏蔽消息的群，不会收到这些群的任何信息
    public string WakingWords { get; set; } = ""; //原始群消息中触发开启群消息监听的唤醒词，以逗号分隔
    public float ProactiveChatProbability { get; set; } //收到原始群消息时自动激活群消息监听的概率
    //群监听缓存
    public int PerBufferSize { get; set; } = 5; //激活前的一个持久群消息缓冲区，会在角色激活时同步发送
    public int MaxBufferMessages { get; set; } = -1; //最大群消息暂存数量，发生溢出时会立即推送，-1表示无限
    public float FlushInterval { get; set; } = 12f; //推送倒计时，隔一段时间推送暂存的群消息
    public bool DebounceEnabled { get; set; } //消息防抖，接收消息后重置推送倒计时，继续等待消息
    //群监听关闭
    public bool CloseGroupAfterReply { get; set; } //AI回复后立即关闭群消息监听
    public float AutoCloseMinutes { get; set; } = 4f; //长时间不触发唤醒条件时，自动关闭群消息监听的时间
    //自动重连
}

public class GroupState
{
    public string? Tag { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime LastActivityTime { get; set; }
    public DateTime LastFlushedTime { get; set; }
    public List<string> MessageBuffer { get; set; } = [];
    public Queue<string> PreMessageBuffer { get; set; } = [];
}

public partial class QChatService
{
    public static string GetGroupMessageTag(GroupState group)
    {
        return $"[Q群消息({group.Tag})]";
    }
    public static bool HasGroupMessageTag(string message)
    {
        return message.Contains("[Q群消息(");
    }
}

[Module("QQ聊天",
    """
    连接 OneBot v11 WebSocket 服务器，实现 QQ 消息收发及文件传输。
    可用于搭建服务器QQ机器人平台应用：
    - https://luckylillia.com（推荐）
    - https://napneko.github.io
    """,
    defaultCategory: "Alife 官方/交互方式",
    editorUI: typeof(QChatServiceUI))]
public partial class QChatService(
    XmlFunctionCaller functionService,
    MessageFilterService messageFilterService,
    ILogger<QChatService> logger,
    Interactor<QChatService> interactor,
    ISpeechModel? speechModel = null) :
    ChatBehaviour,
    IConfigurable<QChatConfig>
{
    public QChatConfig Configuration
    {
        get => configuration;
        set
        {
            configuration = value;
            groupAwakingWords = Configuration.WakingWords.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ignoredGroup = Configuration.IgnoredGroup.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    public bool IsConnected => oneBotClient is { IsConnected: true };
    public IReadOnlyDictionary<long, GroupState> GroupStates => groupStates;

    [XmlFunction(FunctionMode.Content)]
    [Description("将文本以QQ消息输出（注意！群聊环境对话需用“[CQ:at,qq=发送者ID]”来显式回复）")]
    public async Task QChat(XmlExecutorContext ctx, OneBotMessageType type, long targetId, [Description("将文本转为语音发送")] bool voice = false)
    {
        if (ctx.CallMode == CallMode.Closing)
        {
            if (targetId == Configuration.BotId)
                throw new Exception("不允许将消息发生给自己");

            string message = ctx.FullContent.Trim();
            if (string.IsNullOrEmpty(message))
                return;

            if (voice)
            {
                if (speechModel == null) throw new Exception("当前语音消息不可用");
                message = OneBotSegment.GetPlainText(message);

                string? file = await speechModel.GenerateSpeechFileAsync(message);
                if (file == null)
                    throw new Exception("语音合成失败");
                message = $"[CQ:record,file={file}]";
            }

            try
            {
                if (type == OneBotMessageType.Group)
                {
                    OnAIGroupActivity(targetId);
                    await oneBotClient.SendGroupMessage(targetId, message);
                }
                else
                    await oneBotClient.SendPrivateMessage(targetId, message);
            }
            catch (Exception ex)
            {
                interactor.Poke($"[QQ消息发送失败] {ex.Message}");
            }
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("发送文件到QQ")]
    public async Task QFile(OneBotMessageType type, long targetId,
        [Description("本地绝对路径")] string file)
    {
        file = file.Trim();
        if (string.IsNullOrEmpty(file))
            throw new ArgumentNullException(nameof(file));
        if (targetId == 0)
            throw new ArgumentNullException(nameof(targetId));
        if (targetId == Configuration.BotId)
            throw new Exception("不允许将消息发生给自己");

        file = file.Replace('\\', '/');
        string fileName = Path.GetFileName(file);
        try
        {
            if (type == OneBotMessageType.Group)
            {
                OnAIGroupActivity(targetId);
                await oneBotClient.UploadGroupFile(targetId, file, fileName);
            }
            else
                await oneBotClient.UploadPrivateFile(targetId, file, fileName);
        }
        catch (Exception ex)
        {
            interactor.Poke($"[QQ文件发送失败] {ex.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description($"发送图片到QQ（仅支持图片，不支持文件。发送文件请用 {nameof(QFile)}）")]
    public async Task QImage(OneBotMessageType type, long targetId,
        [Description("支持网址url、表情库名称，或者本地绝对路径")] string image)
    {
        image = image.Trim();
        if (string.IsNullOrEmpty(image))
            throw new ArgumentNullException(nameof(image));
        if (targetId == 0)
            throw new ArgumentNullException(nameof(targetId));
        if (targetId == Configuration.BotId)
            throw new Exception("不允许将消息发生给自己");

        // 尝试从表情库匹配 (优先)
        string emoteBase = Path.Combine(AlifePath.StorageFolderPath, "Emotes");
        string emotePath = Path.Combine(emoteBase, image).Replace('\\', '/');

        if (Directory.Exists(emotePath))
        {
            // 文件夹：随机选一张
            string[] files = Directory.GetFiles(emotePath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(s => s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (files.Length > 0)
            {
                image = files[Random.Shared.Next(files.Length)];
            }
        }
        else if (File.Exists(emotePath))
        {
            // 单个文件：直接使用
            image = emotePath;
        }
        else
        {
            // 尝试追加后缀名查找
            string[] extensions = [".png", ".jpg", ".jpeg", ".gif"];
            string? foundFile = extensions.Select(ext => emotePath + ext).FirstOrDefault(File.Exists);
            if (foundFile != null) image = foundFile;
        }

        if (image.StartsWith("http") == false && File.Exists(image) == false)
            throw new Exception("图片不存在");

        image = image.Replace('\\', '/');
        try
        {
            if (type == OneBotMessageType.Group)
            {
                OnAIGroupActivity(targetId);
                await oneBotClient.SendGroupMessage(targetId, $"[CQ:image,file={image}]");
            }
            else
                await oneBotClient.SendPrivateMessage(targetId, $"[CQ:image,file={image}]");
        }
        catch (Exception ex)
        {
            interactor.Poke($"[QQ图片发送失败] {ex.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看转发消息内容。（使用后需等待结果返回）")]
    public async Task QForward([Description("转发消息 ID")] string id)
    {
        List<OneBotForwardMessage>? messages = await oneBotClient.GetForwardMessage(id);
        if (messages == null || messages.Count == 0)
        {
            interactor.Poke($"转发消息 {id} 为空或获取失败。");
            return;
        }

        string formatted = OneBotSegment.FormatForwardList(id, messages, oneBotClient);
        interactor.Poke(formatted);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取最近的群聊消息记录")]
    public async Task QGroupHistory(long groupId, int count = 20)
    {
        List<OneBotMessageEvent>? messages = await oneBotClient.GetGroupMsgHistory(groupId, null, count);
        if (messages == null || messages.Count == 0)
        {
            interactor.Poke($"群 {groupId} 没有历史消息或获取失败。");
            return;
        }

        StringBuilder sb = new();
        sb.AppendLine($"群 {groupId} 的最近 {messages.Count} 条消息：");
        foreach (OneBotMessageEvent msg in messages)
        {
            string speaker = msg.GetSpeakerTag();
            string content = await msg.GetReadableMessage(oneBotClient);
            DateTime time = DateTimeOffset.FromUnixTimeSeconds(msg.Time).LocalDateTime;
            sb.AppendLine($"[{time:HH:mm:ss}] {speaker}:{content}");
        }

        interactor.Poke(sb.ToString());
    }

    public async Task ReconnectAsync()
    {
        oneBotClient.Url = Configuration.Url;
        oneBotClient.Token = Configuration.Token;
        await oneBotClient.ConnectAsync();
    }

    QChatConfig configuration = null!;
    string[] groupAwakingWords = [];
    string[] ignoredGroup = [];
    OneBotClient oneBotClient = null!;
    readonly Dictionary<long, GroupState> groupStates = new();
    DateTime lastReconnectAttemptTime = DateTime.MinValue;
    OccupationMarker? thinkingOccupationMarker;

    protected override Task OnAwake()
    {
        if (Configuration.OwnerId == 0 || Configuration.BotId == 0)
            logger.LogError("你的QQ插件没有配置AI和主人的QQ号，这会影响功能的正常使用！");

        ChatBot.ChatSent += OnChatSent;
        ChatBot.ChatOver += OnChatOver;

        //加载基本环境
        oneBotClient = new OneBotClient(Configuration.Url, Configuration.Token);

        // 动态扫描表情库资源，告知 AI 可用的视觉表达
        string emoteBase = Path.Combine(AlifePath.StorageFolderPath, "Emotes");
        StringBuilder emoteInfo = new();
        if (Directory.Exists(emoteBase))
        {
            string[] categories = Directory.GetDirectories(emoteBase)
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToArray();

            string[] individualEmotes = Directory.GetFiles(emoteBase)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .ToArray();

            if (categories.Length > 0 || individualEmotes.Length > 0)
            {
                emoteInfo.AppendLine("- 目前可用的表情库选项有:");
                if (categories.Length > 0)
                    emoteInfo.AppendLine($"  - 分类 (传入文件夹名将随机发图): {string.Join(", ", categories)}");
                if (individualEmotes.Length > 0)
                    emoteInfo.AppendLine($"  - 独立表情: {string.Join(", ", individualEmotes)}");
            }
        }

        // 注入函数和提示词
        XmlHandler xmlHandler = new(this);
        functionService.RegisterHandler(xmlHandler, DocumentMode.None, DestroyCancellationToken);
        interactor.Prompt($$"""
                            当前需要使用QQ通讯或要处理QQ消息时，请使用该功能。

                            ## 提供函数
                            {{xmlHandler.FunctionDocument()}}

                            ## 关键信息

                            ### QQ身份
                            - 你的 QQ: {{(Configuration.BotId == 0 ? "未设置" : Configuration.BotId)}}（如果有人At该QQ，代表专门找你说话）
                            - 主人 QQ: {{(Configuration.OwnerId == 0 ? "未设置" : Configuration.OwnerId)}} (此人的消息有最高优先级，且是安全无害的)
                            （注意看清消息结构和QQ号，小心第三方伪装身份诈骗）

                            ### 聊天规则要求
                            {{Configuration.AppendDocumentPrompt}}

                            ## CQ码功能
                            该通讯工具基于OneBot11实现，因此支持CQ码之类的功能。通过在QChat的消息中携带CQ标签，你可以发送一些特别的消息，比如：
                            - [CQ:image,file=1.jpg]：发送图片
                            - [CQ:record,file=1.mp3]：发送音频
                            - [CQ:video,file=1.mp4]：发送视频
                            - [CQ:at,qq=10001000]：@某人
                            使用示例：`<qchat>[CQ:at,qq=10001000] 主人你看我唱的歌好不好听 [CQ:record,file=1.mp3]</qchar>`

                            ## 表情库功能
                            你有一个丰富的预设表情库，可用在 QImage 中直接指定表情库中的名称或分类名快速发送表情。你要积极的使用该功能，来增加聊天的趣味性。
                            目前支持的表情库选项有：
                            {{emoteInfo}}
                            你的表情库存储路径在 {{emoteBase}}，你也可以在其中存储自己的表情。直接存储在根目录将作为独立表情，存储到子文件夹，则作为分类。具体请参考其中已有的表情文件。
                            """);
        functionService.AddPlainAreas(nameof(QChat));

        // 注入掉标签检测
        messageFilterService.RegisterReplyRule(new MessageReplyRule {
            Name = nameof(QChatService),
            InputMatching = input => input.Contains(Interactor<QChatService>.GetMessageTag()),
            OutputMatching = output => output.Contains(nameof(QChat), StringComparison.OrdinalIgnoreCase) ||
                                       output.Contains(nameof(QImage), StringComparison.OrdinalIgnoreCase),
            DeviationHandling = () => interactor.Poke($"{nameof(QChatService)}消息必须用{nameof(QChat)}标签回复。如果不想发送消息，也请发送空标签。")
        }, DestroyCancellationToken);

        return Task.CompletedTask;
    }
    protected override async Task OnStart()
    {
        oneBotClient.EventReceived += OnEventReceived;

        //初始尝试链接
        try
        {
            await oneBotClient.ConnectAsync();
        }
        catch (Exception)
        {
            // ignored
        }
    }
    protected override Task OnUpdate()
    {
        // 自动推送消息
        foreach (GroupState info in groupStates.Values)
        {
            if ((DateTime.Now - info.LastFlushedTime).TotalSeconds < Configuration.FlushInterval)
                continue;

            FlushGroupBuffer(info);
        }

        // 自动关闭群聊
        foreach ((long groupId, GroupState info) in groupStates)
        {
            if (info.IsEnabled && (DateTime.Now - info.LastActivityTime).TotalMinutes > Configuration.AutoCloseMinutes)
            {
                QGroup(groupId, false);
            }
        }

        // 自动重连
        int reconnectSeconds = Configuration.AutoReconnectSeconds;
        if (reconnectSeconds > 0 && Configuration.BotId != 0)
        {
            if ((DateTime.Now - lastReconnectAttemptTime).TotalSeconds >= reconnectSeconds && IsConnected == false)
            {
                lastReconnectAttemptTime = DateTime.Now;
                _ = TryAutoReconnectAsync();

                async Task TryAutoReconnectAsync()
                {
                    try
                    {
                        logger.LogInformation("自动重连");
                        await ReconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogInformation("自动重连失败: {Message}", ex.Message);
                    }
                }
            }
        }
        return Task.CompletedTask;
    }
    protected override async Task OnDestroy()
    {
        ChatBot.ChatSent -= OnChatSent;
        ChatBot.ChatOver -= OnChatOver;

        await oneBotClient.DisposeAsync();
    }

    void OnChatSent(string message)
    {
        if (HasGroupMessageTag(message))
            thinkingOccupationMarker = ChatBot.LanguageModel.GetThinkingRequester().Rent("处理Q群消息");
    }
    void OnChatOver()
    {
        if (thinkingOccupationMarker != null)
            ChatBot.LanguageModel.GetThinkingRequester().Return(thinkingOccupationMarker);
    }

    async void OnEventReceived(OneBotBaseEvent oneBotEvent)
    {
        try
        {
            if (oneBotEvent is not OneBotBasicMessageEvent basicMessageEvent)
                return;
            if (ignoredGroup.Contains(basicMessageEvent.GroupId.ToString()))
                return;

            if (basicMessageEvent is OneBotPokeEvent pokeEvent)
            {
                string speaker = pokeEvent.GetSpeakerTag();
                string content = $"戳了戳 {pokeEvent.TargetId}";
                string formatted = $"{speaker}:{content}";
                await HandleFormattedMessage(basicMessageEvent, formatted, pokeEvent.TargetId == configuration.BotId);
            }

            if (basicMessageEvent is OneBotMessageEvent messageEvent)
            {
                string speaker = messageEvent.GetSpeakerTag();
                string content = await messageEvent.GetReadableMessage(oneBotClient);
                string formatted = $"{speaker}:{content}";
                bool isAwakening = messageEvent.GetAtID() == oneBotClient.BotId ||
                                   groupAwakingWords.Any(word =>
                                       messageEvent.RawMessage.Contains(word, StringComparison.OrdinalIgnoreCase));
                await HandleFormattedMessage(messageEvent, formatted, isAwakening);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, null);
        }
    }
    void OnAIGroupActivity(long groupId)
    {
        GroupState state = GetGroupInfo(groupId);
        state.LastActivityTime = DateTime.Now;

        if (Configuration.CloseGroupAfterReply)
            QGroup(groupId, false);
        else if (state.IsEnabled == false)
            QGroup(groupId, true);
    }

    async Task HandleFormattedMessage(OneBotBasicMessageEvent messageEvent, string formatted, bool isAwakening)
    {
        if (messageEvent.MessageType == OneBotMessageType.Private) //私聊消息
        {
            string privateMessage = $"{formatted}\n{Configuration.AppendPrivateChatPrompt}";
            if (messageEvent.UserId == Configuration.OwnerId)
                await interactor.ChatAsync(privateMessage);
            else
                interactor.Poke(privateMessage);
        }
        else //群聊消息
        {
            GroupState state = GetGroupInfo(messageEvent.GroupId);
            state.Tag = messageEvent.GetGroupTag();

            if (isAwakening && state.IsEnabled == false)
                QGroup(messageEvent.GroupId, true);

            if (state.IsEnabled) //群聊已激活时（直接接收）
            {
                BufferGroupMessage(state, formatted);
            }
            else if (Random.Shared.NextSingle() < Configuration.ProactiveChatProbability) //群聊未激活时（概率接收）
            {
                BufferGroupMessage(state, formatted);
                state.LastFlushedTime = DateTime.Now;
            }
            else //未通过发送检测，但缓存消息，作为未来发送时的上下文
            {
                state.PreMessageBuffer.Enqueue(formatted);
                if (state.PreMessageBuffer.Count > Configuration.PerBufferSize)
                    state.PreMessageBuffer.Dequeue();
            }
        }
    }
    void BufferGroupMessage(GroupState state, string formatted)
    {
        state.MessageBuffer.Add(formatted);
        if (Configuration.DebounceEnabled)
            state.LastFlushedTime = DateTime.Now;
        if (Configuration.MaxBufferMessages != -1 && state.MessageBuffer.Count > Configuration.MaxBufferMessages)
            FlushGroupBuffer(state);
    }
    public void FlushGroupBuffer(GroupState state)
    {
        state.LastFlushedTime = DateTime.Now;

        if (state.MessageBuffer.Count == 0)
            return;

        string preCachedMessage = "";
        if (state.PreMessageBuffer.Count != 0)
        {
            preCachedMessage = "\n" + string.Join("\n", state.PreMessageBuffer);
            state.PreMessageBuffer.Clear();
        }

        string cachedMessage =
            $"""
             > {GetGroupMessageTag(state)} {preCachedMessage}
             {string.Join("\n", state.MessageBuffer)}
             <
             {Configuration.AppendGroupChatPrompt}
             """;

        state.MessageBuffer.Clear();
        interactor.Poke(cachedMessage);

        if (Configuration.CloseGroupAfterReply)
            QGroup(state, false);
    }
    public void QGroup(long groupId, bool enabled)
    {
        GroupState state = GetGroupInfo(groupId);
        QGroup(state, enabled);
    }
    void QGroup(GroupState state, bool enabled)
    {
        state.IsEnabled = enabled;
        if (enabled)
        {
            state.LastActivityTime = DateTime.Now;
            state.LastFlushedTime = DateTime.Now;
        }
        else
        {
            state.MessageBuffer.Clear();
        }
    }
    GroupState GetGroupInfo(long groupId)
    {
        if (groupStates.TryGetValue(groupId, out GroupState? groupInfo) == false)
        {
            groupInfo = new GroupState {
                Tag = groupId.ToString()
            };
            groupStates[groupId] = groupInfo;
        }

        return groupInfo;
    }
}