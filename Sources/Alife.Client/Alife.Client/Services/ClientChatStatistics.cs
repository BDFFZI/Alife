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
    /// <summary>取值每次都会生成新字符串，但流式输出只累积不拼接，避免每来一块都复制整串。</summary>
    public string Content
    {
        get => contentBuilder.ToString();
        init => contentBuilder.Append(value);
    }
    public string Reasoning => reasoningBuilder.ToString();
    public string? ThinkingReason { get; set; }
    public bool IsUser { get; set; }
    public bool IsInputting { get; set; }
    public bool IsReasoning { get; set; }
    /// <summary>思考过程是否展开。新消息按全局开关初始化，之后由用户点击决定，渲染不再覆盖。</summary>
    public bool ReasoningExpanded { get; set; }

    public void AppendContent(string text) => contentBuilder.Append(text);
    public void AppendReasoning(string text) => reasoningBuilder.Append(text);

    /// <summary>改输入并重发时替换用户气泡内容。</summary>
    public void SetContent(string text)
    {
        contentBuilder.Clear();
        contentBuilder.Append(text);
    }

    readonly StringBuilder contentBuilder = new();
    readonly StringBuilder reasoningBuilder = new();
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

    // ---------------------------------------------------------------- 对话控制

    /// <summary>停止当前生成；已生成的部分保留。</summary>
    public static void Stop(string name)
    {
        if (ChatbotMap.TryGetValue(name, out ChatBot? bot))
            bot.Stop();
    }

    /// <summary>重新生成最后一条回复。返回是否成功发起。</summary>
    public static Task<bool> RegenerateAsync(string name)
    {
        if (ChatbotMap.TryGetValue(name, out ChatBot? bot))
            return bot.RegenerateAsync();
        return Task.FromResult(false);
    }

    /// <summary>改输入并重发：同时更新 UI 的用户气泡。</summary>
    public static async Task<bool> EditAndRegenerateAsync(string name, string newText)
    {
        if (ChatbotMap.TryGetValue(name, out ChatBot? bot) == false)
            return false;

        //UI 的用户气泡要先改，ChatRegenerating 事件里只处理 AI 气泡
        List<ChatMessage> list = GetMessages(name);
        lock (list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].IsUser)
                {
                    list[i].SetContent(newText);
                    break;
                }
            }
        }

        bool ok = await bot.EditAndRegenerateAsync(newText);
        if (ok) MessageChanged?.Invoke(name);
        return ok;
    }

    /// <summary>回退到第 userTurnIndex 轮（按用户气泡序号）。</summary>
    public static async Task<int> RollbackToUserTurnAsync(string name, int userTurnIndex)
    {
        if (ChatbotMap.TryGetValue(name, out ChatBot? bot) == false)
            return 0;

        int removed = await bot.RollbackToUserTurnAsync(userTurnIndex);
        if (removed > 0)
        {
            TrimUiFromUserTurn(name, userTurnIndex);
            MessageChanged?.Invoke(name);
        }
        return removed;
    }

    /// <summary>编辑第 userTurnIndex 轮并重发（同时改 UI 的用户气泡）。</summary>
    public static async Task<bool> EditUserTurnAndRegenerateAsync(string name, int userTurnIndex, string newText)
    {
        if (ChatbotMap.TryGetValue(name, out ChatBot? bot) == false)
            return false;

        List<ChatMessage> list = GetMessages(name);
        int uiIndex = -1, seen = -1;
        lock (list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsUser == false) continue;
                if (++seen == userTurnIndex) { uiIndex = i; break; }
            }

            if (uiIndex >= 0)
            {
                //删掉这条之后的气泡（旧回复），这条本身保留并改内容
                while (list.Count - 1 > uiIndex)
                    list.RemoveAt(list.Count - 1);
                list[uiIndex].SetContent(newText);
            }
        }

        bool ok = await bot.EditUserTurnAndRegenerateAsync(userTurnIndex, newText);
        if (ok) MessageChanged?.Invoke(name);
        return ok;
    }

    /// <summary>UI 列表：从第 userTurnIndex 条用户气泡开始全删。</summary>
    static void TrimUiFromUserTurn(string name, int userTurnIndex)
    {
        List<ChatMessage> list = GetMessages(name);
        lock (list)
        {
            int seen = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsUser == false) continue;
                if (++seen != userTurnIndex) continue;

                while (list.Count > i)
                    list.RemoveAt(list.Count - 1);
                break;
            }
        }
    }

    /// <summary>
    /// 回退本轮：同时删掉 UI 的最后一对气泡和模型历史的最后一轮。
    /// </summary>
    public static async Task<int> RollbackLastTurnAsync(string name)
    {
        if (ChatbotMap.TryGetValue(name, out ChatBot? bot) == false)
            return 0;

        int removed = await bot.RollbackLastTurnAsync();
        if (removed > 0)
        {
            List<ChatMessage> list = GetMessages(name);
            lock (list)
            {
                //先删尾部的 AI 气泡（可能因为中断/重生残留多条）
                while (list.Count > 0 && list[^1].IsUser == false)
                    list.RemoveAt(list.Count - 1);

                //再删这一轮的用户气泡
                if (list.Count > 0 && list[^1].IsUser)
                    list.RemoveAt(list.Count - 1);
            }
            MessageChanged?.Invoke(name);
        }
        return removed;
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
        activity.ChatBot.ChatSent += message => {
            lock (messages)
            {
                messages.Add(new ChatMessage { Content = message, IsUser = true });
                string? thinkingReason = null;

                if (activity.ChatBot.LanguageModel != null)
                {
                    activity.ChatBot.LanguageModel.GetThinkingRequester().Query(list => {
                        if (list.Count > 0)
                            thinkingReason = string.Join(" | ", list.Select(marker => marker.Reason));
                    });
                }

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
        //重新生成：用户气泡保持原样，只把最后一条 AI 气泡换成新的"输入中"气泡。
        //编辑重发时，更新文本后再走这条路径。
        activity.ChatBot.ChatRegenerating += text => {
            lock (messages)
            {
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    if (messages[i].IsUser == false)
                    {
                        messages.RemoveAt(i);
                        break;
                    }
                }

                string? thinkingReason = null;
                if (activity.ChatBot.LanguageModel != null)
                {
                    activity.ChatBot.LanguageModel.GetThinkingRequester().Query(list => {
                        if (list.Count > 0)
                            thinkingReason = string.Join(" | ", list.Select(marker => marker.Reason));
                    });
                }

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
            ChatMessage? aiMessage = messages.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.IsReasoning = false;
                aiMessage.AppendContent(obj);
                MessageChanged?.Invoke(name);
            }
        };
        activity.ChatBot.ReasoningReceived += (obj) => {
            ChatMessage? aiMessage = messages.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.IsReasoning = true;
                aiMessage.AppendReasoning(obj);
                MessageChanged?.Invoke(name);
            }
        };
        activity.ChatBot.ChatOver += () => {
            ChatMessage? aiMessage = messages.LastOrDefault(m => m is { IsUser: false, IsInputting: true });
            if (aiMessage != null)
            {
                aiMessage.IsReasoning = false;
                aiMessage.IsInputting = false;
                MessageChanged?.Invoke(name);
            }
        };
        activity.ChatBot.ChatExceptionThrow += exception => {
            if (exception is OperationCanceledException)
                return;

            ChatExceptionThrew?.Invoke(name, exception);
        };
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
