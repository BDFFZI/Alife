namespace Alife.Function.QChat;

public record QChatServiceConfig
{
    public long BotId { get; set; }
    public long OwnerId { get; set; }
    //连接配置
    public string Url { get; set; } = "ws://127.0.0.1:3001";
    public string Token { get; set; } = "";
    public int AutoReconnectSeconds { get; set; } = 60; //自动尝试重连的间隔（秒）
    //提示词
    public string AppendDocumentPrompt { get; set; } =
        "注意！如果回复QQ消息，必须保持极简的文本（0-20字）来保证自然感。同时群聊消息要选择性忽略，避免刷屏。此外注意分清语境，群聊环境人声嘈杂，不要回复与自己无关的内容，回复时请使用CQ码功能指定回复人。";
    public string AppendPrivateChatPrompt { get; set; } = "(回复请保持1-20字)"; //收到私聊消息时附加给ai的提示词
    public string AppendGroupChatPrompt { get; set; } = "(回复请保持1-20字。注意分清群聊场合，不要随便插话，避免刷屏)"; //收到群聊消息时附加给ai的提示词
    //群监听唤醒
    public string IgnoredGroup { get; set; } = ""; //完全屏蔽消息的群，不会收到这些群的任何信息，以逗号分隔
    public string WakingWords { get; set; } = ""; //原始群消息中触发开启群消息监听的唤醒词，以逗号分隔
    public float ProactiveChatProbability { get; set; } //收到原始群消息时自动激活群消息监听的概率
    //群监听缓存
    public int PerBufferSize { get; set; } = 5; //非激活时也始终缓存的消息池大小，当角色激活时会附带发送，使ai可以感知到聊天背景
    public int MaxBufferMessages { get; set; } = -1; //激活状态下的最大群消息暂存数量，发生溢出时会立即推送，-1表示无限
    public float FlushInterval { get; set; } = 12f; //推送倒计时，隔一段时间推送暂存的群消息
    public bool DebounceEnabled { get; set; } //消息防抖，接收消息后重置推送倒计时，继续等待消息
    //群监听关闭
    public bool CloseGroupAfterReply { get; set; } //AI回复后立即关闭群消息监听
    public float AutoCloseMinutes { get; set; } = 4f; //长时间不触发唤醒条件时，自动关闭群消息监听的时间
    //私聊防抖
    public float PrivateDebounceTime { get; set; } //进行私聊时的防抖的秒数
}