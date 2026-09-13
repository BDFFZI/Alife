using System.Text;
using Alife.Function.Memory;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace Alife.Test.Memory;

/// <summary>
/// 持久记忆插件回归测试。
/// 主要验证 MemoryManager 对“逆序区域”的自动合并升级逻辑：
/// 正常的历史顺序应是「级别从高到低」单调排列，当存档中出现「低级别之后又冒出高级别」的乱序
/// （逆序）记录时，旧代码会静默跳过、导致高级别存档永远滞留/阻塞后续处理；本测试确认新代码
/// （快照分析 Analyze + 原子替换 Apply）能正确检测并自动合并升级，且对正常单调结构不误触发。
/// </summary>
/// <remarks>
/// 说明：测试仅针对逻辑层验证 Analyze 生成压缩计划、以及 Apply 对「无归纳摘要」分支的原子替换
/// （单条逆序直接升级）。不走存储落库分支，故不依赖真实 TextVectorizer/DuckDB/Python 环境，
/// 可在 Alife 客户端运行期间正常执行。
/// </remarks>
[TestFixture]
public class MemoryManagerTests
{
    /// <summary>
    /// 场景A：逆序时前面已积累多条低级别消息，应将受影响区域整体合并升级。
    /// </summary>
    [Test]
    public async Task Analyze_InverseRegion_WithMultiplePriorLowLevels_Consolidates()
    {
        await RunAnalyze(hb => {
            for (int i = 0; i < 5; i++) hb.AddRaw($"原始消息{i}");
            hb.AddArchive("高级别存档(乱序)", 2);   // 逆序：高级别出现在低级别之后
            hb.AddRaw("逆序之后的普通消息A");
            hb.AddRaw("逆序之后的普通消息B");
        }, plan => {
            // 5 条低级别消息应整体合并升级为 level2 存档
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.InsertIndex, Is.EqualTo(0));
            Assert.That(plan.RemoveCount, Is.EqualTo(5));
            Assert.That(plan.NewLevel, Is.EqualTo(2));
            Assert.That(plan.Summary, Is.Not.Null);
            // 锚点应指向快照上的头尾元素（供 Apply 做引用校验）
            Assert.That(plan.HeadAnchor, Is.Not.Null);
            Assert.That(plan.TailAnchor, Is.Not.Null);
            Assert.That(ReferenceEquals(plan.HeadAnchor, plan.TailAnchor), Is.False);
        });
    }

    /// <summary>
    /// 场景B：逆序时前面仅 1 条低级别消息，应直接升级为当前级别，而非留下逆序残留。
    /// </summary>
    [Test]
    public async Task AnalyzeApply_InverseRegion_SinglePriorLowLevel_Consolidates()
    {
        await RunScenario(hb => {
            hb.AddRaw("单独的低级别消息");
            hb.AddArchive("高级别存档(乱序)", 2);   // 逆序
            hb.AddRaw("之后的消息");
        }, (manager, result) => {
            // 单条逆序前消息被直接升级到 level2，不产生新存档
            Assert.That(result.ChatHistory, Has.None.Matches<ChatMessageContent>(
                m => m.Content != null && m.Content.Contains("[记忆存档(")));
            Assert.That(manager.GetMemoryMetaData(result.ChatHistory[0]).Level, Is.EqualTo(2));
            // 原消息内容不应丢失
            Assert.That(result.ChatHistory, Has.Some.Matches<ChatMessageContent>(
                m => m.Content != null && m.Content.Contains("单独的低级别消息")));
        });
    }

    /// <summary>
    /// 场景C：正常的单调递减结构（高在前、低在后），不应误触发压缩。
    /// </summary>
    [Test]
    public async Task Analyze_NormalMonotonic_DoesNotMisTrigger()
    {
        await RunAnalyze(hb => {
            hb.AddArchive("一级存档", 1);
            hb.AddRaw("普通消息1");
            hb.AddRaw("普通消息2");
        }, plan => {
            // 阈值未达到，不应生成任何压缩计划
            Assert.That(plan, Is.Null);
        });
    }

    /// <summary>
    /// 场景D：完全逆序结构（高级别反复穿插在低级别之间）不应崩溃，应被收敛。
    /// </summary>
    [Test]
    public async Task AnalyzeApply_HeavilyReversed_DoesNotCrash()
    {
        await RunScenario(hb => {
            hb.AddRaw("消息0");
            hb.AddArchive("存档-3级(逆序)", 3);
            hb.AddRaw("消息1");
            hb.AddArchive("存档-2级(逆序)", 2);
            hb.AddRaw("消息2");
            hb.AddRaw("消息3");
        }, (manager, result) => {
            // 首个逆序点应把前面的单条消息升级到 level3，且不崩溃
            Assert.That(manager.GetMemoryMetaData(result.ChatHistory[0]).Level, Is.EqualTo(3));
        });
    }

    /// <summary>仅做分析并断言计划，不执行落库分支。</summary>
    Task RunAnalyze(Action<HistoryBuilder> setup, Action<MemoryCompressionPlan?> assert)
        => RunCore(setup, async (manager, chatHistory, snapshot) => {
            MemoryCompressionPlan? plan = await manager.Analyze(snapshot);
            assert(plan);
        });

    /// <summary>分析并应用（单条升级等无落库场景），然后断言最终上下文。</summary>
    Task RunScenario(Action<HistoryBuilder> setup, Action<MemoryManager, FilterResult> assert)
        => RunCore(setup, async (manager, chatHistory, snapshot) => {
            // 阶段一：在快照副本上分析（与正式流程一致，不触碰真实上下文）
            MemoryCompressionPlan? plan = await manager.Analyze(snapshot);

            // 阶段二：依据计划对真实上下文原子替换（含锚点校验）
            if (plan != null)
                await manager.Apply(chatHistory, plan);

            assert(manager, new FilterResult(chatHistory));
        });

    async Task RunCore(Action<HistoryBuilder> setup, Func<MemoryManager, ChatHistory, ChatHistory, Task> run)
    {
        string storagePath = Path.Combine(Path.GetTempPath(), $"alife_test_memory_{Guid.NewGuid():N}");
        Directory.CreateDirectory(storagePath);
        try
        {
            DateTime t = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Local);
            HistoryBuilder hb = new(t);
            setup(hb);

            await File.WriteAllTextAsync(Path.Combine(storagePath, "History.json"), hb.BuildJson());

            // 传 null 向量化器：本测试不触碰存储落库分支（SaveMemory/SearchAsync），故无需真实模型。
            MemoryManager manager = new(
                new FakeCompressor(),
                null!,
                storagePath,
                compressionThreshold: 8,
                compressionCount: 6,
                maxCompressionLevel: 8);

            ChatHistoryAgentThread thread = new();
            manager.LoadHistory(thread.ChatHistory);

            // 快照副本（浅拷贝共享同一批 ChatMessageContent 引用，供 Apply 锚点校验）
            ChatHistory snapshot = new();
            foreach (ChatMessageContent message in thread.ChatHistory)
                snapshot.Add(message);

            await run(manager, thread.ChatHistory, snapshot);
        }
        finally
        {
            try { Directory.Delete(storagePath, true); } catch { }
        }
    }

    sealed class FilterResult(ChatHistory chatHistory)
    {
        public ChatHistory ChatHistory { get; } = chatHistory;
    }

    class HistoryBuilder
    {
        readonly DateTime t;
        readonly List<Entry> items = new();

        public HistoryBuilder(DateTime t) => this.t = t;

        public void AddRaw(string content, int level = 0)
            => items.Add(new Entry("user", content, level, t, t));
        public void AddArchive(string content, int level)
            => items.Add(new Entry("assistant", content, level, t, t));

        public string BuildJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];
                string name = $"{e.Level}-{t:yyyyMMddHHmmss}-{t:yyyyMMddHHmmss}";
                sb.Append("  {\n");
                sb.Append($"    \"Role\": {{\"Label\": \"{e.Role}\"}},\n");
                sb.Append($"    \"Content\": {Newtonsoft.Json.JsonConvert.ToString(e.Content)},\n");
                sb.Append("    \"MemoryMeta\": {\n");
                sb.Append($"      \"Level\": {e.Level},\n");
                sb.Append($"      \"StartTime\": \"{t:O}\",\n");
                sb.Append($"      \"EndTime\": \"{t:O}\",\n");
                sb.Append($"      \"Name\": \"{name}\"\n");
                sb.Append("    }\n");
                sb.Append("  }");
                sb.Append(i < items.Count - 1 ? "," : "");
                sb.AppendLine();
            }
            sb.AppendLine("]");
            return sb.ToString();
        }

        sealed record Entry(string Role, string Content, int Level, DateTime StartTime, DateTime EndTime);
    }

    /// <summary>确定性假压缩器：避免依赖真实 LLM，返回固定摘要。</summary>
    sealed class FakeCompressor : HistoryCompressor
    {
        public override Task<string?> Compress(ChatHistoryAgentThread chatHistoryAgentThread, string range)
        {
            return Task.FromResult<string?>($"（压缩摘要：{range} 的合并结果）");
        }
    }
}