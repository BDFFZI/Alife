namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 采样器配置抽象基类：承载数据名、启用开关及框架通用参数。
/// 各采样器实现自己的配置子类存放专属参数，框架只通过该基类间接使用配置，
/// 完全不感知具体配置类型（逻辑层与子类实现解耦）。
/// </summary>
public abstract class CollectConfigBase
{
    /// <summary>数据名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>是否启用：关闭状态的采样器不计入框架遍历流程（可从浮窗快速开关）。</summary>
    public bool IsEnable { get; set; } = true;

    /// <summary>是否作为采样验证：仅该值为 true 的采样器参与「全部有效才推送」的判定门。</summary>
    public bool IsValidator { get; set; } = false;

    /// <summary>本采样器防抖时间（秒）：该采样器产出新值后，至少等待该时长才推送给 AI（0 = 立即）。</summary>
    public double DebounceSeconds { get; set; } = 0.6;

    /// <summary>过期时间（秒）：防抖期满后若因对话占用一直推不出去，超过「防抖+过期」仍未推送则静默丢弃该值（不打扰 AI）。默认 0.6。</summary>
    public double ExpireSeconds { get; set; } = 0.6;

    /// <summary>强制推送：本采样器到可推送时机时，立即用 Chat 打断当前对话并推送（连同本次其他可推送项）。</summary>
    public bool ForcePush { get; set; } = false;

    /// <summary>前置采样器名称：仅当前置采样器有效（CurrentValue != null）时，本采样器才执行更新和推送。</summary>
    public string? Prerequisite { get; set; }
}