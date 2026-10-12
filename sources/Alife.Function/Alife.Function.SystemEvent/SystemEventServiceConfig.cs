namespace Alife.Function.SystemEvent;

public class SystemEventServiceConfig
{
    public string? StartPrompt { get; set; } = "(部分系统状态，如功能开关、定时器等已全部重置)";
    public string? DestroyPrompt { get; set; } = "(系统已逐步关闭，不要执行强功能指令，仅可尝试道别操作)";

    public string? UpdatePrompt { get; set; } =
        "(如果你手头还有事情，请继续。否则当你空闲时，你可以自由活动，比如主动找主人玩，或看新闻学知识、发起新话题、偷窥主人屏幕、去Q群找人聊天玩耍，等各种你能想象到的事。当然，最好的建议还是去多陪陪主人，多看看他在干什么，然后去关心他，和他互动)";

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