using Alife.Framework;

namespace Alife.Components.Services;

public class ChatSettings
{
    public string UserTag { get; set; } = "[管理员]";
    public int MaxMessageCount { get; set; } = 200;
    public bool ShowReasoningInChat { get; set; }
}

public class ChatMessage
{
    public string? Content { get; set; }
    public string? RawContent { get; set; }
    public string? Reasoning { get; set; }
    public bool IsUser { get; set; }
    public bool IsInputting { get; set; }

    public void AppendAssistantContent(string content)
    {
        RawContent += content;
        Content = AssistantDisplayTextFilter.Filter(RawContent);
    }

    public bool ShouldDisplay(bool showReasoning)
    {
        if (IsInputting || IsUser)
            return true;

        return ChatMessageDisplayPolicy.ShouldDisplayAssistantMessage(
            Content,
            Reasoning,
            IsInputting,
            showReasoning);
    }
}

/// <summary>
/// UI层的聊天消息状态管理。在角色激活后立即挂接事件，确保后台对话也能被记录。
/// 采用名称索引以确保在活动重启（Character对象被Clone）时记录依然能够持久。
/// </summary>
public class ChatMessageService
{
    public event Action<string>? OnMessageChanged;
    public event Action<string>? OnUserMessageSent;

    public string MessageTag
    {
        get => settings.UserTag;
        set
        {
            settings.UserTag = value;
            SaveSettings();
        }
    }
    public int MaxMessageCount
    {
        get => settings.MaxMessageCount;
        set
        {
            settings.MaxMessageCount = value;
            SaveSettings();
        }
    }
    public bool ShowReasoningInChat
    {
        get => settings.ShowReasoningInChat;
        set
        {
            settings.ShowReasoningInChat = value;
            SaveSettings();
            foreach (string name in messagesMap.Keys)
                OnMessageChanged?.Invoke(name);
        }
    }

    public ChatMessageService(ChatActivitySystem system, StorageSystem storage)
    {
        this.storage = storage;
        settings = storage.GetObject(SettingsKey, new ChatSettings())!;
        system.ActivatingCreated += OnActivityCreated;
        system.Destroyed += OnActivityDestroyed;
        system.ActivationFailed += OnActivationFailed;
    }

    public ChatBot? GetChatBot(string name)
    {
        return chatbotMap.GetValueOrDefault(name);
    }
    public List<ChatMessage> GetMessages(string name)
    {
        if (messagesMap.ContainsKey(name) == false)
            messagesMap.Add(name, new List<ChatMessage>());
        return messagesMap[name];
    }
    public void ClearMessages(string name)
    {
        if (messagesMap.TryGetValue(name, out List<ChatMessage>? list))
        {
            list.Clear();
        }
    }
    public void SendMessage(string name, string message)
    {
        if (chatbotMap.TryGetValue(name, out ChatBot? bot))
            bot.Chat(MessageTag + message);
    }

    public string GetDraft(string name) => draftMap.GetValueOrDefault(name) ?? "";
    public void SetDraft(string name, string draft) => draftMap[name] = draft;

    readonly Dictionary<string, string> draftMap = new();
    readonly Dictionary<string, List<ChatMessage>> messagesMap = new();
    readonly Dictionary<string, ChatBot> chatbotMap = new();

    const string SettingsKey = "ChatSettings";
    readonly StorageSystem storage;
    readonly ChatSettings settings;

    void SaveSettings()
    {
        storage.SetObject(SettingsKey, settings);
    }

    void TrimMessages(string name)
    {
        if (messagesMap.TryGetValue(name, out List<ChatMessage>? list) && list.Count > settings.MaxMessageCount)
        {
            list.RemoveRange(0, list.Count - settings.MaxMessageCount);
        }
    }
    /// <summary>
    /// 确保指定Activity的ChatBot事件已挂接到UI消息列表。
    /// 幂等操作，重复调用安全。
    /// </summary>
    void OnActivityCreated(ChatActivity activity)
    {
        string name = activity.Character.Name;
        List<ChatMessage> messages = GetMessages(name);
        chatbotMap.Add(name, activity.ChatBot);
        activity.ChatBot.ChatInputSent += args => {
            lock (messages)
            {
                if (args.DisplayInputMessageInChat)
                    messages.Add(new ChatMessage { Content = args.Message, IsUser = true });
                messages.Add(new ChatMessage { IsUser = false, IsInputting = true });
                TrimMessages(name);
            }

            OnMessageChanged?.Invoke(name);
            if (args.DisplayInputMessageInChat)
                OnUserMessageSent?.Invoke(name);
        };
        activity.ChatBot.ChatReceived += (obj) => {
            ChatMessage? aiMessage = messages.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.AppendAssistantContent(obj);
                OnMessageChanged?.Invoke(name);
            }
        };
        activity.ChatBot.ReasoningReceived += (obj) => {
            ChatMessage? aiMessage = messages.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.Reasoning += obj;
                OnMessageChanged?.Invoke(name);
            }
        };
        activity.ChatBot.ChatOver += () => {
            ChatMessage? aiMessage = messages.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.IsInputting = false;
                OnMessageChanged?.Invoke(name);
            }
        };
    }
    void OnActivationFailed(Character arg1, Exception arg2)
    {
        chatbotMap.Remove(arg1.Name);
    }
    void OnActivityDestroyed(ChatActivity activity)
    {
        string name = activity.Character.Name;
        chatbotMap.Remove(name);
    }
}
