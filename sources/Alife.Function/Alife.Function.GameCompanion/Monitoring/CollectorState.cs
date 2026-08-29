using System;
using Alife.Function.GameCompanion.Collector;

namespace Alife.Function.GameCompanion.Monitoring;

/// <summary>
/// 采样器的框架调度状态：持有对采样器实例的引用，管理 CurrentValue/PushedValue/防抖计时等框架层状态。
/// 采样器本身只负责采样逻辑，框架通过此状态类驱动调度。
/// </summary>
sealed class CollectorState
{
    public required CollectorBase Collector { get; init; }

    /// <summary>框架追踪的当前值：与 Collector.Value 同步更新，用于防抖判断和与 PushedValue 比较。</summary>
    public string? CurrentValue { get; private set; }

    /// <summary>上次真正推送给 AI 的值。</summary>
    public string? PushedValue { get; private set; }

    DateTime lastUpdateTime = DateTime.MinValue;

    /// <summary>上次更新时间（UTC ticks，供前端做平滑动画）。</summary>
    public long LastUpdateTimeTicks => lastUpdateTime.Ticks;

    public bool IsDue(DateTime now)
        => (now - lastUpdateTime).TotalSeconds >= Math.Max(0, Collector.Config.DebounceSeconds);

    /// <summary>防抖完成进度 0~1：距上次更新已过的时间占防抖时长的比例（防抖期满为 1）。</summary>
    public double DebounceProgress(DateTime now)
    {
        double total = Math.Max(0, Collector.Config.DebounceSeconds);
        if (total <= 0)
            return 1;
        double elapsed = (now - lastUpdateTime).TotalSeconds;
        return Math.Clamp(elapsed / total, 0, 1);
    }

    /// <summary>框架在每次 Update 后调用：比较 Collector.Value 与 CurrentValue，有变化则更新并刷新防抖计时。</summary>
    public void TrackCurrentValue()
    {
        string? value = Collector.Value;
        if (value != CurrentValue)
        {
            CurrentValue = value;
            lastUpdateTime = DateTime.UtcNow;
        }
    }

    /// <summary>框架在 report 成功后调用：记录 PushedValue、重置防抖计时并消费触发值。</summary>
    public void OnPushed(string? value, DateTime now)
    {
        PushedValue = value;
        lastUpdateTime = now;
        try { Collector.Use(); } catch (Exception ex) { _ = ex; }
    }
}
