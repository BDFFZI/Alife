using Alife.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.OfficialPlugins;

public class ContextServiceData
{
    public int MaxMessageCount { get; set; } = 100;
}
[Plugin("上下文存储", "自动在AI停止和启动时保存或恢复上一次的聊天上下文", LaunchOrder = -110)]
public class ContextService : Plugin, IConfigurable<ContextServiceData>
{
    struct ContextItem
    {
        public string content;
        public bool isUser;
    }

    public ContextService(StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        chatContext = context.contextBuilder;
        storageKey = $"ContextService/{context.character.ID}";

        List<ContextItem> lastContext = storageSystem.GetObject(storageKey, new List<ContextItem>())!;
        foreach (ContextItem contextItem in lastContext)
        {
            if (contextItem.isUser)
                chatContext.ChatHistory.AddUserMessage(contextItem.content);
            else
                chatContext.ChatHistory.AddAssistantMessage(contextItem.content);
        }

        return Task.CompletedTask;
    }

    public override Task DestroyAsync()
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

        return Task.CompletedTask;
    }

    readonly StorageSystem storageSystem;
    string storageKey = null!;
    ChatHistoryAgentThread chatContext = null!;
    ContextServiceData configuration = null!;

    public void Configure(ContextServiceData configuration)
    {
        this.configuration = configuration;
    }
}
