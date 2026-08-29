using Alife.Function.GameCompanion.Collector;
using Alife.Function.GameCompanion.Screen;

namespace Alife.Function.GameCompanion.Implement;

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
