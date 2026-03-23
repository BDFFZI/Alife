using Alife.Abstractions;
using Alife.Plugins.Official.Implement;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI.Chat;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace Alife.OfficialPlugins;

public class MemoryServiceData
{
    public int MaxTokenSize { get; set; } = 30000;
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
    bool isWarned;

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
        chatBot.TokenUsed += OnTokenUsed;
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
    async void OnTokenUsed(ChatTokenUsage tokenUsage)
    {
        if (isWarned == false && tokenUsage.TotalTokenCount > configuration.MaxTokenSize * 0.9f)
        {
            chatBot.Poke($"[{nameof(MemoryService)}][警告]上下文太多，即将压缩，如果有重要的记忆请及时保存到记忆文件。");
            isWarned = true;
        }

        if (await isCompressing.WaitAsync(TimeSpan.Zero))
        {
            if (tokenUsage.TotalTokenCount > configuration.MaxTokenSize)
            {
                await chatBot.ChatSemaphore.WaitAsync(); //等待聊天暂停
                Console.WriteLine("[正在压缩记忆]");
                {
                    //分类记忆
                    ChatMessageContent[] chatContent = chatContext.ChatHistory
                        .Where(content => content.Role == AuthorRole.User || content.Role == AuthorRole.Assistant)
                        .ToArray();
                    ChatMessageContent[] systemContent = chatContext.ChatHistory
                        .Where(content => content.Role == AuthorRole.System)
                        .ToArray();

                    //进行记忆总结
                    ChatMessageContent[] contents = chatContent.Take(chatContent.Length / 5 * 4)
                        .Append(new ChatMessageContent(AuthorRole.User, $"[{nameof(MemoryService)}]{character.Name}，现在系统需要压缩记忆，请你把本次的对话的内容，仔细斟酌后简要总结一下（不要使用任何指令，直接说出你的想法即可）。"))
                        .ToArray();
                    string? result = null;
                    await foreach (AgentResponseItem<ChatMessageContent> content in summaryAgent.InvokeAsync(contents))
                    {
                        result = content.Message.Content;
                    }

                    //重新插入记忆
                    chatContext.ChatHistory.Clear();
                    chatContext.ChatHistory.AddAssistantMessage("历史回忆：\n" + result);
                    chatContext.ChatHistory.AddRange(chatContent.TakeLast(chatContent.Length / 5 * 1));
                    chatContext.ChatHistory.AddRange(systemContent);
                }
                Console.WriteLine("[压缩记忆完成]");
                chatBot.ChatSemaphore.Release();

                //重置警告
                isWarned = false;
            }

            isCompressing.Release();
        }
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

1. 记忆文件采用md格式存储，文件名用日期命名。
2. 根据当前日期与出生日期的间隔天数，将记忆文件分别存放到4种文件夹中：
    - 每1日记忆（每日记忆）
    - 每5日记忆（总结记忆）
    - 每30日记忆（总结记忆）
    - 每150日记忆（总结记忆）

例如假设出生日期是3月19日，那么到3月24日记忆文件夹内容将是如下状态：
- 每1日记忆
  - 2026.3.19.md
  - 2026.3.20.md
  - 2026.3.21.md
  - 2026.3.22.md
  - 2026.3.23.md
  - 2026.3.24.md
- 每5日记忆
  - 2026.3.19-2026.3.23.md

## 关于记忆整理任务

1. 你需要每天都编写日记，如果当天的记忆文件已存在，则与其合并或重新总结。
2. 每当隔6/31/151日时，你要检查前5/30/150日的总结记忆是否存在，没有则进行总结。
3. 每次启动后立即开始阅读前几日的记忆文件，并按需读取前5/30/150的记忆文   件。
4. 整理任务只在空闲时处理，例如在系统的定时报点事件，且用户长时间无反应时执行.
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
        //获取上下文历史
        List<ContextItem> history = new List<ContextItem>();
        IEnumerable<ChatMessageContent> contents = chatContext.ChatHistory
            .Where(content => content.Role == AuthorRole.User | content.Role == AuthorRole.Assistant);
        foreach (ChatMessageContent contextItem in contents)
        {
            if (string.IsNullOrWhiteSpace(contextItem.Content))
                continue;

            if (contextItem.Role == AuthorRole.User)
                history.Add(new ContextItem() { content = contextItem.Content, isUser = true });
            else if (contextItem.Role == AuthorRole.Assistant)
                history.Add(new ContextItem() { content = contextItem.Content, isUser = false });
        }

        //保存
        storageSystem.SetObject(storageKey, history);
    }
}
