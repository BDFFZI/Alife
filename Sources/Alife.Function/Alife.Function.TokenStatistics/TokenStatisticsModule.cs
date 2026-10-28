using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Foundation;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// Token 统计模块（角色级 ChatBehaviour）。
/// 随角色激活由 ConstructContainer 自动构造。OnStart 时订阅本角色模型的 TokenUsed 事件，
/// 每次模型调用写入一条对话记录（按月 JSONL，90 天保留）；统计页经 <see cref="Query"/> 读取。
/// OnDestroy 精确退订，保证热重载与角色重激活不重复订阅。
/// </summary>
[Module(
    "Token 统计",
    "记录本角色每轮对话的 token 开销与花费，按时间段、标签与函数统计，可在系统管理中心查看。",
    defaultCategory: "Alife 官方/系统工具"
)]
public class TokenStatisticsModule : ChatBehaviour
{
    // 已订阅的模型实例 → 其事件委托，用于 OnDestroy 精确退订
    readonly Dictionary<ITokenUser, Action<TokenUsageContext>> subscribedHandlers = new();

    /// <summary>本角色的记录存储，OnStart 后非空。</summary>
    public ChatRecordStore? RecordStore { get; private set; }

    /// <summary>统计查询服务，OnStart 后非空。统计页通过它取数据。</summary>
    public TokenStatisticsService? Query { get; private set; }

    protected override async Task OnStart()
    {
        // 对话记录放在角色目录下：Storage\Character\<角色>\TokenStatistics\records
        string recordsFolder = Path.Combine(AlifePath.StorageFolderPath, Character.StorageKey, "TokenStatistics", "records");
        RecordStore = new ChatRecordStore(recordsFolder);

        // 节假日表决定写入记录时的峰谷单价；缺失则按星期判断（不影响记录本身）
        HolidayCalendar calendar = TokenStatisticsLocator.CreateHolidayCalendar(Character.StorageKey);
        int year = DateTime.Now.Year;
        await calendar.EnsureYearAsync(year);
        await calendar.EnsureYearAsync(year - 1);

        Query = new TokenStatisticsService(RecordStore, calendar);

        // 订阅本角色当前已存在的所有 ITokenUser（模型通常在此时已构造）
        foreach (object instance in ChatActivity.Container.Instances)
        {
            if (instance is ITokenUser tokenUser)
                Subscribe(tokenUser);
        }

        // 模型若晚于本模块构造（懒构造/热重载重建），挂容器事件补订阅
        ChatActivity.Container.InstanceCreated += OnInstanceCreated;

    }

    Task OnInstanceCreated(object instance)
    {
        if (instance is ITokenUser tokenUser)
            Subscribe(tokenUser);
        return Task.CompletedTask;
    }

    void Subscribe(ITokenUser tokenUser)
    {
        if (subscribedHandlers.ContainsKey(tokenUser))
            return;
        Action<TokenUsageContext> handler = WriteRecord;
        tokenUser.TokenUsed += handler;
        subscribedHandlers[tokenUser] = handler;
    }

    /// <summary>
    /// 把一次模型调用写成一条记录：解析标签/函数字节数，按默认价格配置计费。
    /// 节假日尚未接入，此时高峰判定退化为"周一至周五"，与设计的兜底一致。
    /// </summary>
    void WriteRecord(TokenUsageContext context)
    {
        if (RecordStore == null)
            return;

        DateTimeOffset now = DateTimeOffset.Now;
        ChatRecord record = new() {
            Id = Guid.NewGuid().ToString("N"),
            Time = now,
            PriceProfile = PriceCalculator.DefaultProfileName,
            InputTotal = context.TokenUsage.Input,
            InputCached = context.TokenUsage.Cached,
            OutputTotal = context.TokenUsage.Output,
            UserMessage = context.UserMessage ?? "",
            AiMessage = context.AIMessage ?? "",
            AiThinking = context.AIThinking ?? "",
            UserTags = MarkupParser.ParseUserTags(context.UserMessage ?? "").ToList(),
            AiFunctions = MarkupParser.ParseAiFunctions(context.AIMessage ?? "").ToList(),
        };

        record.Cost = PriceCalculator.CostAt(
            PriceCalculator.CreateDefaultProfile(),
            now.DateTime,
            NoHolidayCalendar.Instance,
            context.TokenUsage.Input,
            context.TokenUsage.Cached,
            context.TokenUsage.Output);

        RecordStore.Append(record);
    }

    protected override Task OnDestroy()
    {
        ChatActivity.Container.InstanceCreated -= OnInstanceCreated;
        foreach ((ITokenUser tokenUser, Action<TokenUsageContext> handler) in subscribedHandlers)
            tokenUser.TokenUsed -= handler;
        subscribedHandlers.Clear();
        return Task.CompletedTask;
    }
}
