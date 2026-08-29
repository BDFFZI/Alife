using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.GameCompanion.Screen;

namespace Alife.Function.GameCompanion.Collector;

public struct GameContext
{
    public ScreenFrame? Frame;
}

/// <summary>
/// 数据采样器抽象基类：仅负责采样逻辑。
/// 框架通过 <see cref="Monitoring.CollectorState"/> 管理调度状态（CurrentValue/PushedValue/防抖计时等）。
/// 子类可自行选择实现 <see cref="IDisposable"/> 或 <see cref="IAsyncDisposable"/>。
/// </summary>
public abstract class CollectorBase
{
    /// <summary>采样器配置（含 Name、DebounceSeconds 等框架通用参数）。</summary>
    public abstract CollectConfigBase Config { get; }

    /// <summary>当前输出值（框架每帧读取）。null=无输出，""=有效空文本。</summary>
    public abstract string? Value { get; }

    /// <summary>原始值（供浮窗调试）。</summary>
    public abstract string? DebugValue { get; }

    public abstract Task Update(GameContext ctx, CancellationToken ct);

    /// <summary>触发式采样器重写此方法：清空已累计的触发时间戳。</summary>
    public virtual void Use() { }
}
