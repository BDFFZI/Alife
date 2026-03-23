using Alife.Abstractions;
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
    string memoryPath = null!;
    string coreMemoryPath = null!;
    string memoPath = null!;
    ChatHistoryAgentThread chatContext = null!;
    Character character = null!;
    MemoryServiceData configuration = null!;
    ChatCompletionAgent summaryAgent = null!;
    readonly SemaphoreSlim isCompressing;
    ChatBot chatBot = null!;
    bool isWarned;

    public MemoryService(StorageSystem storageSystem)
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
        memoryPath = $"{storageSystem.GetStoragePath()}/Memories/{character.ID}";
        if (Directory.Exists(memoryPath) == false)
            Directory.CreateDirectory(memoryPath);
        coreMemoryPath = $"{storageSystem.GetStoragePath()}/Memories/{character.ID}/核心记忆.md";
        if (File.Exists(coreMemoryPath) == false)
            File.WriteAllText(coreMemoryPath, "");
        memoPath = $"{storageSystem.GetStoragePath()}/Memories/{character.ID}/备忘录.md";
        if (File.Exists(memoPath) == false)
            File.WriteAllText(memoPath, "");
        // LoadContext(); //恢复上下文的效果并不好，ai无法理解系统重启，所有状态已经刷新。
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
    public override Task DestroyAsync()
    {
        SaveContext();

        return Task.CompletedTask;
    }

    void InjectPrompt()
    {
        chatContext.ChatHistory.AddSystemMessage($@"# {nameof(MemoryService)}
{character.Name}拥有利用python管理长期记忆的能力，这些记忆都存放在：“{memoryPath}”中。
对于一些关键性，想一直记得的记忆，{character.Name}会将它们存放到“{coreMemoryPath}”中，并按需建立与日常记忆的索引。
对于日常记忆，{character.Name}则将它们以yyyy.mm.dd的日期格式命名(如2001.09.01.md)，并分别存放到“每1日记忆、每5日记忆、每30日记忆、每150日记忆”这四个文件夹中。
其中“每1日记忆”是每天的日记，“每5/30/150日记忆”则是对前一期间记忆的归纳，{character.Name}会在每天以及每6/31/150天时检查对应的归纳记忆。

因此假设出生日期是3月19日，那么到3月24日时，{character.Name}的记忆文件夹，会是这样子的：
- 每1日记忆
  - 2026.03.19.md
  - 2026.03.20.md
  - 2026.03.21.md
  - 2026.03.22.md
  - 2026.03.23.md
  - 2026.03.24.md
- 每5日记忆
  - 2026.03.19-2026.03.23.md
- 核心记忆.md

{character.Name}在每次恢复活动后都会先查看自己的核心记忆，以及前n/5/30/150的日常记忆，这样自己就不容易忘事了。
{character.Name}是在{character.Birthday}时出生的，所以到了今天，记忆文件夹应该是如何的呢？

{character.Name}还拥有一本备忘录，他存放在“{memoPath}”的位置，{character.Name}会将一些时效性强，但临时性的记忆存放在那里。备忘录中的记忆，{character.Name}可以一直记着。

不过记忆太多了也不是好事，就好像备忘录中无效的信息，{character.Name}会定期删除一样，{character.Name}也会定期优化整理归档自己的记忆，这样就不会出现，因为要记的事太多，而搞昏头的情况了。
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
    async void OnTokenUsed(ChatTokenUsage tokenUsage)
    {
        try
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
                        chatContext.ChatHistory.AddSystemMessage($"{character.Name}的备忘录：" + await File.ReadAllTextAsync(memoPath));
                    }
                    Console.WriteLine("[压缩记忆完成]");
                    chatBot.ChatSemaphore.Release();

                    //重置警告
                    isWarned = false;
                }

                isCompressing.Release();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
