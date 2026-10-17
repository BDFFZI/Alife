namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 音频触发采样器：匹配参考音频文件，检测系统声音中的音效。
/// 命中时累计触发时间戳（逗号分隔）；框架推送后清空。
/// </summary>
// [Collector(typeof(AudioTriggerConfig), "音频触发",
//     Ui = """
//         <div class="t-specific-row"><label>音频文件</label><input data-cfg="AudioFilePath" placeholder="支持 wav/mp3/amr/m4a/silk" style="flex:1;min-width:200px" /></div>
//         <div class="t-specific-row"><label>相似度阈值</label><input type="number" data-cfg="Threshold" min="0" max="1" step="0.05" value="0.8" /></div>
//         """)]
// public sealed class AudioTriggerCollector : CollectorBase, IDisposable
// {
//     readonly AudioTriggerConfig config;
//     readonly AudioMatchDetector detector;
//     readonly string patternId;
//     readonly List<string> triggers = new();
//     bool disposed;
//
//     public AudioTriggerCollector(AudioTriggerConfig config)
//     {
//         this.config = config;
//         patternId = $"audio_{config.GetHashCode()}";
//         detector = AudioMatchPool.Acquire(
//             config.AudioFilePath ?? "",
//             config.Threshold,
//             config.SampleCount);
//         detector.AddPattern(patternId);
//     }
//
//     public override CollectConfigBase Config => config;
//     public override string? Value => triggers.Count == 0 ? null : string.Join(",", triggers);
//     public override string? DebugValue => detector.LastSimilarity.ToString("F3");
//
//     public override void Use() => triggers.Clear();
//
//     public override System.Threading.Tasks.Task Update(GameContext ctx, System.Threading.CancellationToken ct)
//     {
//         if (detector.Consume(patternId) == "true")
//             triggers.Add(DateTime.Now.ToString("HH:mm:ss"));
//         return System.Threading.Tasks.Task.CompletedTask;
//     }
//
//     public void Dispose()
//     {
//         if (disposed) return;
//         disposed = true;
//         detector.RemovePattern(patternId);
//         AudioMatchPool.Release();
//     }
// }
