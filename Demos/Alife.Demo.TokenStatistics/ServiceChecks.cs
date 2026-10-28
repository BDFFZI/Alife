using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alife.Function.TokenStatistics;

namespace Alife.Demo.TokenStatistics;

/// <summary>
/// 针对统计页按钮背后逻辑的确定性验证：
/// 范围汇总、分桶（按小时/按天）、标签/函数字节占比分摊、默认过滤、分页切片。
/// </summary>
public static class ServiceChecks
{
    public static int Run()
    {
        int failures = 0;

        string tempRoot = Path.Combine(Path.GetTempPath(), "alife-tokenstats-check-" + Guid.NewGuid().ToString("N"));
        string recordsFolder = Path.Combine(tempRoot, "records");
        try
        {
            ChatRecordStore store = new(recordsFolder);

            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset today = new(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);

            // 今天 3 条（同一小时）、昨天 2 条、上月 1 条
            store.Append(MakeRecord(today.AddHours(9), 1000, 900, 50, "源(ChatWindow)", "Speak"));
            store.Append(MakeRecord(today.AddHours(9).AddMinutes(10), 2000, 1800, 80, "源(ChatWindow)", "Speak"));
            store.Append(MakeRecord(today.AddHours(11), 3000, 2700, 120, "源(AuditoryService)", "Expression"));
            store.Append(MakeRecord(today.AddDays(-1).AddHours(20), 4000, 3600, 200, "系统推送消息", "Motion"));
            store.Append(MakeRecord(today.AddDays(-1).AddHours(21), 5000, 4500, 300, "源(SystemEventService)", "Memorize"));
            store.Append(MakeRecord(today.AddMonths(-1), 6000, 5400, 400, "源(ChatWindow)", "Speak"));

            TokenStatisticsService service = new(store, NoHolidayCalendar.Instance);
            TagFilter filter = new();

            // 1. 今天范围汇总：应为 3 条
            var todaySummary = service.Summarize(today, today.AddDays(1));
            Check(ref failures, "今天条数=3", todaySummary.ChatCount == 3, todaySummary.ChatCount);
            Check(ref failures, "今天输入合计=6000", todaySummary.InputTotal == 6000, todaySummary.InputTotal);
            Check(ref failures, "今天输入缓存合计=5400", todaySummary.InputCached == 5400, todaySummary.InputCached);
            Check(ref failures, "今天输出合计=250", todaySummary.OutputTotal == 250, todaySummary.OutputTotal);

            // 2. 本周汇总：应含今天 3 条 + 昨天 2 条（取决于今天星期，至少覆盖今天）
            int weekdayOffset = ((int)today.DayOfWeek + 6) % 7;
            DateTimeOffset weekStart = today.AddDays(-weekdayOffset);
            var weekSummary = service.Summarize(weekStart, weekStart.AddDays(7));
            Check(ref failures, "本周条数>=3", weekSummary.ChatCount >= 3, weekSummary.ChatCount);

            // 3. 分桶：今天按小时应有 24 个桶，且有数据的桶为 2 个（9 点、11 点）
            var hourly = service.Bucketize(today, today.AddDays(1), TimeSpan.FromHours(1), BucketLabelMode.Hour);
            Check(ref failures, "按小时桶数=24", hourly.Count == 24, hourly.Count);
            int nonEmptyHour = hourly.Count(b => b.InputTotal > 0);
            Check(ref failures, "有数据的按小时桶=2", nonEmptyHour == 2, nonEmptyHour);

            // 4. 分桶：本周按天应为 7 个桶
            var daily = service.Bucketize(weekStart, weekStart.AddDays(7), TimeSpan.FromDays(1), BucketLabelMode.Day);
            Check(ref failures, "按天桶数=7", daily.Count == 7, daily.Count);

            // 5. 字节占比：被过滤的「系统推送消息」不参与分摊，只剩「生文本」
            var records = service.QueryRecords(today.AddDays(-1), today.AddDays(1), filter);
            var pushRecord = records.FirstOrDefault(r => r.Record.InputTotal == 4000);
            Check(ref failures, "系统推送消息 默认参与统计",
                pushRecord != null && pushRecord.TagWeights.Any(w => w.Name == "系统推送消息"),
                pushRecord == null ? "null" : string.Join(",", pushRecord.TagWeights.Select(w => $"{w.Name}:{w.Weight:P0}")));

            var eventRecord = records.FirstOrDefault(r => r.Record.InputTotal == 5000);
            Check(ref failures, "事件记录标签含 源(SystemEventService) 且权重合计=1",
                eventRecord != null
                && eventRecord.TagWeights.Any(w => w.Name == "源(SystemEventService)")
                && Math.Abs(eventRecord.TagWeights.Sum(w => w.Weight) - 1) < 1e-9,
                eventRecord == null ? "null" : string.Join(",", eventRecord.TagWeights.Select(w => $"{w.Name}:{w.Weight:P0}")));

            // 6. 函数同样按字节占比
            Check(ref failures, "事件记录函数含 Memorize，思考独立成一类",
                eventRecord != null
                && eventRecord.FuncWeights.Any(w => w.Name == "Memorize")
                && eventRecord.FuncWeights.Any(w => w.Name == TokenStatisticsService.ThinkingName)
                && Math.Abs(eventRecord.FuncWeights.Sum(w => w.Weight) - 1) < 1e-9,
                eventRecord == null ? "null" : string.Join(",", eventRecord.FuncWeights.Select(w => $"{w.Name}:{w.Weight:P0}")));



            // 7. 分页切片：共 5 条，每页 2 条 → 第 1 页 2 条、第 3 页 1 条
            var all = service.QueryRecords(today.AddDays(-1), today.AddDays(1), filter);
            Check(ref failures, "范围记录数=5", all.Count == 5, all.Count);
            var page1 = all.Skip(0).Take(2).ToList();
            var page3 = all.Skip(4).Take(2).ToList();
            Check(ref failures, "第1页=2条", page1.Count == 2, page1.Count);
            Check(ref failures, "第3页=1条", page3.Count == 1, page3.Count);

            // 6.1 多标签按字节占比分摊，且分摊后总量守恒
            string multiMessage = "[源(Alpha)]包一包二包三包四[源(Beta)]测一测二测三测四测五测六测七测八测九测十";
            store.Append(MakeRawRecord(today.AddHours(23), 1234, 400, 100, multiMessage, "<Speak>回复</Speak>", now));
            var afterMulti = service.QueryRecords(today, today.AddDays(1), filter);
            var multi = afterMulti.FirstOrDefault(r => r.Record.InputTotal == 1234);
            double alphaWeight = multi?.TagWeights.FirstOrDefault(w => w.Name == "源(Alpha)").Weight ?? 0;
            double betaWeight = multi?.TagWeights.FirstOrDefault(w => w.Name == "源(Beta)").Weight ?? 0;
            Check(ref failures, "多标签权重合计=1", Math.Abs(alphaWeight + betaWeight - 1) < 1e-9, alphaWeight + betaWeight);
            Check(ref failures, "Beta 字节多于 Alpha → 权重更大", betaWeight > alphaWeight, $"alpha={alphaWeight:P1} beta={betaWeight:P1}");

            var multiRank = service.RankByTags(afterMulti);
            double rankedCost = multiRank.Sum(row => row.Cost);
            double totalCost = afterMulti.Sum(view => view.Cost);
            Check(ref failures, "标签排行分摊合计=总花费", Math.Abs(rankedCost - totalCost) < 1e-6, $"Σ={rankedCost:F6} 总={totalCost:F6}");
            // 8. 上月记录不计入今天范围
            var lastMonthSummary = service.Summarize(today.AddMonths(-1), today.AddMonths(-1).AddDays(1));
            Check(ref failures, "上月范围条数=1", lastMonthSummary.ChatCount == 1, lastMonthSummary.ChatCount);

            // 9. 90 天保留：清理后早于保留期的月文件被删除
            string oldMonthFile = Path.Combine(recordsFolder, now.AddDays(-200).ToString("yyyy-MM") + ".jsonl");
            File.WriteAllText(oldMonthFile, "");
            store.PruneExpired(now);
            Check(ref failures, "过期月文件已删除", File.Exists(oldMonthFile) == false, File.Exists(oldMonthFile));

            // 10. 函数解析的边界：自闭合 / 嵌套（互斥，不重复计） / 同名嵌套 / 未闭合
            string selfClosing = "<Expression option=\"微笑\"/>";
            var selfHits = MarkupParser.ParseAiFunctions(selfClosing);
            Check(ref failures, "自闭合函数计自身",
                selfHits.Count == 1 && selfHits[0].Name == "Expression"
                && selfHits[0].Bytes == MarkupParser.Utf8Bytes(selfClosing),
                string.Join(",", selfHits.Select(h => $"{h.Name}:{h.Bytes}")));

            string nested = "<Speak>甲<Expression x=\"1\"/>乙</Speak>";
            var nestedHits = MarkupParser.ParseAiFunctions(nested);
            int nestedSum = nestedHits.Sum(h => h.Bytes);
            int speakBytes = nestedHits.FirstOrDefault(h => h.Name == "Speak").Bytes;
            Check(ref failures, "嵌套函数字节互斥且守恒",
                nestedSum == MarkupParser.Utf8Bytes(nested) && speakBytes == MarkupParser.Utf8Bytes(nested) - MarkupParser.Utf8Bytes("<Expression x=\"1\"/>"),
                $"总和={nestedSum} Speak={speakBytes}");

            string sameNameNested = "<A>a<A>b</A>c</A>";
            var sameHits = MarkupParser.ParseAiFunctions(sameNameNested);
            int sameSum = sameHits.Sum(h => h.Bytes);
            Check(ref failures, "同名嵌套不截断且守恒",
                sameHits.Count == 2 && sameSum == MarkupParser.Utf8Bytes(sameNameNested),
                $"个数={sameHits.Count} 总和={sameSum}");

            string unclosed = "<Unclosed>abc";
            var unclosedHits = MarkupParser.ParseAiFunctions(unclosed);
            Check(ref failures, "未闭合函数收到消息末尾",
                unclosedHits.Count == 1 && unclosedHits[0].Bytes == MarkupParser.Utf8Bytes(unclosed),
                string.Join(",", unclosedHits.Select(h => $"{h.Name}:{h.Bytes}")));

            // 11. 历史最佳日命中率：样本够的日子里取最高，样本不足的日子不算
            string targetRoot = Path.Combine(tempRoot, "target");
            ChatRecordStore targetStore = new(targetRoot);
            for (int i = 0; i < 12; i++)
                targetStore.Append(MakeRecord(today.AddDays(-3).AddHours(9).AddMinutes(i), 1000, 900, 10, "源(A)", "Speak"));   // 90%
            for (int i = 0; i < 12; i++)
                targetStore.Append(MakeRecord(today.AddDays(-2).AddHours(9).AddMinutes(i), 1000, 100, 10, "源(A)", "Speak"));   // 10%
            for (int i = 0; i < 2; i++)
                targetStore.Append(MakeRecord(today.AddDays(-1).AddHours(9).AddMinutes(i), 1000, 1000, 10, "源(A)", "Speak"));  // 100% 但只有 2 条

            TokenStatisticsService targetService = new(targetStore, NoHolidayCalendar.Instance);
            var target = targetService.BestDailyHitRate(today.AddDays(-7), today, 10);
            Check(ref failures, "历史最佳日命中率=90%（样本不足的日子跳过）",
                target != null && Math.Abs(target.Rate - 0.9) < 1e-9 && target.SampleCount == 12,
                target == null ? "null" : $"{target.Rate:P0} / {target.SampleCount} 条 / {target.Day:MM-dd}");

            var noTarget = targetService.BestDailyHitRate(today.AddDays(-7), today, 50);
            Check(ref failures, "样本门槛过高时返回 null（不硬凑目标值）", noTarget == null, noTarget?.Rate.ToString("P0") ?? "null");

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "✔ 服务层检查全部通过" : $"✘ 服务层检查失败 {failures} 项");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    static ChatRecord MakeRecord(DateTimeOffset time, int input, int cached, int output, string tagName, string funcName)
    {
        string userMessage = $"[时间({time:yyyy-MM-dd HH:mm:ss})]\n[{tagName}]测试内容";
        string aiMessage = $"<{funcName}>回复内容</{funcName}>";
        return new ChatRecord {
            Id = Guid.NewGuid().ToString("N"),
            Time = time,
            PriceProfile = PriceCalculator.DefaultProfileName,
            InputTotal = input,
            InputCached = cached,
            OutputTotal = output,
            Cost = PriceCalculator.CostAt(PriceCalculator.CreateDefaultProfile(), time.DateTime, NoHolidayCalendar.Instance, input, cached, output),
            UserMessage = userMessage,
            AiMessage = aiMessage,
            AiThinking = "思考内容",
            UserTags = MarkupParser.ParseUserTags(userMessage).ToList(),
            AiFunctions = MarkupParser.ParseAiFunctions(aiMessage).ToList(),
        };
    }

    static ChatRecord MakeRawRecord(DateTimeOffset time, int input, int cached, int output, string userMessage, string aiMessage, DateTimeOffset now)
        => new() {
            Id = Guid.NewGuid().ToString("N"),
            Time = time,
            PriceProfile = PriceCalculator.DefaultProfileName,
            InputTotal = input,
            InputCached = cached,
            OutputTotal = output,
            Cost = PriceCalculator.CostAt(PriceCalculator.CreateDefaultProfile(), time.DateTime, NoHolidayCalendar.Instance, input, cached, output),
            UserMessage = userMessage,
            AiMessage = aiMessage,
            AiThinking = "",
            UserTags = MarkupParser.ParseUserTags(userMessage).ToList(),
            AiFunctions = MarkupParser.ParseAiFunctions(aiMessage).ToList(),
        };
    static void Check(ref int failures, string name, bool ok, object actual)
    {
        if (ok)
        {
            Console.WriteLine($"  ✔ {name}");
            return;
        }
        failures++;
        Console.WriteLine($"  ✘ {name}（实际: {actual}）");
    }
}
