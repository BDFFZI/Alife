using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.GameCompanion.Screen;

namespace Alife.Function.GameCompanion.Collectors;

public struct GameContext
{
    public ScreenFrame? Frame;
}

/// <summary>
/// 数据采样器抽象基类。
/// 采样器通过 <see cref="Update"/> 更新自身状态，暴露 <see cref="Value"/> 和 <see cref="DebugValue"/>。
/// 框架在每次 Update 后调用 <see cref="TrackCurrentValue"/> 追踪当前值变化并刷新防抖计时。
/// 真正推送后调用 <see cref="OnPushed"/>（内部调用 <see cref="Use"/> 消费触发值）。
/// </summary>
public abstract class CollectorBase : IDisposable
{
    public abstract string Name { get; }

    /// <summary>当前输出值（框架每帧读取）。null=无输出，""=有效空文本。</summary>
    public abstract string? Value { get; }

    /// <summary>原始值（供浮窗调试）。</summary>
    public abstract string? DebugValue { get; }

    /// <summary>框架追踪的当前值：与 Value 同步更新，用于防抖判断和与 PushedValue 比较。</summary>
    public string? CurrentValue { get; private set; }

    /// <summary>上次真正推送给 AI 的值。</summary>
    public string? PushedValue { get; private set; }

    public abstract double DebounceSeconds { get; }

    DateTime lastUpdateTime = DateTime.MinValue;

    /// <summary>上次更新时间（UTC ticks，供前端做平滑动画）。</summary>
    public long LastUpdateTimeTicks => lastUpdateTime.Ticks;

    public bool IsDue(DateTime now)
        => (now - lastUpdateTime).TotalSeconds >= Math.Max(0, DebounceSeconds);

    /// <summary>防抖完成进度 0~1：距上次更新已过的时间占防抖时长的比例（防抖期满为 1）。</summary>
    public double DebounceProgress(DateTime now)
    {
        double total = Math.Max(0, DebounceSeconds);
        if (total <= 0)
            return 1;
        double elapsed = (now - lastUpdateTime).TotalSeconds;
        return Math.Clamp(elapsed / total, 0, 1);
    }

    /// <summary>框架在每次 Update 后调用：比较 Value 与 CurrentValue，有变化则更新并刷新防抖计时。
    /// Value 为 null 也更新（表示无待推值），保证推送消费后 CurrentValue 正确清空。</summary>
    public void TrackCurrentValue()
    {
        string? value = Value;
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
        try { Use(); } catch (Exception ex) { _ = ex; }
    }

    /// <summary>触发式采样器重写此方法：清空已累计的触发时间戳。</summary>
    public virtual void Use() { }

    public abstract Task Update(GameContext ctx, CancellationToken ct);
    public virtual void Dispose() { }
}