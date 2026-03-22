using Alife.Abstractions;
using Alife.Plugins.Official.Implement;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;

namespace Alife.OfficialPlugins;

public class MemoryServiceData
{
    public int MaxFullMessageCount { get; set; } = 90;
}
[Plugin("记忆存储", "引导AI编写日记并自动在活动开始结束前进行上下文的备份和恢复", LaunchOrder = -110)]
public class MemoryService : Plugin, IConfigurable<MemoryServiceData>
{
    struct ContextItem
    {
        public string content;
        public bool isUser;
    }

    readonly StorageSystem storageSystem;
    string storageKey = null!;
    ChatHistoryAgentThread chatContext = null!;
    Character character = null!;
    MemoryServiceData configuration = null!;
    ChatCompletionAgent summaryAgent = null!;
    readonly SemaphoreSlim isCompressing;
    ChatBot chatBot = null!;

    public MemoryService(StorageSystem storageSystem, OpenAIChatService openAIChatService)
    {
        this.storageSystem = storageSystem;
        isCompressing = new SemaphoreSlim(1);
    }

    public void Configure(MemoryServiceData configuration)
    {
        this.configuration = configuration;
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        chatContext = context.contextBuilder;
        character = context.character;
        storageKey = $"ContextService/{context.character.ID}";

        LoadContext();
        InjectPrompt();

        return Task.CompletedTask;
    }
    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        //上下文压缩需要的工具
        chatBot = chatActivity.ChatBot;
        chatBot.ChatHistoryAdd += OnChatHistoryAdd;
        summaryAgent = new() {
            Name = character.Name,
            Instructions = character.Prompt,
            InstructionsRole = AuthorRole.System,
            Kernel = kernel.Clone(),
            Arguments = new KernelArguments(
                new PromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.None(), }
            ),
        };

        return Task.CompletedTask;
    }
    async void OnChatHistoryAdd(ChatMessageContent obj)
    {
        if (chatContext.ChatHistory.Count == configuration.MaxFullMessageCount / 3 * 2)
        {
            chatBot.Poke($"[{nameof(MemoryService)}][警告]上下文太多，即将压缩，如果有重要的记忆请及时保存到记忆文件。");
        }

        await isCompressing.WaitAsync(TimeSpan.Zero);
        {
            if (chatContext.ChatHistory.Count >= configuration.MaxFullMessageCount)
            {
                Console.WriteLine("[正在压缩记忆]");

                //进行记忆总结
                ChatMessageContent[] contents = chatContext.ChatHistory.Take(configuration.MaxFullMessageCount / 3 * 2)
                    .Where(content => content.Role == AuthorRole.User || content.Role == AuthorRole.Assistant)
                    .Append(new ChatMessageContent(AuthorRole.User, $"[{nameof(MemoryService)}]{character.Name}，现在系统需要压缩记忆，请你把本次的对话的内容，仔细斟酌后简要总结一下（不要使用任何指令，直接说出你的想法即可）。"))
                    .ToArray();
                string? result = null;
                await foreach (AgentResponseItem<ChatMessageContent> content in summaryAgent.InvokeAsync(contents))
                {
                    result = content.Message.Content;
                }

                //等待空闲时，替换记忆
                await chatBot.ChatSemaphore.WaitAsync();
                chatContext.ChatHistory.RemoveRange(0, configuration.MaxFullMessageCount / 3 * 2);
                if (string.IsNullOrEmpty(result) == false)
                    chatContext.ChatHistory.Insert(0, new ChatMessageContent(AuthorRole.Assistant, "历史回忆：\n" + result));
                chatBot.ChatSemaphore.Release();

                Console.WriteLine("[压缩记忆完成]：\n" + result);
            }
        }
        isCompressing.Release();
    }
    public override Task DestroyAsync()
    {
        SaveContext();

        return Task.CompletedTask;
    }

    void InjectPrompt()
    {
        chatContext.ChatHistory.AddSystemMessage($@"# {nameof(MemoryService)}
你拥有存储和读取长期记忆的能力，你的记忆文件夹在这：
{storageSystem.GetStoragePath()}/Memories/{character.ID}
你首次拥有记忆的时间（出生时间）是：
{character.Birthday}

请用python进行你的记忆读写。

## 关于记忆文件规范

记忆文件采用md格式存储，并且需要根据当前时间与出生时间的间隔，将记忆文件分别存放到4种文件夹中：
- 每1日记忆
- 每5日记忆
- 每30日记忆
- 每150日记忆

记忆文件在每个文件夹中按实际顺序编号，例如假设距离出生日期已有6天，则你的记忆文件夹内容将是如下状态：
- 每1日记忆
  - 1.md
  - 2.md
  - 3.md
  - 4.md
  - 5.md
  - 6.md
- 每5日记忆
  - 1.md

## 关于记忆整理任务

1. 你需要每天都编写日记，如果记忆文件已存在，则与其合并。
2. 每当隔5/30/150日时，你要检查相应的记忆文件夹是否有对应的记忆文件，没用则进行总结。
3. 每天开始前你需要阅读前几日的记忆，并按需读取前5/30/150的记忆。
4. 上述任务只在空闲时处理，例如在系统的定时事件中，用户长时间无反应时执行.
");
    }
    void LoadContext()
    {
        List<ContextItem> lastContext = storageSystem.GetObject(storageKey, new List<ContextItem>())!;
        foreach (ContextItem contextItem in lastContext)
        {
            if (contextItem.isUser)
                chatContext.ChatHistory.AddUserMessage(contextItem.content);
            else
                chatContext.ChatHistory.AddAssistantMessage(contextItem.content);
        }
    }
    void SaveContext()
    {
        List<ContextItem> history = FetchHistory(configuration.MaxFullMessageCount, true);
        storageSystem.SetObject(storageKey, history);
    }

    List<ContextItem> FetchHistory(int count, bool takeLast)
    {
        List<ContextItem> history = new(count);

        IEnumerable<ChatMessageContent> contents = chatContext.ChatHistory
            .Where(content => content.Role == AuthorRole.User | content.Role == AuthorRole.Assistant);
        contents = takeLast ? contents.TakeLast(count) : contents.Take(count);

        foreach (ChatMessageContent contextItem in contents)
        {
            if (string.IsNullOrWhiteSpace(contextItem.Content))
                continue;

            if (contextItem.Role == AuthorRole.User)
                history.Add(new ContextItem() { content = contextItem.Content, isUser = true });
            else if (contextItem.Role == AuthorRole.Assistant)
                history.Add(new ContextItem() { content = contextItem.Content, isUser = false });
        }

        return history;
    }
}
