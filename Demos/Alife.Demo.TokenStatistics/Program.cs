using System.Text.Json;
using Alife.Function.TokenStatistics;
using Alife.Demo.TokenStatistics;

// service 模式：验证统计页按钮背后的服务层逻辑（范围/分桶/分摊/过滤/分页/保留期）
if (args.Contains("service"))
    return ServiceChecks.Run();

// funcs 模式：看某些 AI 回复里，每个函数的字节是怎么分的（排查嵌套互斥、未配对被丢弃）。
// 用法：dotnet run -- funcs "<records 目录>" "<消息里的关键字>" [最多几条]
if (args.Contains("funcs"))
{
    string folder = args.Length > 1 ? args[1] : @"C:\Users\13309\Desktop\Alife\Storage\Character\测试\TokenStatistics\records";
    string keyword = args.Length > 2 ? args[2] : "<";
    int max = args.Length > 3 && int.TryParse(args[3], out int parsedMax) ? parsedMax : 3;

    ChatRecordStore store = new(folder);
    TokenStatisticsService service = new(store, NoHolidayCalendar.Instance);
    DateTimeOffset now = DateTimeOffset.Now;
    IReadOnlyList<TokenStatisticsService.RecordView> views =
        service.QueryRecords(now.AddDays(-30), now.AddDays(1), new TagFilter());

    int shown = 0;
    foreach (TokenStatisticsService.RecordView view in views)
    {
        if (view.Record.AiMessage.Contains(keyword, StringComparison.Ordinal) == false)
            continue;

        Console.WriteLine($"── {view.Record.Time:MM-dd HH:mm}  AiMessage {view.Record.AiMessage.Length} 字符");
        foreach (TokenStatisticsService.WeightedName weight in view.FuncWeights)
            Console.WriteLine($"     {weight.Name,-24} {weight.Bytes,7} 字节  {weight.Weight:P1}");
        Console.WriteLine("   原文:");
        foreach (string line in view.Record.AiMessage.Split('\n')[..Math.Min(12, view.Record.AiMessage.Split('\n').Length)])
            Console.WriteLine("     │ " + line.TrimEnd('\r'));

        if (++shown >= max)
            break;
    }

    return 0;
}

