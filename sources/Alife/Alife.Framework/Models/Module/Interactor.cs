using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Framework;

public interface IInteractor
{
    public Func<string, string> ChatTextFilter { get; set; }
    public void Prompt(string prompt);
    public void Throw(string error);
    public void Poke(string message);
    public void Chat(string message);
    public Task<string> ChatAsync(string message);
}

public interface IInteractor<T> : IInteractor;

public class Interactor<T>(ChatBot target) : IInteractor<T>
{
    public string GetPromptTag()
    {
        return $"[功能说明({typeof(T).Name})]";
    }

    public Func<string, string> ChatTextFilter { get; set; } = text => $"消息来源:[{typeof(T).Name}]\n{text}";
    public void Prompt(string prompt)
    {
        string content = $"{GetPromptTag()}\n{prompt}";

        ChatMessageContent? chatMessageContent = target.ChatHistory.FirstOrDefault(content => content.Content?.StartsWith(GetPromptTag()) ?? false);
        if (chatMessageContent != null)
        {
            chatMessageContent.Content = content;
            return;
        }

        (ChatMessageContent item, int index) = target.ChatHistory
            .Select((item, index) => (item, index))
            .FirstOrDefault(x => x.item.Role == AuthorRole.System);

        target.EditChatHistory(thread => {
            if (item == null)
                thread.ChatHistory.AddSystemMessage(content);
            else
                thread.ChatHistory.Insert(index + 1, new ChatMessageContent(AuthorRole.System, content));
        }, "注入提示词");
    }
    public void Throw(string error)
    {
        throw new Exception($"[{typeof(T).Name}] 发生错误\n{error}");
    }
    public void Poke(string message)
    {
        target.Poke(ChatTextFilter(message));
    }
    public void Chat(string message)
    {
        target.Chat(ChatTextFilter(message));
    }
    public Task<string> ChatAsync(string message)
    {
        return target.ChatAsync(ChatTextFilter(message));
    }
}