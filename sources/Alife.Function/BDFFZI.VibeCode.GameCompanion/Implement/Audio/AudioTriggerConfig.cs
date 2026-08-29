using BDFFZI.VibeCode.GameCompanion;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 音频触发采样器配置：匹配参考音频文件，检测系统声音中的音效。
/// </summary>
public class AudioTriggerConfig : CollectConfigBase
{
    /// <summary>参考音频文件路径（wav/mp3/amr/m4a/silk）。</summary>
    public string? AudioFilePath { get; set; }

    /// <summary>余弦相似度阈值（0~1），越大越严格。默认 0.8。</summary>
    public double Threshold { get; set; } = 0.8;

    /// <summary>FFT 采样点数（2 的幂）。默认 1024。</summary>
    public int SampleCount { get; set; } = 1024;
}
