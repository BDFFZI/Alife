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
    public Task ChatAsync(string message);
    public Task ImplicitChatAsync(string message);
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
        if (target.ChatHistory.Any(content => content.Content?.StartsWith(GetPromptTag()) ?? false))
            return;

        (ChatMessageContent item, int index) = target.ChatHistory
            .Select((item, index) => (item, index))
            .FirstOrDefault(x => x.item.Role == AuthorRole.System);
        if (item == null)
            target.ChatHistory.AddSystemMessage($"{GetPromptTag()}\n{prompt}");
        else
            target.ChatHistory.Insert(index + 1, new ChatMessageContent(AuthorRole.System, $"{GetPromptTag()}\n{prompt}"));

        target.UpdateHistoryEndIndex();
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
    public Task ChatAsync(string message)
    {
        return target.ChatAsync(ChatTextFilter(message));
    }
    public Task ImplicitChatAsync(string message)
    {
        return target.ImplicitChatAsync(ChatTextFilter(message));
    }
}