using System.Collections.Generic;
using BDFFZI.VibeCode.GameCompanion;
using BDFFZI.VibeCode.GameCompanion;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>像素颜色基类配置：检测区域（支持点/矩形/三角/扇面）+ 颜色代称列表。</summary>
public class PixelCollectorConfig : CollectConfigBase
{
    /// <summary>检测区域（支持点/矩形/三角/扇面）。</summary>
    public ScreenRegion Region { get; set; } = new();
    /// <summary>颜色代称列表（每个含名称、十六进制色值、误差）。匹配最接近的颜色名。</summary>
    public List<NamedColor> ColorOptions { get; set; } = new();
}

/// <summary>颜色内容配置（复用基类字段）。</summary>
public sealed class ColorContentConfig : PixelCollectorConfig { }
/// <summary>颜色触发配置（触发式默认防抖 20 秒）。</summary>
public sealed class ColorTriggerConfig : PixelCollectorConfig
{
    public ColorTriggerConfig() { DebounceSeconds = 20; }
}
/// <summary>颜色开关配置（复用基类字段）。</summary>
public sealed class ColorSwitchConfig : PixelCollectorConfig { }
