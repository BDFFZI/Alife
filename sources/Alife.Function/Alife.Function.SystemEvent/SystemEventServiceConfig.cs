namespace Alife.Function.SystemEvent;

public class SystemEventServiceConfig
{
    public string? StartPrompt { get; set; } = "(部分系统状态，如功能开关、定时器等已全部重置)";
    public string? DestroyPrompt { get; set; } = "(系统已逐步关闭，不要执行强功能指令，仅可尝试道别操作)";

    public string? UpdatePrompt { get; set; } =
        "(如果你手头还有事情，请继续。否则你可以自由活动，比如主动找主人玩，或看新闻学知识、发起新话题、偷窥主人屏幕、去Q群找人聊天玩耍，等各种你能想象到的事)";

    public int UpdateInterval { get; set; } = 30;
    public int UpdateRandomOffset { get; set; } = 10;
    public int UpdateIntervalMultiplier { get; set; } = 3;
    public int UpdateIntervalMaxPowerCount { get; set; } = 5;
}