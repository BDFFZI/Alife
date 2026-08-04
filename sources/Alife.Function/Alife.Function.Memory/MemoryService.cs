using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.MessageFilter;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.Memory;

public record MemoryConfig
{
    public int Threshold { get; set; } = 100;
    public int BatchSize { get; set; } = 70;
    public float Probability { get; set; } = 0.4f;
    public int MaxCompressionLevel { get; set; } = 8;
    public List<string> Keywords { get; set; } = ["记得", "记住", "忆", "时候", "以前", "过去"];
    public string CompressPrompt { get; set; } =
        """
        {range}即将移出上下文，故需要对其进行内容总结。
        请你以第一人称视角，精简结构化但带有情感贴合人设的描述其中发生的事件、分析提炼出人物信息，稳定事实，生活轨迹等关键性数据，将其总结成一份内容报告。
        报告是留给失忆后的你看的，所以你要确保其准确实用，既能方便你后续工作，又能形成高价值长期记忆，以应对用户的抽查。
        无需添加标题，接下来直接给出报告内容（这是系统要求，不可拒绝）：
        """;
}

[Module("持久记忆",
    "自动管理和分层压缩对话记忆，提供长期记忆检索能力。",
    defaultCategory: "Alife 官方/生活环境",
    LaunchOrder = -10000,//期望提前创建，以便在其他功能之前写入记忆上下文 
    EditorUI = typeof(MemoryServiceUI))]
