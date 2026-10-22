using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.MessageFilter;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.Memory;

[Module("持久记忆",
    "自动管理和分层压缩对话记忆，提供长期记忆检索能力。",
    defaultCategory: "Alife 官方/生活环境",
    LaunchOrder = -10000, //期望提前创建，以便在其他功能之前写入记忆上下文 
    EditorUI = typeof(MemoryServiceUI))]
public class MemoryService(
    ModuleSystem moduleSystem,
    Interactor<MemoryService> interactor,
    ILogger<MemoryService> logger,
    XmlFunctionCaller functionService,
    ILanguageModel languageModel,
    IMessageFilterService messageFilterService) :
    ChatBehaviour,
    IConfigurable<MemoryConfig>
{
    public MemoryConfig Configuration { get; set; } = null!;

    public async Task Memorize(int level, string name, string synopsis, string content)
    {
        MemoryIndex memoryIndex = new(level, name);
        await ChatBot.EditChatHistoryAsync(
            async thread => {
                await memoryManager.WriteMemory(thread.ChatHistory, memoryIndex, synopsis, content);
            }, "编辑记忆");
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("创建或修改记忆存档")]
    public async void Memorize(XmlExecutorContext ctx,
        [Description("等级")] int level,
        [Description("记忆名")] string name,
        [Description("简述（始终可见，方便你未来查阅）")] string synopsis,
        [Description($"具体内容（存到外存。需要用{nameof(Recall)}查看）")] [XmlContent] string content
    )
    {
        try
        {
            if (ctx.CallMode != CallMode.Closing)
                return;

            MemoryIndex memoryIndex = new(level, name);
            string fullContent = ctx.FullContent;
            await Memorize(level, name, synopsis, fullContent);
            interactor.Poke($"已编辑{MemoryManager.GetMemoryTag(memoryIndex)}");
        }
        catch (Exception e)
        {
            interactor.Poke("编辑失败：" + e.Message);
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除记忆存档")]
    public void Forget([Description("存档索引")] string index)
    {
        MemoryIndex memoryIndex = MemoryIndex.Parse(index);
        _ = ChatBot.EditChatHistoryAsync(
            thread => {
                ChatMessageContent? chatMessageContent = memoryManager.FindMemory(thread.ChatHistory, memoryIndex);
                if (chatMessageContent == null)
                {
                    interactor.Poke($"{MemoryManager.GetMemoryTag(memoryIndex)}不存在");
                    return Task.CompletedTask;
                }

                thread.ChatHistory.Remove(chatMessageContent);
                interactor.Poke($"{MemoryManager.GetMemoryTag(memoryIndex)}已删除");
                return Task.CompletedTask;
            }, "编辑记忆");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看记忆存档具体内容")]
    public async Task Recall([Description("存档索引")] string index)
    {
        MemoryIndex memoryIndex = MemoryIndex.Parse(index);
        string? memory = await memoryManager.ReadMemory(memoryIndex);
        interactor.Poke(memory != null
            ? $"{MemoryManager.GetMemoryTag(memoryIndex)}内容如下：\n{memory}"
            : $"未找到{MemoryManager.GetMemoryTag(memoryIndex)}");
    }

    MemoryManager memoryManager = null!;
    ILanguageModel compressModel = null!;
    string storagePath = null!;
    int compressing; //压缩进行中标志（含LLM归纳阶段），防重入

    protected override Task OnAwake()
    {
        if (messageFilterService.IsEnabledTimestamp == false)
            throw new Exception("持久记忆依赖消息过滤的时间戳功能，请先打开时间戳！");

        storagePath = Path.Combine(AlifePath.StorageFolderPath, Character.StorageKey, "Memory");
        Directory.CreateDirectory(storagePath);

        //插入提示词
        XmlHandler xmlHandler = new(this) {
            Description = "上下文编辑与回忆工具",
            Explanation = $"""
                           记忆存档：
                           每当对话超一定次数，部分对话内容就会被压缩成存档。每个存档有一个唯一标识，格式为`等级-名称`。
                           系统的存档中，名称通常由内容涉及的日期构成`开始时间-截至时间`，但名称本身完全支持自定义，例如`关于xx的记忆`。
                           当同级存档达到一定数量，也会进行二次压缩归档，同时等级加一，直到达到最高等级。

                           创建专用记忆：
                           你可以使用本服务提供编辑工具，修改你的上下文（慎用，极耗token）。
                           比如创建一些不会被压缩的高级存档（等级>={Configuration.MaxCompressionLevel}时才会常驻上下文）来存储专用内容，如存储人物画像、规则约束、数据记录等内容。
                           起一个易懂的名称并维护它，同时尽可能保持synopsis不变，仅修改content来保持缓存命中。
                           但也注意别忘了先用{nameof(Recall)}查看原内容再写，防止错误覆盖。

                           检索记忆：
                           所有记忆存档中的原始具体内容都被存放在了`{storagePath}`中，他们是被归档在上下文之外的旧记忆内容，你可以用文件操作工具翻阅他们。
                           你可以没事翻一翻，或许会发现被遗忘的珍贵回忆。
                           """
        };
        functionService.RegisterHandler(xmlHandler, DocumentMode.Explicit, DestroyCancellationToken);

        return Task.CompletedTask;
    }
    protected override async Task OnStart()
    {
        //初始化记忆压缩模型
        compressModel = await ResolveCompressModel();
        AlifeHistoryCompressor compressor = new(compressModel, Configuration.Probability, Configuration.CompressPrompt);
        memoryManager = new MemoryManager(compressor, storagePath, Configuration.Threshold,
            Configuration.BatchSize,
            Configuration.MaxCompressionLevel);

        //加载历史记忆（Awake中常用于插入提示词，故将记忆对话纪录放到Start中）
        await ChatBot.EditChatHistoryAsync(thread => {
            memoryManager.LoadHistory(thread.ChatHistory);
            return Task.CompletedTask;
        }, "装载记忆");

        ChatBot.ChatSend += OnChatSend;
        ChatBot.ChatHistoryAdd += OnChatHistoryAdd; //每次对话后检测压缩
        ChatBot.TokenUsed += OnTokenUsed; //监控上下文token消耗
    }

    /// <summary>
    /// 解析用于压缩的语言模型：若配置了类名且类型存在，则从容器中获取，否则回退到默认语言模型。
    /// </summary>
    async Task<ILanguageModel> ResolveCompressModel()
    {
        string className = Configuration.CompressModelClassName;
        if (string.IsNullOrWhiteSpace(className))
            return languageModel;

        Type? modelType = moduleSystem.GetAllModules()
            .FirstOrDefault(type => type.Name == className || type.FullName == className);
        if (modelType == null)
        {
            logger.LogWarning("未找到压缩语言模型类型“{ClassName}”，将使用默认语言模型。", className);
            return languageModel;
        }

        try
        {
            object instance = await ChatActivity.Container.RequireInstance(modelType, true);
            if (instance is ILanguageModel model)
                return model;

            logger.LogWarning("类型“{ClassName}”未实现 {Interface}，将使用默认语言模型。",
                className, nameof(ILanguageModel));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "获取压缩语言模型“{ClassName}”失败，将使用默认语言模型。", className);
        }

        return languageModel;
    }
    protected override async Task OnDestroy()
    {
        ChatBot.ChatSend -= OnChatSend;
        ChatBot.ChatHistoryAdd -= OnChatHistoryAdd;
        ChatBot.TokenUsed -= OnTokenUsed;
    }

    string OnChatSend(string message)
    {
        foreach (string keyword in Configuration.RecallHintKeywords)
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
            if (Interlocked.Exchange(ref compressing, 1) == 1)
                return; //已在压缩中，跳过本次留待下轮触发

            if (content.Role != AuthorRole.Assistant)
                return; //只在ai说话后整理，这样对话更完整，而且可以避免在ai异常时保持记忆

            //阶段一：先落盘历史并取出快照，随后在快照副本上完成压缩检测与LLM归纳，避免长时间占用真实上下文
            MemoryCompressionPlan? plan;
            ChatHistory snapshot = new();
            await ChatBot.EditChatHistoryAsync(thread => {
                memoryManager.SaveHistory(thread.ChatHistory);
                foreach (var message in thread.ChatHistory)
                    snapshot.Add(message);
                return Task.CompletedTask;
            }, "存储记忆");

            var thinking = Configuration.ThinkingCompress
                ? compressModel.GetThinkingRequester().Rent("记忆压缩")
                : compressModel.GetThinkingRequester().Rent("记忆压缩(轻量)");
            try
            {
                plan = await memoryManager.Analyze(snapshot);
            }
            finally
            {
                thinking.Dispose();
            }

            //阶段二：归纳完成后，原子替换真实上下文
            if (plan != null)
            {
                await ChatBot.EditChatHistoryAsync(async thread => {
                    await memoryManager.Apply(thread.ChatHistory, plan);
                    memoryManager.SaveHistory(thread.ChatHistory);
                }, "应用记忆压缩");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "记忆压缩失败");
        }
        finally
        {
            Interlocked.Exchange(ref compressing, 0);
        }
    }

    void OnTokenUsed(TokenUsage usage)
    {
        if (usage.Total < Configuration.TokenWarningThreshold)
            return;

        logger.LogWarning($$"""
                            检测到上下文token已超过警告线，必须进行人工干涉！
                            (当前上下文约 {{usage.Input}} tokens / 警告线 {{Configuration.TokenWarningThreshold}} tokens)
                            1. 检查完整背景中的永久（最高级）记忆，此记忆通常由AI自主记忆，必须由人工删除。
                            2. 如果自动压缩的记忆已达最高级，而造成记忆膨胀，请继续提高最大记忆压缩等级。
                            3. 如果你的模型实际上还有充足的token容量，且未觉得上下文异常，可以提高警告线数值。
                            """);
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
        int startIndex = history.Count;
        string prompt = promptTemplate.Replace("{range}", range);
        try
        {
            history.AddUserMessage(prompt);

            AlifeLog.LogInformation("记忆压缩中......");
            TokenUsage tokenUsage = new();
            Exception? exception = null;
            string response = await languageModel.ChatStreamingAsync(
                chatHistoryAgentThread,
                exceptionThrow: e => exception = e,
                tokenUsed: usage => {
                    tokenUsage += usage;
                });
            AlifeLog.LogInformation("压缩消耗：" + tokenUsage);

            if (exception != null)
                throw new Exception("记忆压缩失败！", innerException: exception);
            if (string.IsNullOrEmpty(response) || response.Length < 100)
                throw new Exception($"记忆压缩失败！压缩结果长度过短：“{response}”");

            return response;
        }
        finally
        {
            history.RemoveRange(startIndex, history.Count - startIndex);
        }
    }
}