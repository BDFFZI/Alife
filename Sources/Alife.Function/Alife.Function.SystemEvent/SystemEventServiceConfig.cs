namespace Alife.Function.SystemEvent;

public class SystemEventServiceConfig
{
    public string? StartPrompt { get; set; } = "(部分系统状态，如功能开关、定时器等已全部重置)";
    public string? DestroyPrompt { get; set; } = "(系统已逐步关闭，不要执行强功能指令，仅可尝试道别操作)";

    public string? UpdatePrompt { get; set; } =
        """
        如果你手头还有事情，请继续。否则当你空闲时，你可以自由活动。
        比如主动偷窥监听主人屏幕、发起新话题、看新闻学知识、Q群聊天、做点小玩意，等等各种你能想象到的事，这些都会让用户感到非常惊喜。
        当然，最好的建议还是多去关心主人，和他互动。
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