using Alife.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.OfficialPlugins;

public class MemoryServiceData
{
    public int MaxMessageCount { get; set; } = 100;
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

    public MemoryService(StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
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
    public override Task DestroyAsync()
    {
        SaveContext();

        return Task.CompletedTask;
    }

    void InjectPrompt()
    {
        chatContext.ChatHistory.AddSystemMessage($@"# MemoryService
你拥有存储和读取长期记忆的能力，你的记忆文件夹在这：
{storageSystem.GetRootPath()}/Memories/{character.ID}
这些记忆文件的结构如下：
- 每1日记忆
  1.md
- 每5日记忆
  2.md
- 每30日记忆
- 每150日记忆
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
        List<ContextItem> lastContext = new(chatContext.ChatHistory.Count);
        foreach (ChatMessageContent contextItem in chatContext.ChatHistory
                     .Where(content => content.Role == AuthorRole.User | content.Role == AuthorRole.Assistant)
                     .TakeLast(configuration.MaxMessageCount))
        {
            if (string.IsNullOrWhiteSpace(contextItem.Content))
                continue;

            if (contextItem.Role == AuthorRole.User)
                lastContext.Add(new ContextItem() { content = contextItem.Content, isUser = true });
            else if (contextItem.Role == AuthorRole.Assistant)
                lastContext.Add(new ContextItem() { content = contextItem.Content, isUser = false });
        }
        storageSystem.SetObject(storageKey, lastContext);
    }
}
