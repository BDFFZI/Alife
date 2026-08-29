using System;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 采样器注册特性：标记在采样器类上，框架自动扫描并注册。
/// 新增采样器只需：配置类 + 采样器类 + 此特性，零注册代码。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CollectorAttribute(Type configType, string displayName) : Attribute
{
    /// <summary>采样器配置类型。</summary>
    public Type ConfigType { get; } = configType;

    /// <summary>显示名称。</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>采样器专属配置 UI 片段（HTML）。</summary>
    public string Ui { get; set; } = "";
}
