using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 规则化的开销诊断（保守版）：优先做「与上一段同长度窗口」的对比，
/// 样本不足或差异不显著时只陈述事实、不下结论，避免误导。
/// 纯函数，只依赖记录视图。
/// </summary>
public static class TokenStatisticsDiagnostics
{
    /// <summary>严重程度，决定界面配色。</summary>
    public enum Severity
    {
        /// <summary>纯事实，没有倾向。</summary>
        Info,
        /// <summary>值得注意，但属推测。</summary>
        Warning,
        /// <summary>结构性异常（仅在差异显著时给出）。</summary>
        Critical,
    }

    /// <summary>一条诊断：结论 + 依据 + 建议 + 等级。</summary>
    public sealed record Insight(string Title, string Detail, string Advice, string Basis, Severity Level);

    // 判断门槛：不满足就不下结论
    /// <summary>做趋势/对比判断所需的最小样本数（外部也用它筛"历史最佳日"）。</summary>
    public const int MinSamplesForTrend = 10;

    /// <summary>找"历史最佳日"时回看的窗口（天）。</summary>
    public const int LookbackDays = 30;
    const double SignificantCostChange = 0.20;      // 花费变化 ≥20% 才说"明显"
    const double SignificantHitRateChange = 0.05;   // 命中率变化 ≥5pp 才说"明显"
    const double SignificantShareChange = 0.10;     // 占比变化 ≥10pp
    const int MinSamplesForSavingEstimate = 20;

