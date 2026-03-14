using Alife.Abstractions;

public class ChatMessage
{
    public string content = "";
    public bool isUser;
}

[Plugin("框架-聊天窗口", "提供一个公用的有配套界面的聊天窗口。")]
public class ChatWindow : IPlugin
{
    public event Action<ChatMessage>? MessageAdded;
    public event Action? MessageUpdated;

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
        MessageUpdated?.Invoke();
    }

    readonly List<ChatMessage> messages = new();
}
