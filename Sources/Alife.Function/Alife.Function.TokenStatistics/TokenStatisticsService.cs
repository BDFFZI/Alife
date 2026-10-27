
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Alife.Framework;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// Token 统计的纯数据聚合层（无框架订阅逻辑，便于独立测试与多实例合并）。
/// 由角色级 <see cref="TokenStatisticsModule"/> 持有并驱动：模块订阅本角色模型的 TokenUsed 事件，
/// 每轮调用本服务的 Record 累计；UI 经 ChatActivitySystem 遍历各角色模块，读取本服务做展示/汇总。
/// 通过 StorageSystem 按角色存储键隔离持久化，重启不丢。
/// </summary>
public class TokenStatisticsService
{
    sealed class DailyAccumulator
    {
        public TokenUsage Usage;
        public int ChatCount;
    }

    public sealed class PriceConfig
    {
        public double InputPricePerK { get; set; }
        public double OutputPricePerK { get; set; }
        public double CachedPricePerK { get; set; }
    }

    public sealed record DailyStat(
        DateTime Date, int TotalTokens, int InputTokens, int OutputTokens, int CachedTokens,
        int ChatCount, double HitRate, double Cost);
    public sealed record TagStat(string Tag, int Count);
    public sealed record Overview(double TotalCost, int TotalChatCount, double AverageHitRate);

    readonly StorageSystem storageSystem;
    readonly string storageKey;
    readonly Dictionary<DateTime, DailyAccumulator> dailyMap = new();
    readonly Dictionary<string, int> tagMap = new();
    PriceConfig price = new();

    // 解析消息正文开头的属性标签 [标签名...]，取方括号内的标识词（排除纯时间戳/带括号参数行）
    static readonly Regex TagPattern = new(@"^\[(?<tag>[^\s\(\)（）:：0-9][^\s\(\)（）]*)", RegexOptions.Compiled);

    // 每个角色用独立存储键，避免多角色数据互相覆盖
    string HistoryKey => $"TokenStatistics.{storageKey}.History";
    string PriceKey => $"TokenStatistics.{storageKey}.Price";

    public TokenStatisticsService(StorageSystem storageSystem, string storageKey)
    {
        this.storageSystem = storageSystem;
        this.storageKey = storageKey;
        Load();
    }

    /// <summary>记录一轮对话的 token 开销与标签。由模块在 TokenUsed 回调中调用。</summary>
    public void Record(TokenUsageContext context)
    {
        DateTime day = DateTime.Now.Date;
        if (dailyMap.TryGetValue(day, out DailyAccumulator? acc) == false)
        {
            acc = new DailyAccumulator();
            dailyMap[day] = acc;
        }
        acc.Usage = acc.Usage + context.TokenUsage;
        acc.ChatCount++;

        if (!string.IsNullOrEmpty(context.UserMessage))
        {
            foreach (string line in context.UserMessage.Split('\n'))
            {
                Match match = TagPattern.Match(line.TrimStart());
                if (match.Success)
                {
                    string tag = match.Groups["tag"].Value;
                    tagMap.TryGetValue(tag, out int count);
                    tagMap[tag] = count + 1;
                }
            }
        }

        Save();
    }

    public IReadOnlyList<DailyStat> GetDailyStats()
    {
        return dailyMap
            .OrderByDescending(pair => pair.Key)
            .Select(pair => new DailyStat(
                pair.Key,
                pair.Value.Usage.Total,
                pair.Value.Usage.Input,
                pair.Value.Usage.Output,
                pair.Value.Usage.Cached,
                pair.Value.ChatCount,
                ComputeHitRate(pair.Value.Usage),
                ComputeCost(pair.Value.Usage)))
            .ToList();
    }

    public IReadOnlyList<TagStat> GetTagStats()
    {
        return tagMap
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new TagStat(pair.Key, pair.Value))
            .ToList();
    }

    public Overview GetOverview()
    {
        double totalCost = dailyMap.Values.Sum(acc => ComputeCost(acc.Usage));
        int totalChat = dailyMap.Values.Sum(acc => acc.ChatCount);
        double avgHit = dailyMap.Count == 0 ? 0 : dailyMap.Values.Average(acc => ComputeHitRate(acc.Usage));
        return new Overview(totalCost, totalChat, avgHit);
    }

    public PriceConfig GetPriceConfig() => price;

    public void SetPriceConfig(PriceConfig config)
    {
        price = config;
        Save();
    }

    static double ComputeHitRate(TokenUsage usage)
        => usage.Input > 0 ? (double)usage.Cached / usage.Input : 0;

    double ComputeCost(TokenUsage usage)
    {
        int nonCachedInput = usage.Input - usage.Cached;
        if (nonCachedInput < 0) nonCachedInput = 0;
        return nonCachedInput / 1000.0 * price.InputPricePerK
             + usage.Cached / 1000.0 * price.CachedPricePerK
             + usage.Output / 1000.0 * price.OutputPricePerK;
    }

    void Load()
    {
        PriceConfig? loaded = storageSystem.GetSetting<PriceConfig>(PriceKey);
        if (loaded != null) price = loaded;

        HistorySnapshot? snapshot = storageSystem.GetSetting<HistorySnapshot>(HistoryKey);
        if (snapshot != null)
        {
            foreach (DailyEntry entry in snapshot.Dailies)
            {
                DateTime day = DateTime.Parse(entry.Date);
                dailyMap[day] = new DailyAccumulator {
                    Usage = new TokenUsage { Total = entry.Total, Input = entry.Input, Cached = entry.Cached, Output = entry.Output },
                    ChatCount = entry.ChatCount
                };
            }
            foreach (TagEntry tag in snapshot.Tags)
                tagMap[tag.Tag] = tag.Count;
        }
    }

    void Save()
    {
        HistorySnapshot snapshot = new() {
            Dailies = dailyMap.Select(pair => new DailyEntry {
                Date = pair.Key.ToString("yyyy-MM-dd"),
                Total = pair.Value.Usage.Total,
                Input = pair.Value.Usage.Input,
                Cached = pair.Value.Usage.Cached,
                Output = pair.Value.Usage.Output,
                ChatCount = pair.Value.ChatCount
            }).ToList(),
            Tags = tagMap.Select(pair => new TagEntry { Tag = pair.Key, Count = pair.Value }).ToList()
        };
        storageSystem.SetSetting(HistoryKey, snapshot);
        storageSystem.SetSetting(PriceKey, price);
    }

    sealed class HistorySnapshot
    {
        public List<DailyEntry> Dailies { get; set; } = new();
        public List<TagEntry> Tags { get; set; } = new();
    }
    sealed class DailyEntry
    {
        public string Date { get; set; } = "";
        public int Total { get; set; }
        public int Input { get; set; }
        public int Cached { get; set; }
        public int Output { get; set; }
        public int ChatCount { get; set; }
    }
    sealed class TagEntry
    {
        public string Tag { get; set; } = "";
        public int Count { get; set; }
    }
}
