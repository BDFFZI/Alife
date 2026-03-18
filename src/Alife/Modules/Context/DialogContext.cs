using Alife.Abstractions;

namespace Alife.Modules.Context;

public class DialogItem
{
    public string? tool;
    public string? content;
    public bool isUser;
    public bool isInputting;
    public bool isDefaultHiding;
}
[Plugin("背景-对话窗口", "提供一个传统的聊天窗口平台来显示内容。")]
public class DialogContext : Plugin
{
    public event Action<DialogItem>? MessageAdded;
    public event Action<DialogItem>? MessageUpdated;
    
    public List<DialogItem> GetMessages()
    {
        return messages;
    }
    public void AddMessage(DialogItem dialogItem)
    {
        messages.Add(dialogItem);
        MessageAdded?.Invoke(dialogItem);
    }
    public void UpdateMessage(DialogItem dialogItem)
    {
        MessageUpdated?.Invoke(dialogItem);
    }

    readonly List<DialogItem> messages = new();
}