    public static IReadOnlyList<Insight> Analyze(
        IReadOnlyList<TokenStatisticsService.RecordView> current,
        IReadOnlyList<TokenStatisticsService.RecordView> baseline,
        TimeSpan period,
        Func<DateTime, bool> isPeak,
        string holidayNote, TokenStatisticsService.HitRateTarget? hitRateTarget)
    {
        List<Insight> insights = new();
        if (current.Count == 0)
            return insights;

        Stats now = Stats.From(current, isPeak);
        Stats before = Stats.From(baseline, isPeak);
        bool hasBaseline = before.Count > 0;
        string periodText = DescribePeriod(period);
        string comparePeriod = $"前 {periodText}";

        // ── 1. 规模与费用变化（有基线才谈变化） ──
        if (hasBaseline)
        {
            double costDelta = before.Cost > 0 ? (now.Cost - before.Cost) / before.Cost : 0;
            double countDelta = before.Count > 0 ? (double)(now.Count - before.Count) / before.Count : 0;
            string trend = Math.Abs(costDelta) >= SignificantCostChange
                ? $"花费比{comparePeriod}（{before.Count} 条）{(costDelta > 0 ? "上升" : "下降")} {Math.Abs(costDelta):P0}"
                : $"花费比{comparePeriod}变化 {Signed(costDelta * 100)}%，但没到 {SignificantCostChange:P0} 的变化门槛，按持平处理";

            insights.Add(new Insight(
                "规模与费用",
                $"本期（{periodText}）{now.Count} 条对话、花了 ¥{now.Cost:F4}；{trend}；条数 {Signed(countDelta * 100)}%。",
                Math.Abs(costDelta) >= SignificantCostChange
                    ? "费用有明显变化，建议结合下面的来源/函数排行确认是谁变了。"
                    : "变化在噪声范围内，不必据此下结论。",
                $"依据：本期 {now.Count} 条 vs {comparePeriod} {before.Count} 条",
                Math.Abs(costDelta) >= SignificantCostChange ? Severity.Warning : Severity.Info));
        }
        else
        {
            insights.Add(new Insight(
                "规模与费用",
                $"本期（{periodText}）{now.Count} 条、¥{now.Cost:F4}。{comparePeriod}没有数据，无法做同比。",
                "缺少对比基线，下面的结论只描述当前结构，不代表「变好/变差」。",
                $"依据：本期 {now.Count} 条；{comparePeriod} 0 条",
                Severity.Info));
        }

        // ── 2. 三段费用结构（纯事实 + 指出最大头） ──
        double cachedShare = now.HitCost / now.Cost;
        double missShare = now.MissCost / now.Cost;
        double outShare = now.OutputCost / now.Cost;

        string structureAdvice;
        if (missShare >= cachedShare && missShare >= outShare)
        {
            structureAdvice = $"输入段占 {missShare:P0}，是最大头：提升缓存命中率（少注入重复内容、固定前缀）收益最直接。";
        }
        else if (outShare >= cachedShare && outShare >= missShare)
        {
            structureAdvice = $"输出段占 {outShare:P0}，是最大头：优先压缩输出长度，或先在本地把内容压成要点再输出。";
        }
        else
        {
            structureAdvice = $"缓存段占 {cachedShare:P0}，说明命中率已经很高，这一段可优化空间有限。";
        }

        insights.Add(new Insight(
            "费用结构",
            $"本期总花费 ¥{now.Cost:F4}，按三段拆开（括号是占本期总花费的比例）：" +
            $"缓存 ¥{now.HitCost:F4}（{cachedShare:P1}）、输入 ¥{now.MissCost:F4}（{missShare:P1}）、输出 ¥{now.OutputCost:F4}（{outShare:P1}）。" +
            $"整体缓存命中率 {now.HitRate:P1}，即输入 token 里有 {now.HitRate:P0} 按缓存价计费、其余按未命中价计费。",
            structureAdvice,
            $"依据：{now.Count} 条记录逐条按各自时段适用单价累加；三段相加 = 总花费",
            Severity.Info));

        // ── 3. 单条输入的规模分布（看长尾：少数超长上下文拉高整体成本） ──
        long[] inputs = [.. current.Select(view => (long)view.Record.InputTotal).OrderBy(value => value)];
        long p50 = Percentile(inputs, 0.50);
        long p90 = Percentile(inputs, 0.90);
        long maxInput = inputs.Length > 0 ? inputs[^1] : 0;

        string sizeAdvice;
        if (p50 > 0 && p90 >= p50 * 2.5)
        {
            sizeAdvice = $"少数对话特别长：最长的 10% 从 {(double)p90:N0} token 起，是中间那条的 {(double)p90 / p50:F1} 倍，成本被这几条拉高了。优先处理这批——用下面的排行和卡片就能定位到具体哪几条。";
        }
        else if (p50 > 0 && maxInput >= p50 * 4)
        {
            sizeAdvice = $"大部分对话不算长（中间那条 {p50:N0} token），但有一条到 {maxInput:N0} token，这种单条超长上下文值得单独翻出来看。";
        }
        else
        {
            sizeAdvice = "每条对话的输入长度比较接近，没有异常长的上下文；想让成本降下来，得从每条都有的内容入手。";
        }

        insights.Add(new Insight(
            "上下文长度分布",
            $"本期 {now.Count} 条对话，每条塞进去的输入长度：一半的对话不到 {p50:N0} token，最长的 10% 从 {p90:N0} token 起，最长的一条 {maxInput:N0} token（输入合计 {now.MissTokens + now.CachedTokens:N0}）。",
            sizeAdvice,
            "依据：把本期每条对话的输入 token 从小到大排，取中间那条（一半的对话比它短）和倒数第二成的起点（最长的 10% 从它开始）",
            Severity.Info));
        // ── 3. 命中率变化（样本足够 + 变化显著才下结论） ──
        if (hasBaseline && now.Count >= MinSamplesForTrend && before.Count >= MinSamplesForTrend)
        {
            double delta = now.HitRate - before.HitRate;
            bool significant = Math.Abs(delta) >= SignificantHitRateChange;
            insights.Add(new Insight(
                "缓存命中率变化",
                $"本期输入里有 {now.HitRate:P1} 是直接命中缓存、按便宜价计费；{comparePeriod}是 {before.HitRate:P1}，相差 {Signed(delta * 100)} 个百分点。",
                significant
                    ? (delta > 0
                        ? "命中率上升，多半是上下文前缀更稳定了；继续保持。"
                        : "命中率下降，通常是上下文变动变大（历史变长、注入了每次都不同的内容）。可对比下钻里的长对话确认。")
                    : "变化很小，属于正常波动，不建议据此改动。",
                $"依据：本期 {now.Count} 条 vs {comparePeriod} {before.Count} 条（门槛：各 ≥{MinSamplesForTrend} 条、变化 ≥{SignificantHitRateChange:P0}）",
                significant ? Severity.Warning : Severity.Info));
        }
        else if (!hasBaseline)
        {
            insights.Add(new Insight(
                "缓存命中率",
                $"本期整体缓存命中率 {now.HitRate:P1}。",
                "没有对比基线，无法判断这个命中率是偏高还是偏低。",
                $"依据：{now.Count} 条",
                Severity.Info));
        }

        // ── 4. 价格覆盖时段（只陈述比例，并说明口径） ──
        double overrideShare = now.Cost > 0 ? now.OverrideCost / now.Cost : 0;
        insights.Add(new Insight(
            "价格覆盖时段",
            $"有 {now.OverrideCount}/{now.Count} 条对话落在加价时段里，它们占了 {overrideShare:P1} 的花费（¥{now.OverrideCost:F4}）。",
            overrideShare >= 0.5
                ? "这些时段的单价高于基线；把非实时任务（记忆整理、网页预取等）挪到基线时段能省一部分。"
                : "覆盖时段花费占比不高，时段调度不是当前重点。",
            $"依据：按当前价格配置的覆盖规则判定（工作日判定结合节假日表：{holidayNote}）",
            Severity.Info));

        // ── 5. 缓存优化空间（目标值来自"历史最佳日"，不是拍固定百分比） ──
        if (now.Count >= MinSamplesForSavingEstimate)
        {
            double perTokenGap = now.MissTokens > 0
                ? (now.MissCost / now.MissTokens) - (now.HitCost / Math.Max(1, now.CachedTokens))
                : 0;

            if (hitRateTarget is null)
            {
                insights.Add(new Insight(
                    "缓存优化空间",
                    $"本期缓存命中率 {now.HitRate:P1}，但最近没有样本足够的历史日子可以作对照。",
                    "样本不足时不估算，避免给出没有依据的目标值；把跨度拉长后再看。",
                    $"依据：本期 {now.Count} 条；目标值需要历史里某天样本 ≥{MinSamplesForTrend} 条",
                    Severity.Info));
            }
            else if (now.HitRate >= hitRateTarget.Rate)
            {
                insights.Add(new Insight(
                    "缓存优化空间",
                    $"本期命中率 {now.HitRate:P1}，已达到或超过观察窗口内的最好水平（{hitRateTarget.Day:MM-dd} 的 {hitRateTarget.Rate:P1}）。",
                    "命中率已在历史高位，继续提升的空间有限；要再省只能减少注入内容本身。",
                    $"依据：近 {LookbackDays} 天按天聚合，取样本 ≥{MinSamplesForTrend} 条的日子里命中率最高的一天",
                    Severity.Info));
            }
            else
            {
                double gap = hitRateTarget.Rate - now.HitRate;
                double saving = (now.MissTokens + now.CachedTokens) * gap * perTokenGap;
                insights.Add(new Insight(
                    "缓存优化空间",
                    $"若命中率从 {now.HitRate:P1} 提到历史最好水平 {hitRateTarget.Rate:P1}（{hitRateTarget.Day:MM-dd}，{hitRateTarget.SampleCount} 条），按现在的单价简单折算，大约能省 ¥{saving:F4}（占本期 {saving / Math.Max(1e-9, now.Cost):P1}）。",
                "这只是个量级：真能省多少，取决于前缀能不能稳定下来，通常还要同时减少每次都不同的注入内容。",
                    $"依据：目标 = 近 {LookbackDays} 天里样本 ≥{MinSamplesForTrend} 条的日子中命中率最高的一天；这是按比例推算的参考值，不是实测结果",
                    Severity.Info));
            }
        }

        return insights;
    }
    /// <summary>某一组（按主导标签）的聚合结果。</summary>
    sealed record GroupStats(string Name, int Count, double Cost, double MissCost, double HitCost, double OutputCost, long Input, long Cached, long Output)
    {
        public double HitRate => Input > 0 ? (double)Cached / Input : 0;
    }
    /// <summary>
    /// 按标签聚合：每条记录的三段 token / 费用按各标签字节占比分摊（不再挑"主导"）。
    /// </summary>
    static List<GroupStats> GroupByTag(IReadOnlyList<TokenStatisticsService.RecordView> views)
    {
        Dictionary<string, TagAccumulator> map = new(StringComparer.OrdinalIgnoreCase);

        foreach (TokenStatisticsService.RecordView view in views)
        {
            double missTokens = Math.Max(0, view.Record.InputTotal - view.Record.InputCached);
            foreach (TokenStatisticsService.WeightedName weight in view.TagWeights)
            {
                if (map.TryGetValue(weight.Name, out TagAccumulator? acc) == false)
                {
                    acc = new TagAccumulator();
                    map[weight.Name] = acc;
                }

                acc.Count++;
                acc.Cost += view.Cost * weight.Weight;
                acc.MissCost += view.MissCost * weight.Weight;
                acc.HitCost += view.HitCost * weight.Weight;
                acc.OutputCost += view.OutputCost * weight.Weight;
                acc.Input += (long)Math.Round(view.Record.InputTotal * weight.Weight);
                acc.Cached += (long)Math.Round(view.Record.InputCached * weight.Weight);
                acc.Output += (long)Math.Round(view.Record.OutputTotal * weight.Weight);
            }
        }

        return [.. map
            .Select(pair => new GroupStats(
                pair.Key,
                pair.Value.Count,
                pair.Value.Cost,
                pair.Value.MissCost,
                pair.Value.HitCost,
                pair.Value.OutputCost,
                pair.Value.Input,
                pair.Value.Cached,
                pair.Value.Output))
            .OrderByDescending(item => item.Cost)];
    }
    /// <summary>按标签分摊的累加器。</summary>
    sealed class TagAccumulator
    {
        public int Count;
        public double Cost;
        public double MissCost;
        public double HitCost;
        public double OutputCost;
        public long Input;
        public long Cached;
        public long Output;
    }

