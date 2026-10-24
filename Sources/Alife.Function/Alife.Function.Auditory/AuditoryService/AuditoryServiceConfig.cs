using System.Collections.Generic;
using System.ComponentModel;

namespace Alife.Function.Auditory;

public class AuditoryServiceConfig
{
    public string? PushToTalkKey { get; set; }
    public List<string> ReplyRestrictedWords { get; set; } = ["<speak>"];

    [DisplayName("推送间隔（秒）")]
    [Description("系统声音识别结果合并推送给 AI 的基础间隔。")]
    public double PushIntervalSeconds { get; set; } = 30;

    [DisplayName("推送间隔抖动（秒）")]
    [Description("在基础间隔上下随机浮动的范围，避免固定节奏。")]
    public double PushJitterSeconds { get; set; } = 10;
}