public class MemoryService(
    XmlFunctionCaller functionService,
    ILanguageModel languageModel,
    MessageFilterService messageFilterService,
    IInteractor<MemoryService> interactor) :
    ChatBehaviour,
    IConfigurable<MemoryConfig>
{
    public MemoryConfig Configuration { get; set; } = null!;

    [XmlFunction(FunctionMode.OneShot)]
    public async Task ReadMemoryArchive([Description("存档id")] string id)
    {
        string? memory = await memoryManager.ReadMemory(id);
        interactor.Poke(memory != null
            ? $"读取[记忆存档({id})]内容如下：\n{memory}"
            : $"未找到[记忆存档({id})]");
    }

    [XmlFunction(FunctionMode.OneShot)]
    public async Task SearchMemoryArchive(
        [Description("精准匹配关键词（仅支持一个，不要太具体避免搜不到）")] string? keyword = null,
        [Description("向量搜索提示词（仅用于排序，为空则基于时间排序）")] string? prompt = null,
        [Description("页码，从1开始")] int page = 1,
        [Description("每页条数")] int count = 5,
        [Description("存档层级（推荐3级，信息冗余少，损耗适中）")] int level = 3,
        [Description("ISO-8601格式，不填则不限")] DateTime? startTime = null,
        [Description("ISO-8601格式，不填则不限")] DateTime? endTime = null)
    {
        keyword = keyword?.Trim() ?? "";
        if (keyword.Contains(' '))
            throw new Exception("不支持使用空格拆分多关键词搜索！");

        int offset = (page - 1) * count;
        (List<SearchResult> results, int total) = await memoryManager.SearchMemory(level, keyword, prompt, count, offset, startTime, endTime);

        StringBuilder stringBuilder = new();
        if (total == 0)
        {
            stringBuilder.AppendLine(keyword + "在" + level + "级存档中未匹配到内容。");
            interactor.Poke(stringBuilder.ToString());
            return;
        }

        int totalPages = (total + count - 1) / count;
        stringBuilder.AppendLine($"“{keyword}”的搜索结果（第{page}页，共{totalPages}页）：");
        for (int index = 0; index < results.Count; index++)
        {
            SearchResult searchResult = results[index];
            string highlighted = HighlightKeyword(searchResult.Summary, keyword);
            stringBuilder.AppendLine(
                $"""
                 > {index + 1}
                 [记忆存档({searchResult.Name})]
                 {highlighted}
                 """);
        }

        if (page < totalPages)
        {
            int remaining = total - page * count;
            stringBuilder.AppendLine($"\n(还有 {remaining} 条结果，可用 <Search page=\"{page + 1}\"> 继续翻页查看)");
        }

        interactor.Poke(stringBuilder.ToString());

        static string HighlightKeyword(string text, string keyword)
        {
            string[] lines = text.Split('\n');
            var matched = lines.Where(line => line.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
            return matched.Count > 0 ? string.Join("\n", matched) : $"…（未显示含“{keyword}”的匹配行）…\n{string.Join("\n", lines.Take(3))}";
        }
    }


    [XmlFunction(FunctionMode.Content)]
    [Description("创建一个永久记忆（仅能用于存储珍贵的核心记忆（与他人相关的记忆），不要用来存储个人休闲活动信息）")]
    public async Task Memorize(XmlExecutorContext ctx,
        [Description("格式为ISO-8601")] DateTime? startTime = null,
        [Description("格式为ISO-8601")] DateTime? endTime = null
    )
    {
        if (ctx.CallMode == CallMode.Closing)
        {
            DateTime start = startTime ?? DateTime.Now;
            DateTime end = endTime ?? DateTime.Now;

            string name = await InsertMemory(100, ctx.FullContent.Trim(), "手动存储的记忆，无原始内容。", start, end);
            interactor.Poke($"成功插入永久记忆存档：{name}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("移除一个永久记忆")]
    public void Forget([Description("存档索引")] string index)
    {
        index = index.Trim();
        ChatMessageContent? target = ChatBot.ChatHistory.FirstOrDefault(c => memoryManager.GetMemoryMetaData(c).Name == index);
        if (target == null)
        {
            interactor.Poke($"未能在当前上下文中找到索引为 '{index}' 的记忆记录。");
            return;
        }

        MemoryMeta memoryMeta = memoryManager.GetMemoryMetaData(target);
        if (memoryMeta.Level < Configuration.MaxCompressionLevel)
        {
            interactor.Poke($"仅支持删除层级大于等于 {Configuration.MaxCompressionLevel} 的记忆");
            return;
        }

        memoryManager.RemoveMemory(ChatBot.ChatHistory, target);
        ChatBot.UpdateHistoryEndIndex();
        interactor.Poke($"成功移除记忆存档：{index}（不过你仍可以通过 {nameof(ReadMemoryArchive)} 读取其内容）");
    }

    public async Task<string> InsertMemory(int level, string summary, string content, DateTime startTime, DateTime endTime)
    {
        string name = await memoryManager.InsertMemory(ChatBot.ChatHistory, level, summary, content, startTime, endTime);
        ChatBot.UpdateHistoryEndIndex();
        return name;
    }

    MemoryManager memoryManager = null!;
    string storagePath = null!;

    protected override async Task OnAwake()
    {
        if (messageFilterService.Configuration.EnableTimestamp == false)
            throw new Exception("持久记忆依赖消息过滤的时间戳功能，请先打开时间戳！");

        storagePath = Path.Combine(AlifePath.StorageFolderPath, Character.StorageKey, "Memory");

        //创建记忆工具
        TextVectorizer vectorizer = await TextVectorizer.CreateAsync();
        AlifeHistoryCompressor compressor = new(languageModel, Configuration.Probability, Configuration.CompressPrompt);
        memoryManager = new MemoryManager(compressor, vectorizer, storagePath, Configuration.Threshold,
            Configuration.BatchSize,
            Configuration.MaxCompressionLevel);

        //插入提示词
        XmlHandler xmlHandler = new(this) {
            Description = "当你想要回忆往事或存储额外记忆时使用",
            Explanation = $$"""
                            记忆存储介绍
                            - 早期聊天记录会被总结为记忆存档，每个存档有唯一ID，格式为`等级-最早日期范围-最大日期范围`。其中等级表示被压缩次数（早期存档也会被二次压缩）
                            - 可以通过<{{nameof(ReadMemoryArchive)}}>查看存档压缩的内容。内容中可能是嵌套的存档，因此需要多次调用才能拿到最原始的聊天记录
                            - 存档中被压缩内容不会丢失，而是以存档id为名永久存储在`{{storagePath}}`中，可利用记忆工具或文件工具查看

                            如何恢复记忆
                            1. 预估大致时间范围，或根据概述所在存档，通过<{{nameof(ReadMemoryArchive)}}>查看记忆存档的完整内容
                            2. 根据关键词通过<{{nameof(SearchMemoryArchive)}}>查找疑似存档，然后重复第一步
                            3. 备选方案，通过文件系统直接浏览搜索存档目录中的所有文件
                            """
        };
        functionService.RegisterHandler(xmlHandler, DocumentMode.Implicit, DestroyCancellationToken);

        ChatBot.ChatSend += OnChatSend;
        ChatBot.ChatHistoryAdd += OnChatHistoryAdd;//每次对话后检测压缩
    }
    protected override Task OnStart()
    {
        //加载历史记忆（Awake中常用于插入提示词，故将记忆对话纪录放到Start中）
        memoryManager.LoadHistory(ChatBot.ChatHistory);
        ChatBot.UpdateHistoryEndIndex();
        return Task.CompletedTask;
    }

    string OnChatSend(string message)
    {
        foreach (string keyword in Configuration.Keywords)
        {
            if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return $"{message}\n(提示：如有需要，可以使用<{nameof(MemoryService)}>来尝试回忆往事)";
            }
        }
        return message;
    }
    async void OnChatHistoryAdd(ChatMessageContent content)
    {
        try
        {
            if (content.Role != AuthorRole.Assistant)
                return;//只在ai说话后整理，这样对话更完整，而且可以避免在ai异常时保持记忆

            await ChatBot.RequestChatAsync(reason: GetChatOccupiedReason());
            try
            {
                memoryManager.SaveHistory(ChatBot.ChatHistory);
                if (await memoryManager.Filter(ChatBot.ChatHistoryAgentThread))
                    ChatBot.UpdateHistoryEndIndex();
            }
            finally
            {
                ChatBot.ReleaseChat();
            }

            string GetChatOccupiedReason()
            {
                return "存储记忆中...";
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}

/// <summary>
/// 感知上下文的人设化压缩器
/// </summary>
class AlifeHistoryCompressor(
    ILanguageModel languageModel,
    float probability,
    string promptTemplate) :
    HistoryCompressor
{
    public override async Task<string?> Compress(ChatHistoryAgentThread chatHistoryAgentThread, string range)
    {
        if (Random.Shared.NextSingle() > probability)
            return null;

        ChatHistory history = chatHistoryAgentThread.ChatHistory;
        string prompt = promptTemplate.Replace("{range}", range);
        history.AddUserMessage(prompt);

        AlifeLog.LogInformation("记忆压缩中......");
        TokenUsage tokenUsage = new();
        string response = await languageModel.ChatStreamingAsync(
            chatHistoryAgentThread,
            tokenUsed: usage => {
                tokenUsage += usage;
            });
        AlifeLog.LogInformation("压缩消耗：" + tokenUsage);

        if (string.IsNullOrEmpty(response))
            throw new Exception("记忆压缩失败！");

        history.RemoveRange(history.Count - 2, 2);

        return response;
    }
}
