using Alife.Abstractions;

namespace Alife.Modules.Context;

public class ChatMessage
{
    public string? tool;
    public string? content;
    public bool isUser;
    public bool isInputting;
    public bool isDefaultHiding;
}
[Plugin("背景-对话窗口", "提供一个传统的聊天窗口平台来显示内容。")]
public class ChatWindow : Plugin
{
    public event Action<ChatMessage>? MessageAdded;
    public event Action<ChatMessage>? MessageUpdated;

    public List<ChatMessage> GetMessages()
    {
        return messages;
    }
    public void AddMessage(ChatMessage chatMessage)
    {
        messages.Add(chatMessage);
        MessageAdded?.Invoke(chatMessage);
    }
    public void UpdateMessage(ChatMessage chatMessage)
    {
        MessageUpdated?.Invoke(chatMessage);
    }

    readonly List<ChatMessage> messages = new();
}
