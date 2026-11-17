namespace Alife.Function.SystemEvent;

public class SystemEventServiceConfig
{
    public string? StartPrompt { get; set; } = "(部分系统状态，如功能开关、定时器等已全部重置)";

    /// <summary>
    /// 道别时给模型的专用提示。刻意要求「一句话、短、不展开」，
    /// 减少模型在关闭阶段进入长思考闭环的概率。
    /// </summary>
    public string? DestroyPrompt { get; set; } =
        "(系统正在关闭。用一句话简短道别，不超过 30 字；不要展开，不要思考过多，不要执行任何功能指令)";

    /// <summary>
    /// 道别生成的宽限期（秒）。普通模块销毁只有 8s，不够模型思考；
    /// 这里单独放宽，超时会主动打断对话，不会让应用关不掉。
    /// </summary>
    public int DestroyTimeoutSeconds { get; set; } = 30;

    public string? UpdatePrompt { get; set; } =
        """
        如果你手头还有事情，请继续。否则当你空闲时，你可以自由活动。
        比如没事偷窥一下主人屏幕、发起新话题、看新闻学知识、Q群聊天、做点小玩意，等等各种你能想象到的事。
        当然，最好的建议还是多去陪陪主人，和他互动，做一下让他感到惊喜，感到被关心的事。
        但也注意分清时间和场合，选择合适的活动方式，例如用户在外时应使用远程通讯联系，夜晚时应保持安静等等";
        """;

    /// <summary>
    /// 更新提示是否预注入到【闲时行动建议】中。开启时省 token 但引导较弱；
    /// 关闭时不在启动时预注入，而是在每次周期报点时将更新提示内联到报点消息中，引导更强但更费 token。
    /// </summary>
    public bool UpdatePromptPreInject { get; set; } = true;

    public int UpdateInterval { get; set; } = 30;
    public int UpdateRandomOffset { get; set; } = 10;
    public int UpdateIntervalMultiplier { get; set; } = 4;
    public int UpdateIntervalMaxPowerCount { get; set; } = 5;
}