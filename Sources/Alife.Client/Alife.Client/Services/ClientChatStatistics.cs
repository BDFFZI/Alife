using System.Collections.Concurrent;
using System.Text;
using Alife.Framework;

namespace Alife.Client;

public class ChatSettings
{
    public string UserTag { get; set; } = "[源(ChatWindow)]";
    public int MaxMessageCount { get; set; } = 100;
    public bool ShowReasoning { get; set; } = true;
}

public class ChatMessage
{
    /// <summary>
    /// 取值命中缓存时返回同一字符串实例，Blazor 比较文本帧会直接命中引用相等，
    /// 长对话不会在每次流式刷新时把所有历史消息重新复制一遍。
    /// </summary>
    public string Content
    {
        get
        {
            lock (textLock)
                return content ??= contentBuilder.ToString();
        }
        init => AppendContent(value);
    }
    public string Reasoning
    {
        get
        {
            lock (textLock)
                return reasoning ??= reasoningBuilder.ToString();
        }
    }
    public bool HasReasoning
    {
        get
        {
            lock (textLock)
                return reasoningBuilder.Length != 0;
        }
    }
    public string? ThinkingReason { get; set; }
    public bool IsUser { get; set; }
    public bool IsInputting { get; set; }
    public bool IsReasoning { get; set; }
    /// <summary>思考过程是否展开。新消息按全局开关初始化，之后由用户点击决定，渲染不再覆盖。</summary>
    public bool ReasoningExpanded { get; set; }

    public void AppendContent(string text)
    {
        lock (textLock)
        {
            contentBuilder.Append(text);
            content = null;
        }
    }
    public void AppendReasoning(string text)
    {
        lock (textLock)
        {
            reasoningBuilder.Append(text);
            reasoning = null;
        }
    }

    readonly object textLock = new();
    readonly StringBuilder contentBuilder = new();
    readonly StringBuilder reasoningBuilder = new();
    string? content;
    string? reasoning;
}

public static class ClientChatStatistics
{
    /// <summary>
    /// UI层的聊天消息状态管理。在角色激活后立即挂接事件，确保后台对话也能被记录。
    /// 采用名称索引以确保在活动重启（Character对象被Clone）时记录依然能够持久。
    /// </summary>
    [InitializeOnLoad]
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
        return MessagesMap.GetOrAdd(name, _ => new List<ChatMessage>());
    }
    public static void ClearMessages(string name)
    {
        if (MessagesMap.TryGetValue(name, out List<ChatMessage>? list))
        {
            lock (list)
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

    static readonly ConcurrentDictionary<string, ChatBot> ChatbotMap = new();
    static readonly ConcurrentDictionary<string, List<ChatMessage>> MessagesMap = new();
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
        activity.ChatBot.ChatSent += message => {
            string? thinkingReason = null;
            if (activity.ChatBot.LanguageModel != null)
            {
                activity.ChatBot.LanguageModel.GetThinkingRequester().Query(list => {
                    if (list.Count > 0)
                        thinkingReason = string.Join(" | ", list.Select(marker => marker.Reason));
                });
            }

            lock (messages)
            {
                messages.Add(new ChatMessage { Content = message, IsUser = true });
                messages.Add(new ChatMessage {
                    IsUser = false,
                    IsInputting = true,
                    ThinkingReason = thinkingReason,
                    ReasoningExpanded = chatSettings.ShowReasoning,
                });
                TrimMessages(name);
            }

            MessageChanged?.Invoke(name);
        };
        activity.ChatBot.ChatReceived += (obj) => {
            lock (messages)
            {
                ChatMessage? aiMessage = messages.FindLast(m => m is { IsUser: false, IsInputting: true });
                if (aiMessage == null)
                    return;

                aiMessage.IsReasoning = false;
                aiMessage.AppendContent(obj);
            }

            MessageChanged?.Invoke(name);
        };
        activity.ChatBot.ReasoningReceived += (obj) => {
            lock (messages)
            {
                ChatMessage? aiMessage = messages.FindLast(m => m is { IsUser: false, IsInputting: true });
                if (aiMessage == null)
                    return;

                aiMessage.IsReasoning = true;
                aiMessage.AppendReasoning(obj);
            }

            MessageChanged?.Invoke(name);
        };
        activity.ChatBot.ChatOver += () => {
            lock (messages)
            {
                ChatMessage? aiMessage = messages.FindLast(m => m is { IsUser: false, IsInputting: true });
                if (aiMessage == null)
                    return;

                aiMessage.IsReasoning = false;
                aiMessage.IsInputting = false;
            }

            MessageChanged?.Invoke(name);
        };
        activity.ChatBot.ChatExceptionThrow += exception => {
            if (exception is OperationCanceledException)
                return;

            ChatExceptionThrew?.Invoke(name, exception);
        };
    }
    static void OnActivationFailed(Character arg1, Exception arg2)
    {
        ChatbotMap.TryRemove(arg1.Name, out _);
        ChatbotMapUpdated?.Invoke();
    }
    static void OnActivityDeactivated(ChatActivity activity)
    {
        string name = activity.Character.Name;
        ChatbotMap.TryRemove(name, out _);
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
