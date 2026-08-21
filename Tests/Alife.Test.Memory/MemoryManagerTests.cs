using System.Text;
using Alife.Function.Memory;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace Alife.Test.Memory;

/// <summary>
/// 持久记忆插件修复回归测试。
/// 主要验证 MemoryManager.Filter 对“逆序区域”的自动合并升级逻辑：
/// 正常的历史顺序应是「级别从高到低」单调排列，当存档中出现「低级别之后又冒出高级别」的乱序
/// （逆序）记录时，旧代码会静默跳过、导致高级别存档永远滞留/阻塞后续处理；本测试确认新代码
/// 能正确检测并自动合并升级，且对正常单调结构不误触发。
/// </summary>
/// <remarks>
/// 说明：由于压缩会调用 SaveMemory → MemoryStorage.SaveAsync（依赖 DuckDB 与向量化模型），
/// 本测试使用真实的 TextVectorizer（bge 模型）。运行测试前需确保模型与 python 环境可用
/// （与 Alife 正式运行时一致）。
/// </remarks>
[TestFixture]
public class MemoryManagerTests
{
    TextVectorizer vectorizer = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        vectorizer = await TextVectorizer.CreateAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await vectorizer.DisposeAsync();
    }

    [SetUp]
    public void Setup() { }

    [TearDown]
    public void Cleanup() { }

    /// <summary>
    /// 场景A：逆序时前面已积累多条低级别消息，应将受影响区域整体合并升级。
    /// </summary>
    [Test]
    public async Task Filter_InverseRegion_WithMultiplePriorLowLevels_Consolidates()
    {
        await RunFilterScenario(hb => {
            for (int i = 0; i < 5; i++) hb.AddRaw($"原始消息{i}");
            hb.AddArchive("高级别存档(乱序)", 2);   // 逆序：高级别出现在低级别之后
            hb.AddRaw("逆序之后的普通消息A");
            hb.AddRaw("逆序之后的普通消息B");
        }, result => {
            // 逆序区域应被合并升级为一个存档
            Assert.That(result.ChatHistory, Has.Some.Matches<ChatMessageContent>(
                m => m.Content != null && m.Content.Contains("[记忆存档(1-")));
            // 逆序的高级别(level2)记录不应再残留
            Assert.That(result.ChatHistory, Has.None.Matches<ChatMessageContent>(
                m => m.Content != null && m.Content.Contains("高级别存档(乱序)")));
        });
    }

    /// <summary>
    /// 场景B：逆序时前面仅 1 条低级别消息，也应被合并升级，而非留下逆序残留。
    /// </summary>
    [Test]
    public async Task Filter_InverseRegion_SinglePriorLowLevel_Consolidates()
    {
        await RunFilterScenario(hb => {
            hb.AddRaw("单独的低级别消息");
            hb.AddArchive("高级别存档(乱序)", 2);   // 逆序
            hb.AddRaw("之后的消息");
        }, result => {
            Assert.That(result.ChatHistory, Has.Some.Matches<ChatMessageContent>(
                m => m.Content != null && m.Content.Contains("[记忆存档(1-")));
            Assert.That(result.ChatHistory, Has.None.Matches<ChatMessageContent>(
                m => m.Content != null && m.Content.Contains("高级别存档(乱序)")));
        });
    }

    /// <summary>
    /// 场景C：正常的单调递减结构（高在前、低在后），不应误触发压缩。
    /// </summary>
    [Test]
    public async Task Filter_NormalMonotonic_DoesNotMisTrigger()
    {
        await RunFilterScenario(hb => {
            hb.AddArchive("一级存档", 1);
            hb.AddRaw("普通消息1");
            hb.AddRaw("普通消息2");
        }, result => {
            // 阈值未达到，不应新增任何存档
            Assert.That(result.ChatHistory, Has.None.Matches<ChatMessageContent>(
                m => m.Content != null && m.Content.Contains("[记忆存档(")));
        });
    }

    /// <summary>
    /// 场景D：完全逆序结构（高级别反复穿插在低级别之间）不应崩溃，应被收敛。
    /// </summary>
    [Test]
    public async Task Filter_HeavilyReversed_DoesNotCrash()
    {
        await RunFilterScenario(hb => {
            hb.AddRaw("消息0");
            hb.AddArchive("存档-3级(逆序)", 3);
            hb.AddRaw("消息1");
            hb.AddArchive("存档-2级(逆序)", 2);
            hb.AddRaw("消息2");
            hb.AddRaw("消息3");
        }, _ => {
            // 只要求不崩溃；结构正确性由上述场景保证
        });
    }

    async Task RunFilterScenario(Action<HistoryBuilder> setup, Action<FilterResult> assert)
    {
        string storagePath = Path.Combine(Path.GetTempPath(), $"alife_test_memory_{Guid.NewGuid():N}");
        Directory.CreateDirectory(storagePath);
        try
        {
            DateTime t = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Local);
            HistoryBuilder hb = new(t);
            setup(hb);

            await File.WriteAllTextAsync(Path.Combine(storagePath, "History.json"), hb.BuildJson());

            MemoryManager manager = new(
                new FakeCompressor(),
                vectorizer,
                storagePath,
                compressionThreshold: 8,
                compressionCount: 6,
                maxCompressionLevel: 8);

            ChatHistoryAgentThread thread = new();
            manager.LoadHistory(thread.ChatHistory);

            // 核心：确认滤除过程不抛出异常（逆序问题可能导致越界/死循环/静默跳过）
            await manager.Filter(thread);

            assert(new FilterResult(thread.ChatHistory));
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
