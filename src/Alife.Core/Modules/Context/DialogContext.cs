using System;
using System.Collections.Generic;

namespace Alife.Abstractions;

public class DialogItem
{
    public string? tool;
    public string? content;
    public bool isUser;
    public bool isInputting;
    public bool isDefaultHiding;
}

[Plugin("框架-聊天窗口", "提供一个公用的有配套界面的聊天窗口。")]
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

    readonly List<DialogItem> messages;

    public DialogContext(List<DialogItem>? messages = null)
    {
        this.messages = messages ?? new List<DialogItem>();
    }
}
