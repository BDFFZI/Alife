using BDFFZI.VibeCode.GameCompanion;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>语音关键词采样器配置：触发关键词（逗号分隔可监听多个）。</summary>
public sealed class VoiceCollectorConfig : CollectConfigBase
{
    /// <summary>监听关键词，多个用逗号间隔（如 "左, 右侧"），任一命中即触发。</summary>
    public string? Keyword { get; set; }
}
