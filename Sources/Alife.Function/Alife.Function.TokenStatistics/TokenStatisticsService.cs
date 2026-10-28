using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 统计查询服务：基于对话记录存储（JSONL）做范围查询、分桶汇总、分页与排行。
/// 所有数字都由记录现算，不另存汇总，避免两份数据不一致。
/// 不依赖角色是否激活（见 <see cref="TokenStatisticsLocator"/>）。
/// </summary>
/// <param name="recordStore">记录存储。</param>
/// <param name="calendar">节假日日历（决定峰谷）。</param>
/// <param name="priceProfile">价格配置；为空时用内置 deepseek-flash 默认值。</param>
public sealed class TokenStatisticsService(
    ChatRecordStore recordStore,
    IHolidayCalendar calendar,
    PriceProfile? priceProfile = null)
{
    /// <summary>本服务使用的价格配置（面板上可改，改完重建服务即可生效）。</summary>
    public PriceProfile PriceProfile { get; } = priceProfile ?? PriceCalculator.CreateDefaultProfile();

    /// <summary>一个时间桶（柱状图的一根柱子）。</summary>
    public sealed record Bucket(
        DateTimeOffset Start,
        DateTimeOffset End,
        string Label,
        string FullLabel,
        int ChatCount,
        int InputTotal,
        int InputCached,
        int OutputTotal,
        double Cost,
        double InputMissCost,
        double InputHitCost,
        double OutputCost);

    /// <summary>一个统计范围内的汇总（费用按 缓存命中 / 输入未命中 / 输出 三段拆分）。</summary>
    public sealed record RangeSummary(
        int ChatCount,
        int InputTotal,
        int InputCached,
        int OutputTotal,
        double MissCost,
        double HitCost,
        double OutputCost)
    {
        /// <summary>输入侧费用（未命中 + 命中）。</summary>
        public double InputCost => MissCost + HitCost;

        public double Cost => MissCost + HitCost + OutputCost;
        public double HitRate => InputTotal > 0 ? (double)InputCached / InputTotal : 0;
        public double AverageCost => ChatCount > 0 ? Cost / ChatCount : 0;

        /// <summary>未命中 token 数。</summary>
        public int InputMissTokens => Math.Max(0, InputTotal - InputCached);
    }
    /// <summary>一条对话记录 + 标签/函数的字节占比 + 三段费用。</summary>
    public sealed record RecordView(
        ChatRecord Record,
        IReadOnlyList<WeightedName> TagWeights,
        IReadOnlyList<WeightedName> FuncWeights,
        double InputCost,
        double OutputCost,
        double MissCost,
        double HitCost,
        double HitRate,
        IReadOnlyList<string> TagNames,
        IReadOnlyList<string> FuncNames)
    {
        /// <summary>按「缓存 + 输入 + 输出」三段重算的总费用（随价格配置变化，不采用记录里存的值）。</summary>
        public double Cost => MissCost + HitCost + OutputCost;

        /// <summary>这条记录的输入侧是否含该标签。</summary>
        public bool HasTag(string name)
            => TagWeights.Any(weight => string.Equals(weight.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>这条记录的输出侧是否含该函数。</summary>
        public bool HasFunc(string name)
            => FuncWeights.Any(weight => string.Equals(weight.Name, name, StringComparison.OrdinalIgnoreCase));
    }
    /// <summary>排行中的一行：三段 token 与各自费用（已按字节占比分摊），供界面按任意列排序。</summary>
    public sealed record RankRow(
        string Name,
        int ChatCount,
        long CachedTokens,
        long InputMissTokens,
        long OutputTokens,
        double HitCost,
        double MissCost,
        double OutputCost)
    {
        /// <summary>该行总花费（缓存 + 输入 + 输出）。</summary>
        public double Cost => HitCost + MissCost + OutputCost;

        /// <summary>输入合计（缓存命中 + 未命中）。</summary>
        public long InputTokens => CachedTokens + InputMissTokens;
    }


    /// <summary>某个标签/函数在这条消息里的字节数和占比（占比 = 自身字节 ÷ 该侧总字节）。</summary>
    public sealed record WeightedName(string Name, double Weight, int Bytes);

/// <summary>某一天的命中率表现（给"优化目标"找依据用）。</summary>
public sealed record HitRateTarget(double Rate, DateTime Day, int SampleCount);

    /// <summary>没有可用标签/函数时的归类名，代表"这段内容就是纯文本"。</summary>
    public const string NoDominantName = "生文本";

    /// <summary>思考内容的归类名：AI 侧独立一段，与函数一起按 UTF-8 字节占比分摊输出侧。</summary>
    public const string ThinkingName = "思考";

    /// <summary>
    /// 取范围内记录（时间升序），算出每个标签/函数在这条记录里的**字节占比**与三段费用。
    /// 不再挑"主导"：一条消息里出现的每个标签/函数都按自己的字节占比分摊这条记录的 token 与费用。
    /// 标签/函数的字节数一律在读取时由原文重新解析，因此老记录也会套用新的字节口径。
    /// </summary>
    public IReadOnlyList<RecordView> QueryRecords(DateTimeOffset from, DateTimeOffset to, TagFilter filter)
    {
        IReadOnlyList<ChatRecord> records = recordStore.ReadRange(from, to);
        PriceProfile profile = PriceProfile;

        return records
            .OrderBy(record => record.Time)
            .Select(record => {
                UnitPrices prices = profile.PricesAt(record.Time.DateTime, calendar);
                int missTokens = Math.Max(0, record.InputTotal - record.InputCached);
                double missCost = missTokens / 1_000_000d * prices.InputMissPerM;
                double hitCost = record.InputCached / 1_000_000d * prices.InputHitPerM;
                double outputCost = record.OutputTotal / 1_000_000d * prices.OutputPerM;

                IReadOnlyList<MarkupHit> tags = MarkupParser.ParseUserTags(record.UserMessage);
                IReadOnlyList<MarkupHit> funcs = MarkupParser.ParseAiFunctions(record.AiMessage);

                return new RecordView(
                    record,
                    BuildWeights(tags, filter, MarkupParser.Utf8Bytes(record.UserMessage)),
                    BuildAiWeights(funcs, filter, record.AiMessage, record.AiThinking),
                    hitCost + missCost,
                    outputCost,
                    missCost,
                    hitCost,
                    record.InputTotal > 0 ? (double)record.InputCached / record.InputTotal : 0,
                    [.. tags.Select(hit => hit.Name).Distinct(StringComparer.OrdinalIgnoreCase)],
                    [.. funcs.Select(hit => hit.Name).Distinct(StringComparer.OrdinalIgnoreCase)]);
            })
            .ToList();
    }

    /// <summary>
    /// 把单条消息里的若干"出现"合并成 名字 → 字节占比。
    /// 被过滤的名字不参与分摊；如果一个名字都没剩下，就把整段内容记为「生文本」，保证分摊总量守恒。
    /// </summary>
    /// <summary>
    /// AI 侧的分摊：把「思考」当成独立一段，与回复里的函数一起按 UTF-8 字节占比归一；
    /// 回复里没有函数时，回复正文记为「生文本」，保证输出侧分摊守恒。
    /// </summary>
    static IReadOnlyList<WeightedName> BuildAiWeights(
        IReadOnlyList<MarkupHit> funcs, TagFilter filter, string aiMessage, string? aiThinking)
    {
        Dictionary<string, int> bytesByName = new(StringComparer.OrdinalIgnoreCase);

        foreach (MarkupHit hit in funcs)
        {
            string name = filter.Normalize(hit.Name);
            if (filter.IsExcluded(name))
                continue;
            bytesByName[name] = bytesByName.TryGetValue(name, out int existing)
                ? existing + hit.Bytes
                : hit.Bytes;
        }

        int messageBytes = MarkupParser.Utf8Bytes(aiMessage);
        // 「生文本」只吃"没被任何函数覆盖的正文"：函数片段 + 思考之后的剩余字节；被排除的名字不算正文
        int covered = funcs.Sum(hit => hit.Bytes)
            + (string.IsNullOrWhiteSpace(aiThinking) ? 0 : MarkupParser.Utf8Bytes(aiThinking));
        if (messageBytes > covered && filter.IsExcluded(NoDominantName) == false)
        {
            int rest = messageBytes - covered;
            bytesByName[NoDominantName] = bytesByName.TryGetValue(NoDominantName, out int existing) ? existing + rest : rest;
        }

        if (string.IsNullOrWhiteSpace(aiThinking) == false && filter.IsExcluded(ThinkingName) == false)
            bytesByName[ThinkingName] = MarkupParser.Utf8Bytes(aiThinking);

        if (bytesByName.Count == 0)
            return [];

        int total = bytesByName.Values.Sum();
        if (total <= 0)
            return [];

        return [.. bytesByName
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new WeightedName(pair.Key, (double)pair.Value / total, pair.Value))];
    }

    static IReadOnlyList<WeightedName> BuildWeights(IReadOnlyList<MarkupHit> hits, TagFilter filter, int messageBytes)
    {
        Dictionary<string, int> bytesByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (MarkupHit hit in hits)
        {
            string name = filter.Normalize(hit.Name);
            if (filter.IsExcluded(name))
                continue;
            bytesByName[name] = bytesByName.TryGetValue(name, out int existing)
                ? existing + hit.Bytes
                : hit.Bytes;
        }

        if (bytesByName.Count == 0)
            return messageBytes > 0 ? [new WeightedName(NoDominantName, 1, messageBytes)] : [];

        // 标签片段之外还有正文（例如消息以正文开头）时，剩余字节记到「生文本」，
        // 让该维度覆盖整条消息；这样跨维度合并占比才能保证合计 100%。
        int covered = hits.Sum(hit => hit.Bytes);   // 标签覆盖的字节（含被排除的），排除只是不计入名字，不当成正文
        if (messageBytes > covered && filter.IsExcluded(NoDominantName) == false)
        {
            int rest = messageBytes - covered;
            bytesByName[NoDominantName] = bytesByName.TryGetValue(NoDominantName, out int existing) ? existing + rest : rest;
        }

        int total = bytesByName.Values.Sum();
        if (total <= 0)
            return messageBytes > 0 ? [new WeightedName(NoDominantName, 1, messageBytes)] : [];

        return [.. bytesByName
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new WeightedName(pair.Key, (double)pair.Value / total, pair.Value))];
    }

    /// <summary>
    /// 历史最佳命中率目标：在给定观察窗口内按"天"聚合，取样本足够的那些日子里命中率最高的一天。
    /// 用来给"缓存优化空间"一个**有出处的目标值**，而不是拍一个固定百分比。
    /// </summary>
    public HitRateTarget? BestDailyHitRate(DateTimeOffset from, DateTimeOffset to, int minSamples)
    {
        IReadOnlyList<ChatRecord> records = recordStore.ReadRange(from, to);
        if (records.Count == 0)
            return null;

        HitRateTarget? best = null;
        foreach (var day in records.GroupBy(record => record.Time.Date))
        {
            if (day.Count() < minSamples)
                continue;

            long input = day.Sum(record => (long)record.InputTotal);
            long cached = day.Sum(record => (long)record.InputCached);
            if (input <= 0)
                continue;

            double rate = (double)cached / input;
            if (best is null || rate > best.Rate)
                best = new HitRateTarget(rate, day.Key, day.Count());
        }

        return best;
    }

    /// <summary>范围汇总。</summary>
    public RangeSummary Summarize(DateTimeOffset from, DateTimeOffset to)
    {
        IReadOnlyList<ChatRecord> records = recordStore.ReadRange(from, to);
        PriceProfile profile = PriceProfile;

        double missCost = 0;
        double hitCost = 0;
        double outputCost = 0;
        foreach (ChatRecord record in records)
        {
            UnitPrices prices = profile.PricesAt(record.Time.DateTime, calendar);
            int miss = Math.Max(0, record.InputTotal - record.InputCached);
            missCost += miss / 1_000_000d * prices.InputMissPerM;
            hitCost += record.InputCached / 1_000_000d * prices.InputHitPerM;
            outputCost += record.OutputTotal / 1_000_000d * prices.OutputPerM;
        }

        return new RangeSummary(
            records.Count,
            records.Sum(record => record.InputTotal),
            records.Sum(record => record.InputCached),
            records.Sum(record => record.OutputTotal),
            missCost,
            hitCost,
            outputCost);
    }

    /// <summary>
    /// 按时间分桶（柱状图）。桶宽由调用方决定：天→按小时，周/月→按天。
    /// 没有记录的桶也输出，保证横轴连续。
    /// </summary>
    public IReadOnlyList<Bucket> Bucketize(DateTimeOffset from, DateTimeOffset to, TimeSpan bucketWidth, BucketLabelMode labelMode)
    {
        IReadOnlyList<ChatRecord> records = recordStore.ReadRange(from, to);
        List<Bucket> buckets = new();

        for (DateTimeOffset start = from; start < to; start += bucketWidth)
        {
            DateTimeOffset end = start + bucketWidth;
            List<ChatRecord> slice = records.Where(record => record.Time >= start && record.Time < end).ToList();

            // 把费用按"未命中输入 / 命中输入 / 输出"拆开，供柱状图切换到价格口径
            PriceProfile profile = PriceProfile;
            double missCost = 0;
            double hitCost = 0;
            double outCost = 0;
            foreach (ChatRecord record in slice)
            {
                UnitPrices prices = profile.PricesAt(record.Time.DateTime, calendar);
                int miss = Math.Max(0, record.InputTotal - record.InputCached);
                missCost += miss / 1_000_000d * prices.InputMissPerM;
                hitCost += record.InputCached / 1_000_000d * prices.InputHitPerM;
                outCost += record.OutputTotal / 1_000_000d * prices.OutputPerM;
            }

            buckets.Add(new Bucket(
                start,
                end,
                FormatLabel(start, labelMode),
                start.ToString("MM-dd HH:mm") + " ~ " + end.ToString("MM-dd HH:mm"),
                slice.Count,
                slice.Sum(record => record.InputTotal),
                slice.Sum(record => record.InputCached),
                slice.Sum(record => record.OutputTotal),
                missCost + hitCost + outCost,
                missCost,
                hitCost,
                outCost));
        }
        return buckets;
    }

    /// <summary>
    /// 按给定的记录集合分桶：下钻（标签/函数筛选）或拖动记录区间后，图表只看这部分数据。
    /// 费用直接用记录视图里已算好的三段，避免重复计价。
    /// </summary>
    public IReadOnlyList<Bucket> Bucketize(
        IReadOnlyList<RecordView> views,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan bucketWidth,
        BucketLabelMode labelMode)
    {
        List<Bucket> buckets = new();
        if (bucketWidth <= TimeSpan.Zero || to <= from)
            return buckets;

        for (DateTimeOffset start = from; start < to; start += bucketWidth)
        {
            DateTimeOffset end = start + bucketWidth;
            List<RecordView> slice = [.. views.Where(view => view.Record.Time >= start && view.Record.Time < end)];

            buckets.Add(new Bucket(
                start,
                end,
                FormatLabel(start, labelMode),
                start.ToString("MM-dd HH:mm") + " ~ " + end.ToString("MM-dd HH:mm"),
                slice.Count,
                slice.Sum(view => view.Record.InputTotal),
                slice.Sum(view => view.Record.InputCached),
                slice.Sum(view => view.Record.OutputTotal),
                slice.Sum(view => view.Cost),
                slice.Sum(view => view.MissCost),
                slice.Sum(view => view.HitCost),
                slice.Sum(view => view.OutputCost)));
        }
        return buckets;
    }

    /// <summary>当前范围内已知的全部标签/函数名（去重），供过滤面板使用。</summary>
    public IReadOnlyList<string> KnownNames(DateTimeOffset from, DateTimeOffset to, TagFilter filter)
    {
        IReadOnlyList<ChatRecord> records = recordStore.ReadRange(from, to);

        List<string> names = [.. records
            .SelectMany(record => record.UserTags.Concat(record.AiFunctions))
            .Select(hit => filter.Normalize(hit.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)];

        // 思考是一段独立的输出侧内容，也让它在排除面板里可开关
        if (records.Any(record => string.IsNullOrWhiteSpace(record.AiThinking) == false)
            && names.Contains(ThinkingName, StringComparer.OrdinalIgnoreCase) == false)
        {
            names.Add(ThinkingName);
            names.Sort(StringComparer.Ordinal);
        }

        return names;
    }

    /// <summary>
    /// 范围内"带参数出现"的基础名（如 时间、源），供界面配置"忽略参数"。
    /// 只列真正带过参数的名字，没参数的标签不需要这个开关。
    /// </summary>
    public IReadOnlyList<string> ParameterizedBaseNames(DateTimeOffset from, DateTimeOffset to)
    {
        IReadOnlyList<ChatRecord> records = recordStore.ReadRange(from, to);

        return [.. records
            .SelectMany(record => record.UserTags.Concat(record.AiFunctions))
            .Select(hit => hit.Name)
            .Where(name => name.IndexOfAny(['(', '（']) >= 0)
            .Select(TagFilter.BaseNameOf)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)];
    }

    /// <summary>按标签聚合排行：每条记录的三段 token / 费用按各标签字节占比分摊。</summary>
    public IReadOnlyList<RankRow> RankByTags(IReadOnlyList<RecordView> views)
        => Rank(views, view => view.TagWeights);

    /// <summary>按函数聚合排行：同样按字节占比分摊。</summary>
    public IReadOnlyList<RankRow> RankByFunctions(IReadOnlyList<RecordView> views)
        => Rank(views, view => view.FuncWeights);

    IReadOnlyList<RankRow> Rank(IReadOnlyList<RecordView> views, Func<RecordView, IReadOnlyList<WeightedName>> selector)
    {
        Dictionary<string, Accumulator> map = new(StringComparer.OrdinalIgnoreCase);

        foreach (RecordView view in views)
        {
            IReadOnlyList<WeightedName> weights = selector(view);
            if (weights.Count == 0)
                continue;

            int missTokens = Math.Max(0, view.Record.InputTotal - view.Record.InputCached);

            foreach (WeightedName weight in weights)
            {
                if (map.TryGetValue(weight.Name, out Accumulator? acc) == false)
                {
                    acc = new Accumulator();
                    map[weight.Name] = acc;
                }

                // 出现次数：这条对话里出现过就算一次（同名多次只算一次）
                acc.ChatCount++;

                // token 与费用按字节占比分摊
                acc.CachedTokens += view.Record.InputCached * weight.Weight;
                acc.MissTokens += missTokens * weight.Weight;
                acc.OutputTokens += view.Record.OutputTotal * weight.Weight;
                acc.HitCost += view.HitCost * weight.Weight;
                acc.MissCost += view.MissCost * weight.Weight;
                acc.OutputCost += view.OutputCost * weight.Weight;
            }
        }

        return [.. map
            .Select(pair => new RankRow(
                pair.Key,
                pair.Value.ChatCount,
                (long)Math.Round(pair.Value.CachedTokens),
                (long)Math.Round(pair.Value.MissTokens),
                (long)Math.Round(pair.Value.OutputTokens),
                pair.Value.HitCost,
                pair.Value.MissCost,
                pair.Value.OutputCost))
            .OrderByDescending(row => row.Cost)
            .ThenByDescending(row => row.ChatCount)];
    }

    /// <summary>分摊累加器（用 double 累加，最后再取整）。</summary>
    sealed class Accumulator
    {
        public int ChatCount;
        public double CachedTokens;
        public double MissTokens;
        public double OutputTokens;
        public double HitCost;
        public double MissCost;
        public double OutputCost;
    }

    static string FormatLabel(DateTimeOffset start, BucketLabelMode mode) => mode switch
    {
        BucketLabelMode.Hour => start.ToString("HH时"),
        BucketLabelMode.Day => start.ToString("MM-dd"),
        _ => start.ToString("MM-dd HH:mm"),
    };
}

/// <summary>柱状图横轴标签的粒度。</summary>
public enum BucketLabelMode
{
    Hour,
    Day,
    Minute,
}