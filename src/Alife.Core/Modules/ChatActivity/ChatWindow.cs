using Alife.Abstractions;

public class ChatMessage
{
    public string? author;
    public string? content;
    public bool isUser;
    public bool isInputting;
    public bool isDefaultHiding;
}

[Plugin("框架-聊天窗口", "提供一个公用的有配套界面的聊天窗口。")]
public class ChatWindow : IPlugin
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

    readonly List<ChatMessage> messages;

    public ChatWindow(List<ChatMessage>? messages = null)
    {
        this.messages = messages ?? new List<ChatMessage>();
    }
}
