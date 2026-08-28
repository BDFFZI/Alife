using System.Collections.Generic;

namespace Alife.Function.GameCompanion.Collectors;

// ========== 文本统一配置（基类 + 三种模式子类） ==========

/// <summary>文本识别基类配置：OCR 区域 + 可选的过滤正则。</summary>
public class TextCollectorConfig : CollectConfigBase
{
    /// <summary>OCR 区域。</summary>
    public ScreenRegion Region { get; set; } = new();
    /// <summary>过滤正则：识别文本提取匹配片段；无匹配视为本次无效。</summary>
    public string? RegexFilter { get; set; }
}

/// <summary>文本内容配置（复用基类字段）。</summary>
public sealed class TextContentConfig : TextCollectorConfig { }
/// <summary>文本触发配置（复用基类字段）。</summary>
public sealed class TextTriggerConfig : TextCollectorConfig { }
/// <summary>文本开关配置（复用基类字段）。</summary>
public sealed class TextSwitchConfig : TextCollectorConfig { }

// ========== 颜色统一配置（基类 + 三种模式子类） ==========

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

// ========== 语音（独立，不改） ==========

/// <summary>语音关键词采样器配置：触发关键词（逗号分隔可监听多个）。</summary>
public sealed class VoiceCollectorConfig : CollectConfigBase
{
    /// <summary>监听关键词，多个用逗号间隔（如 "左, 右侧"），任一命中即触发。</summary>
    public string? Keyword { get; set; }
}