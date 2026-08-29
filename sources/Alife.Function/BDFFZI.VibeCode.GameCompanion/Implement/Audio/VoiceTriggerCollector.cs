using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BDFFZI.VibeCode.GameCompanion;
using BDFFZI.VibeCode.GameCompanion;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 语音触发采样器：触发式采样。
/// 每次 <see cref="Update"/> 从共享识别器缓存消费本采样器的监听关键词（逗号分隔可监听多个，任一命中即触发；读后即移除，一次发言触发一次）。
/// 命中时累计一个触发时间戳（精确到秒），Value 返回本次累计的所有时间戳（逗号分隔，未命中时为 null）；
/// 框架推送后 <see cref="Use"/> 清空。
/// </summary>
[Collector(typeof(VoiceCollectorConfig), "语音触发",
    Ui = """
        <div class="t-specific-row"><label>监听关键词</label><input data-cfg="Keyword" placeholder="多个用逗号间隔，如：左, 右侧" /></div>
        """)]
public sealed class VoiceTriggerCollector : CollectorBase, IDisposable
{
    readonly VoiceCollectorConfig config;
    readonly VoiceKeywordDetector detector;
    readonly string[] keywords;
    readonly List<string> triggers = new();
    bool disposed;

    public VoiceTriggerCollector(VoiceCollectorConfig config)
    {
        this.config = config;
        keywords = SplitKeywords(config.Keyword);
        detector = VoiceDetectorPool.Acquire();
        foreach (string keyword in keywords)
            detector.AddKeyword(keyword);
    }

    public override CollectConfigBase Config => config;
    public override string? Value => triggers.Count == 0 ? null : string.Join(",", triggers);
    public override string? DebugValue => Value;

    public override void Use() => triggers.Clear();

    public override Task Update(GameContext ctx, CancellationToken ct)
    {
        foreach (string keyword in keywords)
        {
            if (detector.Consume(keyword) == "true")
            {
                triggers.Add(DateTime.Now.ToString("HH:mm:ss"));
                
                break;
            }
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        foreach (string keyword in keywords)
            detector.RemoveKeyword(keyword);
        VoiceDetectorPool.Release();
    }

    static string[] SplitKeywords(string? keywords) => (keywords ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