    /// <summary>整体聚合。</summary>
    sealed record Stats(
        int Count,
        double Cost,
        double MissCost,
        double HitCost,
        double OutputCost,
        long MissTokens,
        long CachedTokens,
        int OverrideCount,
        double OverrideCost)
    {
        public double HitRate => MissTokens + CachedTokens > 0 ? (double)CachedTokens / (MissTokens + CachedTokens) : 0;
        public double OutShare => Cost > 0 ? OutputCost / Cost : 0;

        public static Stats From(IReadOnlyList<TokenStatisticsService.RecordView> views, Func<DateTime, bool> isPeak)
        {
            int overrideCount = 0;
            double overrideCost = 0;
            foreach (var view in views)
            {
                if (isPeak(view.Record.Time.DateTime))
                {
                    overrideCount++;
                    overrideCost += view.Cost;
                }
            }

            return new Stats(
                views.Count,
                views.Sum(view => view.Cost),
                views.Sum(view => view.MissCost),
                views.Sum(view => view.HitCost),
                views.Sum(view => view.OutputCost),
                views.Sum(view => (long)Math.Max(0, view.Record.InputTotal - view.Record.InputCached)),
                views.Sum(view => (long)view.Record.InputCached),
                overrideCount,
                overrideCost);
        }
    }


    /// <summary>把时间片描述成人话：1 天 / 1.2 天 / 3 小时。</summary>
    static string DescribePeriod(TimeSpan period)
        => period.TotalDays >= 1
            ? $"{period.TotalDays:0.#} 天"
            : $"{period.TotalHours:0.#} 小时";
    /// <summary>取分位值（输入需已升序；用最近秩法，样本少时也能给出直观结果）。</summary>
    static long Percentile(IReadOnlyList<long> sorted, double ratio)
    {
        if (sorted.Count == 0)
            return 0;
        int index = (int)Math.Round((sorted.Count - 1) * ratio);
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
    /// <summary>正数带 + 号，负数自带 -，避免自定义格式出现 "-+0.0"。</summary>
    static string Signed(double value) => (value >= 0 ? "+" : "") + value.ToString("0.0");
    /// <summary>把 源(QChat) 显示成 源·QChat。</summary>
    static string Shorten(string name)
    {
        int open = name.IndexOfAny(['(', '（']);
        int close = name.LastIndexOfAny([')', '）']);
        if (open < 0 || close <= open)
            return name;
        return name.Substring(0, open) + "·" + name.Substring(open + 1, close - open - 1);
    }
}