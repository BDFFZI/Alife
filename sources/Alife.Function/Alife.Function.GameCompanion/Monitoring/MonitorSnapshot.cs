using System.Collections.Generic;

namespace Alife.Function.GameCompanion.Monitoring;

/// <summary>
/// 陪玩监控的实时快照：当前状态、游戏名、各数据项的当前值。
/// 供数据浮窗等 UI 实时展示。
/// </summary>
public class MonitorSnapshot
{
    public string GameName { get; set; } = "";
    public MonitorState State { get; set; }
    public Dictionary<string, bool> Enabled { get; set; } = new();
    public Dictionary<string, string?> Values { get; set; } = new();
    public Dictionary<string, string?> DebugValues { get; set; } = new();
    /// <summary>框架追踪的当前值（与 Value 同步，用于防抖和推送比较）。</summary>
    public Dictionary<string, string?> CurrentValues { get; set; } = new();
    /// <summary>上次真正推送给 AI 的值。</summary>
    public Dictionary<string, string?> PushedValues { get; set; } = new();
    /// <summary>防抖完成进度（0~1，1=已到可推送时机）。</summary>
    public Dictionary<string, double> DebounceProgress { get; set; } = new();
    /// <summary>每采样器防抖时长（秒），供前端平滑动画。</summary>
    public Dictionary<string, double> DebounceSeconds { get; set; } = new();
    /// <summary>每采样器上次更新时间（UTC ticks），供前端平滑动画。</summary>
    public Dictionary<string, long> LastUpdateTimeTicks { get; set; } = new();
    /// <summary>每采样器过期时长（秒），供前端平滑动画（过期在防抖之后开始计）。</summary>
    public Dictionary<string, double> ExpireSeconds { get; set; } = new();
    public bool IsPaused { get; set; }
}