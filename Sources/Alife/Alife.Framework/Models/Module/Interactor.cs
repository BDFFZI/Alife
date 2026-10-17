using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Framework;

public partial class Interactor<T>
{
    public static string GetPromptTag()
    {
        return $"[功能说明({typeof(T).Name})]";
    }
    public static string GetMessageTag()
    {
        return $"[消息来源({typeof(T).Name})]";
    }
}

#pragma warning disable CS0618
public partial class Interactor<T>(ChatBot target) : IInteractor<T>, IDisposable
#pragma warning restore CS0618
{
    public Func<string, string> ChatTextFilter { get; set; } = text => text;
    public void Prompt(string prompt)
    {
        if (promptContent == null)
        {
            //尝试获取老提示词注入点
            promptContent = target.ChatHistory.FirstOrDefault(content => content.Content?.StartsWith(GetPromptTag()) ?? false);
            if (promptContent == null)
            {
                //需要创建新的
                (ChatMessageContent item, int index) = target.ChatHistory
                    .Select((item, index) => (item, index))
                    .LastOrDefault(x => x.item.Role == AuthorRole.System);
                promptContent = new ChatMessageContent(AuthorRole.System, "");
                target.EditChatHistory(thread => {
                    thread.ChatHistory.Insert(item == null ? 0 : index + 1, promptContent);
                }, "注入提示词");
            }
        }

        string content = $"{GetPromptTag()}\n{prompt}";
        promptContent.Content = content;
    }
    public void Poke(string message)
    {
        target.Poke(ChatTextFilter(GetMessageTag() + message));
    }
    public void Chat(string message)
    {
        target.Chat(ChatTextFilter(GetMessageTag() + message));
    }
    public Task<ChatResult> ChatAsync(string message)
    {
        return target.ChatAsync(ChatTextFilter(GetMessageTag() + message));
    }

    ChatMessageContent? promptContent;

    public void Dispose()
    {
        if (promptContent != null)
            target.EditChatHistory(thread => thread.ChatHistory.Remove(promptContent), "卸载提示词");
    }
}