// live 模式：对真实记录目录做一次回归——
// 标签/函数识别结果 + 字节守恒（每个维度都要覆盖整条消息），用来验证解析规则改动。
// 用法：dotnet run -- live "<records 目录>" [天数]
if (args.Contains("live"))
{
    string folder = args.Length > 1 ? args[1] : @"C:\Users\13309\Desktop\Alife\Storage\Character\测试\TokenStatistics\records";
    int days = args.Length > 2 && int.TryParse(args[2], out int parsedDays) ? parsedDays : 30;

    ChatRecordStore store = new(folder);
    TokenStatisticsService service = new(store, NoHolidayCalendar.Instance);
    DateTimeOffset now = DateTimeOffset.Now;
    DateTimeOffset from = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset).AddDays(-(days - 1));

    IReadOnlyList<TokenStatisticsService.RecordView> views =
        service.QueryRecords(from, now.AddDays(1), new TagFilter());
    Console.WriteLine($"目录={folder}");
    Console.WriteLine($"近 {days} 天条数={views.Count}");

    List<TokenStatisticsService.RecordView> broken = [];
    foreach (TokenStatisticsService.RecordView view in views)
    {
        int tagBytes = view.TagWeights.Sum(weight => weight.Bytes);
        int funcBytes = view.FuncWeights.Sum(weight => weight.Bytes);
        int messageBytes = MarkupParser.Utf8Bytes(view.Record.UserMessage);
        int replyBytes = MarkupParser.Utf8Bytes(view.Record.AiMessage)
            + (string.IsNullOrWhiteSpace(view.Record.AiThinking) ? 0 : MarkupParser.Utf8Bytes(view.Record.AiThinking));
        if (tagBytes != messageBytes || funcBytes != replyBytes)
            broken.Add(view);
    }

    Console.WriteLine($"字节守恒校验：{views.Count - broken.Count}/{views.Count} 条通过");
    foreach (TokenStatisticsService.RecordView view in broken.Take(5))
    {
        int reply = MarkupParser.Utf8Bytes(view.Record.AiMessage);
        int thinking = string.IsNullOrWhiteSpace(view.Record.AiThinking) ? 0 : MarkupParser.Utf8Bytes(view.Record.AiThinking);
        Console.WriteLine($"  ✘ {view.Record.Time:MM-dd HH:mm}"
            + $"  标签 {view.TagWeights.Sum(w => w.Bytes)}/{MarkupParser.Utf8Bytes(view.Record.UserMessage)}"
            + $"  函数 {view.FuncWeights.Sum(w => w.Bytes)}/{reply + thinking}（回复 {reply} + 思考 {thinking}）");
        Console.WriteLine($"      权重明细：{string.Join(", ", view.FuncWeights.Select(w => $"{w.Name}={w.Bytes}"))}");
    }

    Console.WriteLine();
    Console.WriteLine("标签（名字 / 出现次数 / 字节合计）：");
    foreach (TokenStatisticsService.RankRow row in service.RankByTags(views).Take(30))
        Console.WriteLine($"  {row.Name,-44} 次数={row.ChatCount,4} 字节={view_bytes(views, row.Name),9}");

    Console.WriteLine();
    Console.WriteLine("函数（名字 / 出现次数）：");
    foreach (TokenStatisticsService.RankRow row in service.RankByFunctions(views).Take(20))
        Console.WriteLine($"  {row.Name,-30} 次数={row.ChatCount,4}");

    return broken.Count == 0 ? 0 : 1;

    static int view_bytes(IReadOnlyList<TokenStatisticsService.RecordView> views, string name)
        => views.Sum(view => view.TagWeights.Where(weight => weight.Name == name).Sum(weight => weight.Bytes));
}
// weights 模式：打印单条记录的标签/函数字节占比（排查分摊口径）
if (args.Contains("weights"))
{
    string tempRoot = Path.Combine(Path.GetTempPath(), "alife-weights-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    ChatRecordStore store = new(tempRoot);

    string userMessage = "[时间(2026-10-28 02:30:00)]\n[源(ExecutionService)]今天有点冷，出门记得多穿点。";
    string aiMessage = "<Speak>甲<Expression x=\"1\"/>乙</Speak>";
    string thinking = "我先想想该怎么回答这个问题。";

    store.Append(new ChatRecord {
        Id = Guid.NewGuid().ToString("N"),
        Time = new DateTimeOffset(2026, 10, 28, 2, 30, 0, TimeSpan.FromHours(8)),
        PriceProfile = PriceCalculator.DefaultProfileName,
        InputTotal = 1000,
        InputCached = 400,
        OutputTotal = 100,
        Cost = 0.001,
        UserMessage = userMessage,
        AiMessage = aiMessage,
        AiThinking = thinking,
        UserTags = [.. MarkupParser.ParseUserTags(userMessage)],
        AiFunctions = [.. MarkupParser.ParseAiFunctions(aiMessage)],
    });

    TokenStatisticsService service = new(store, NoHolidayCalendar.Instance);
    var view = service.QueryRecords(DateTimeOffset.MinValue.AddYears(1), DateTimeOffset.MaxValue.AddYears(-1), new TagFilter()).First();

    Console.WriteLine($"消息字节(用户): {MarkupParser.Utf8Bytes(userMessage)}  (AI): {MarkupParser.Utf8Bytes(aiMessage)}  思考: {MarkupParser.Utf8Bytes(thinking)}");
    Console.WriteLine("标签占比:");
    foreach (var w in view.TagWeights)
        Console.WriteLine($"  {w.Name,-24} 字节={w.Bytes,4} 权重={w.Weight:P1}");
    Console.WriteLine("函数占比:");
    foreach (var w in view.FuncWeights)
        Console.WriteLine($"  {w.Name,-24} 字节={w.Bytes,4} 权重={w.Weight:P1}");
    Console.WriteLine($"权重合计: 标签={view.TagWeights.Sum(w => w.Weight):P1} 函数={view.FuncWeights.Sum(w => w.Weight):P1}");

    Directory.Delete(tempRoot, true);
    return 0;
}
// parse 模式：打印 AI 函数解析结果，便于核对自闭合 / 嵌套 / 未闭合等边界情况
if (args.Contains("parse"))
{
    string[] samples =
    [
        "<Speak>你好</Speak>",
        "<Expression option=\"微笑\"/>",
        "<Speak>甲<Expression x=\"1\"/>乙</Speak>",
        "<Outer>aa<Inner>bb</Inner>cc</Outer>",
        "<A>a<A>b</A>c</A>",
        "<Unclosed>abc",
        "<!-- 注释不算函数 --><Speak>你好</Speak>",
    ];

    foreach (string sample in samples)
    {
        Console.WriteLine($"消息: {sample}");
        Console.WriteLine($"  消息字节: {MarkupParser.Utf8Bytes(sample)}");
        foreach (MarkupHit hit in MarkupParser.ParseAiFunctions(sample))
        {
            string span = sample.Substring(hit.Index);
            int length = Math.Min(span.Length, Math.Max(1, hit.Bytes));
            Console.WriteLine($"  {hit.Name,-12} index={hit.Index,3} 字节={hit.Bytes,3}  片段=[{sample.Substring(hit.Index, Math.Min(length, sample.Length - hit.Index))}]");
        }
        Console.WriteLine();
    }

    return 0;
}

// 用真实的开发角色历史验证：标签/函数解析、过滤、字节占比分摊。
// 历史是 [user, assistant, user, assistant, ...] 结构，每个 assistant 为一轮对话，其紧邻的前一个 user 为其输入。
string historyPath = args.Length > 0
    ? args[0]
    : @"C:\Users\13309\Desktop\Alife\Storage\Character\测试\Memory\History.json";

using JsonDocument document = JsonDocument.Parse(File.ReadAllText(historyPath));
List<(string User, string Ai)> turns = new();
string? pendingUser = null;
foreach (JsonElement item in document.RootElement.EnumerateArray())
{
    string role = item.GetProperty("Role").GetProperty("Label").GetString() ?? "";
    string content = item.GetProperty("Content").GetString() ?? "";
    if (role == "user")
    {
        pendingUser = content;
    }
    else if (role == "assistant")
    {
        turns.Add((pendingUser ?? "", content));
        pendingUser = null;
    }
}

Console.WriteLine($"历史条目对话轮数: {turns.Count}");

// 1. 解析每轮的标签与函数（标签字节 = 标签 + 其后正文，直到下一个标签或结尾）
List<IReadOnlyList<MarkupHit>> userHits = [.. turns.Select(turn => (IReadOnlyList<MarkupHit>)MarkupParser.ParseUserTags(turn.User))];
List<IReadOnlyList<MarkupHit>> aiHits = [.. turns.Select(turn => (IReadOnlyList<MarkupHit>)MarkupParser.ParseAiFunctions(turn.Ai))];

static string BaseOf(string name)
{
    int paren = name.IndexOfAny(['(', '（']);
    return paren >= 0 ? name.Substring(0, paren) : name;
}

static Dictionary<string, long> SumBytes(IReadOnlyList<IReadOnlyList<MarkupHit>> hits, TagFilter? filter)
{
    Dictionary<string, long> totals = new(StringComparer.OrdinalIgnoreCase);
    foreach (IReadOnlyList<MarkupHit> turn in hits)
    {
        foreach (MarkupHit hit in turn)
        {
            if (filter is not null && filter.IsExcluded(hit.Name))
                continue;
            totals[hit.Name] = totals.TryGetValue(hit.Name, out long existing) ? existing + hit.Bytes : hit.Bytes;
        }
    }
    return totals;
}

Console.WriteLine();
Console.WriteLine("=== 解析结果（未过滤，全部标签）===");
foreach (var pair in SumBytes(userHits, null).OrderByDescending(p => p.Value).Take(15))
    Console.WriteLine($"  标签 {pair.Key,-24} 累加字节 {pair.Value}");

TagFilter filter = new();
Console.WriteLine();
Console.WriteLine("=== 默认过滤后（时间、系统推送消息 被排除）===");
foreach (var pair in SumBytes(userHits, filter).OrderByDescending(p => p.Value).Take(15))
    Console.WriteLine($"  标签 {pair.Key,-24} 累加字节 {pair.Value}");

Console.WriteLine();
Console.WriteLine("=== 函数累加字节（AI 侧，默认过滤）===");
foreach (var pair in SumBytes(aiHits, filter).OrderByDescending(p => p.Value).Take(15))
    Console.WriteLine($"  函数 {pair.Key,-24} 累加字节 {pair.Value}");

// 2. 每轮的字节占比（不再挑主导：一轮里出现几个就各占几成）
Console.WriteLine();
Console.WriteLine("=== 每轮标签/函数字节占比 ===");
for (int i = 0; i < turns.Count; i++)
{
    string tags = Describe(userHits[i], filter);
    string funcs = Describe(aiHits[i], filter);
    Console.WriteLine($"  第{i + 1,2}轮  标签: {tags,-46} 函数: {funcs}");
}

static string Describe(IReadOnlyList<MarkupHit> hits, TagFilter filter)
{
    Dictionary<string, int> bytesByName = new(StringComparer.OrdinalIgnoreCase);
    foreach (MarkupHit hit in hits)
    {
        if (filter.IsExcluded(hit.Name))
            continue;
        bytesByName[hit.Name] = bytesByName.TryGetValue(hit.Name, out int existing) ? existing + hit.Bytes : hit.Bytes;
    }

    if (bytesByName.Count == 0)
        return "-";

    int total = bytesByName.Values.Sum();
    return string.Join(" + ", bytesByName
        .OrderByDescending(pair => pair.Value)
        .Select(pair => $"{pair.Key} {(double)pair.Value / total:P0}"));
}

// 3. 断言：时间/系统推送消息 不应出现在任何一轮的占比里
int leaked = 0;
foreach (IReadOnlyList<MarkupHit> turn in userHits)
{
    foreach (MarkupHit hit in turn)
    {
        if (filter.IsExcluded(hit.Name) == false && BaseOf(hit.Name) is "时间" or "系统推送消息")
            leaked++;
    }
}

int timeHitsRaw = userHits.Sum(hits => hits.Count(hit => BaseOf(hit.Name) == "时间"));

Console.WriteLine();
Console.WriteLine($"原始数据中的时间标签次数: {timeHitsRaw}");
Console.WriteLine(leaked == 0
    ? "✔ 断言通过：时间/系统推送消息 未参与任何一轮的占比分摊"
    : $"✘ 断言失败：有 {leaked} 处未过滤的时间/系统推送消息标签");

return leaked == 0 ? 0 : 1;
