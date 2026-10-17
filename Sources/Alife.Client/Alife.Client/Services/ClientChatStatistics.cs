using Alife.Framework;

namespace Alife.Client;

public class ChatSettings
{
    public string UserTag { get; set; } = "消息来源:[ChatWindow]";
    public int MaxMessageCount { get; set; } = 100;
    public bool ShowReasoning { get; set; } = true;
}

public class ChatMessage
{
    public string? Content { get; set; }
    public string? Reasoning { get; set; }
    public string? ThinkingReason { get; set; }
    public bool IsUser { get; set; }
    public bool IsInputting { get; set; }
    public bool IsReasoning { get; set; }
}

/// <summary>
/// UI层的聊天消息状态管理。在角色激活后立即挂接事件，确保后台对话也能被记录。
/// 采用名称索引以确保在活动重启（Character对象被Clone）时记录依然能够持久。
/// </summary>
public static class ClientChatStatistics
{
    public static void Initialize(ChatActivitySystem system, StorageSystem storageSystem)
    {
        system.ActivatingCreated += OnActivityCreated;
        system.Deactivated += OnActivityDeactivated;
        system.ActivationFailed += OnActivationFailed;

        ClientChatStatistics.storageSystem = storageSystem;
        chatSettings = storageSystem.GetObject(SettingsKey, new ChatSettings())!;
    }

    public static event Action? ChatbotMapUpdated;
    public static event Action<string>? MessageChanged;
    public static event Action<string>? UserMessageSent;
    public static event Action<string, Exception>? ChatExceptionThrew;

    public static string MessageTag
    {
        get => chatSettings.UserTag;
        set
        {
            chatSettings.UserTag = value;
            SaveSettings();
        }
    }
    public static int MaxMessageCount
    {
        get => chatSettings.MaxMessageCount;
        set
        {
            chatSettings.MaxMessageCount = value;
            SaveSettings();
        }
    }
    public static bool ShowReasoning
    {
        get => chatSettings.ShowReasoning;
        set
        {
            chatSettings.ShowReasoning = value;
            SaveSettings();
        }
    }

    public static List<ChatMessage> GetMessages(string name)
    {
        if (MessagesMap.ContainsKey(name) == false)
            MessagesMap.Add(name, new List<ChatMessage>());
        return MessagesMap[name];
    }
    public static void ClearMessages(string name)
    {
        if (MessagesMap.TryGetValue(name, out List<ChatMessage>? list))
        {
            list.Clear();
        }
    }
    public static void SendMessage(string name, string message)
    {
        if (ChatbotMap.TryGetValue(name, out ChatBot? bot))
            bot.Chat(MessageTag + message);
    }

    public static string GetDraft(string name) => DraftMap.GetValueOrDefault(name) ?? "";
    public static void SetDraft(string name, string draft) => DraftMap[name] = draft;

    static readonly Dictionary<string, ChatBot> ChatbotMap = new();
    static readonly Dictionary<string, List<ChatMessage>> MessagesMap = new();
    static readonly Dictionary<string, string> DraftMap = new();

    const string SettingsKey = "Settings/ChatSettings";
    static StorageSystem storageSystem = null!;
    static ChatSettings chatSettings = null!;

    /// <summary>
    /// 确保指定Activity的ChatBot事件已挂接到UI消息列表。
    /// 幂等操作，重复调用安全。
    /// </summary>
    static void OnActivityCreated(ChatActivity activity)
    {
        string name = activity.Character.Name;
        List<ChatMessage> messages = GetMessages(name);
        ChatbotMap[name] = activity.ChatBot; // 幂等：直接赋值覆盖，避免重复激活时抛异常
        ChatbotMapUpdated?.Invoke();
        activity.ChatBot.ChatSent += message =>
        {
            lock (messages)
            {
                messages.Add(new ChatMessage { Content = message, IsUser = true });
                string? thinkingReason = null;

                if (activity.ChatBot.LanguageModel != null)
                {
                    activity.ChatBot.LanguageModel.GetThinkingRequester().Query(list =>
                    {
                        if (list.Count > 0)
                            thinkingReason = string.Join(" | ", list.Select(marker => marker.Reason));
                    });
                }

                messages.Add(new ChatMessage { IsUser = false, IsInputting = true, ThinkingReason = thinkingReason });
                TrimMessages(name);
            }

            MessageChanged?.Invoke(name);
            UserMessageSent?.Invoke(name);
        };
        activity.ChatBot.ChatReceived += (obj) =>
        {
            ChatMessage? aiMessage = messages.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.IsReasoning = false;
                aiMessage.Content += obj;
                MessageChanged?.Invoke(name);
            }
        };
        activity.ChatBot.ReasoningReceived += (obj) =>
        {
            ChatMessage? aiMessage = messages.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.IsReasoning = true;
                aiMessage.Reasoning += obj;
                MessageChanged?.Invoke(name);
            }
        };
        activity.ChatBot.ChatOver += () =>
        {
            ChatMessage? aiMessage = messages.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.IsReasoning = false;
                aiMessage.IsInputting = false;
                MessageChanged?.Invoke(name);
            }
        };
        activity.ChatBot.ChatExceptionThrow += exception => ChatExceptionThrew?.Invoke(name, exception);
    }
    static void OnActivationFailed(Character arg1, Exception arg2)
    {
        ChatbotMap.Remove(arg1.Name);
        ChatbotMapUpdated?.Invoke();
    }
    static void OnActivityDeactivated(ChatActivity activity)
    {
        string name = activity.Character.Name;
        ChatbotMap.Remove(name);
        ChatbotMapUpdated?.Invoke();
    }

    static void TrimMessages(string name)
    {
        if (MessagesMap.TryGetValue(name, out List<ChatMessage>? list) && list.Count > chatSettings.MaxMessageCount)
        {
            list.RemoveRange(0, list.Count - chatSettings.MaxMessageCount);
        }
    }
    static void SaveSettings()
    {
        storageSystem.SetObject(SettingsKey, chatSettings);
    }
}