using Alife.Function.GameCompanion.Collector;
using Alife.Function.GameCompanion.Screen;

namespace Alife.Function.GameCompanion.Implement;

/// <summary>
/// 颜色比值采样器配置：在两个参考颜色之间计算当前颜色的比值（0~1）。
/// </summary>
public class ColorRatioConfig : CollectConfigBase
{
    /// <summary>采样区域。</summary>
    public ScreenRegion Region { get; set; } = new();

    /// <summary>比值为 0 的颜色（十六进制，如 "#FF0000"）。</summary>
    public string? ColorZero { get; set; }

    /// <summary>比值为 1 的颜色（十六进制，如 "#00FF00"）。</summary>
    public string? ColorOne { get; set; }

    /// <summary>变化阈值：新比值与上次比值之差超过此值才更新（防抖）。默认 0.05。</summary>
    public double ChangeThreshold { get; set; } = 0.05;
}
