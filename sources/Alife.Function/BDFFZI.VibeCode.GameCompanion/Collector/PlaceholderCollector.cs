using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 占位配置：当配置引用的采样器类型在当前程序集中不存在时（插件未加载），
/// 用此配置保留原始数据，确保配置可正确反序列化和重新序列化。
/// </summary>
public sealed class PlaceholderCollectConfig : CollectConfigBase
{
    /// <summary>原始采样器类型名（如 "TextContentConfig"）。</summary>
    public required string SamplerName { get; init; }

    /// <summary>原始 Config 子对象数据（保留完整 JSON）。</summary>
    public required JObject RawConfig { get; init; }
}

/// <summary>
/// 占位采样器：引用未加载插件的采样器类型时使用，不执行任何采样。
/// </summary>
public sealed class PlaceholderCollector : CollectorBase
{
    readonly CollectConfigBase config;

    public PlaceholderCollector(CollectConfigBase config)
    {
        this.config = config;
    }

    public override CollectConfigBase Config => config;
    public override string? Value => null;
    public override string? DebugValue => config is PlaceholderCollectConfig p
        ? $"[缺失: {p.SamplerName}]"
        : "[缺失采样器]";

    public override Task Update(GameContext ctx, CancellationToken ct)
        => Task.CompletedTask;
}
