using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace Alife.Framework;

public struct ChatContext
{
    public string UserMessage { get; init; }
    public string AIMessage { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public struct ChatResult
{
    public string AIThinking { get; init; }
    public string AIMessage { get; init; }
    public Exception? Exception { get; init; }
}

public class ChatBot : IAsyncDisposable
{
    public const string PokeMessageTag = "[系统推送消息]";
    public const string ObservationMessageTag = "[观察]";

    public event Func<string, string>? PokeSend; //Poke消息过滤
    public event Func<string, string>? ChatSend; //消息过滤
    public event Action<ChatMessageContent>? ChatContentSend; //消息发送后

    public event Action<string>? ChatSent; //消息发送后
    /// <summary>
    /// 重新生成：用户消息不变，只重建最后一条 AI 消息。
    /// UI 收到后应删除旧的 AI 气泡并建立一条新的"输入中"气泡，不要再建用户气泡。
    /// </summary>
    public event Action<string>? ChatRegenerating;
    public event Action<string>? ChatReceived; //接收到消息
    public event Action<string>? ReasoningReceived; //接收到思考消息
    public event Action? ChatOver; //接收结束

    public event Action<ChatMessageContent>? ChatHistoryAdd; //对话产生的消息块（如果手动插入且不更新结束索引，则也将记录）
    public event Action<Exception>? ChatExceptionThrow; //对话中出现的异常

    public event Action<ChatContext>? ChatFinished; //对话结束
    public event Func<ChatContext, Task>? ChatFinishedAsync; //对话结束(异步)

    public event Action? ChatHistoryEditing; //对话被请求（信号量首次被锁住）
    public event Action? ChatHistoryEdited; //对话已释放（信号量完全解锁）

    /// <summary>
    /// 历史被「重写」：重新生成 / 编辑重发 / 回退删除了已有消息。
    /// 记忆类插件在每条 AI 消息落盘时就存过一份，历史被删改后必须重新落盘，
    /// 否则被丢弃的那一版会永远留在记忆里，之后还会被检索出来。
    /// </summary>
    public event Action? ChatHistoryRewritten;

    public bool IsChatOccupied => chatSemaphore.CurrentCount == 0;
    public bool IsChatHistoryOccupied => chatHistorySemaphore.CurrentCount == 0;
    public OccupationNotepad ResourceOccupiedReason { get; set; } = new();
    public IReadOnlyList<ChatMessageContent> ChatHistory => chatHistorySnapshot;
    public CancellationTokenSource ChatBreakTokenSource => chatBreakSource;
    public ILanguageModel? LanguageModel { get => languageModel; set => languageModel = value; }

    public async Task EditChatHistoryAsync(Func<ChatHistoryAgentThread, Task> action, string reason)
    {
        await chatHistorySemaphore.WaitAsync();
        AlifeUtility.SafeInvoke(() => ChatHistoryEditing?.Invoke());
        try
        {
            using (ResourceOccupiedReason.Rent(reason))
                await action(chatHistoryAgentThread);
            chatHistorySnapshot = [.. chatHistoryAgentThread.ChatHistory];
            lastContentIndex = chatHistoryAgentThread.ChatHistory.Count;
        }
        finally
        {
            chatHistorySemaphore.Release();
            AlifeUtility.SafeInvoke(() => ChatHistoryEdited?.Invoke());
        }
    }

    /// <summary>
    /// 除非你明确的知道当前没有在嵌套的编辑对话历史，否则不要使用此函数，因为这容易造成死锁。建议使用async版本。
    /// </summary>
    /// <param name="action"></param>
    /// <param name="reason"></param>
    public void EditChatHistory(Action<ChatHistoryAgentThread> action, string reason)
    {
        chatHistorySemaphore.Wait();
        AlifeUtility.SafeInvoke(() => ChatHistoryEditing?.Invoke());
        try
        {
            using (ResourceOccupiedReason.Rent(reason))
                action(chatHistoryAgentThread);
            chatHistorySnapshot = [.. chatHistoryAgentThread.ChatHistory];
            lastContentIndex = chatHistoryAgentThread.ChatHistory.Count;
        }
        finally
        {
            chatHistorySemaphore.Release();
            AlifeUtility.SafeInvoke(() => ChatHistoryEdited?.Invoke());
        }
    }

    public Task<ChatResult> ChatAsync(ChatMessageContent message, bool breakLast = true)
        => ChatAsyncCore(message, breakLast, addUserMessage: true, raiseChatSent: true, skipInjection: false);

    /// <summary>
    /// 对话核心。
    /// addUserMessage=false：重新生成，用户消息已在历史里，不重复添加。
    /// raiseChatSent=false：重新生成，UI 已有气泡，不重复建。
    /// skipInjection=true：重新生成/编辑重发，复用历史里那条「已经处理过」的消息，
    ///   不再跑一遍 ChatSend 过滤器和 poke/观察注入 —— 否则每次重生都会把记忆、
    ///   知识、Soma 等插件的上下文再拼一遍，表现为「先冒出一堆注入，才开始二次输出」。
    /// </summary>
    async Task<ChatResult> ChatAsyncCore(
        ChatMessageContent message, bool breakLast, bool addUserMessage,
        bool raiseChatSent, bool skipInjection)
    {
        CancellationToken cancellationToken;

        lock (this)
        {
            if (breakLast)
                chatBreakSource.Cancel(); //打断上一次的聊天
            chatBreakSource = new CancellationTokenSource();
            cancellationToken = chatBreakSource.Token;
        }

        ChatMessageContent? observationMessage = null;
        bool chatOverRaised = false;

        void RaiseChatOverOnce()
        {
            if (chatOverRaised) return;
            chatOverRaised = true;
            try { ChatOver?.Invoke(); }
            catch (Exception e) { AlifeLog.LogError(e); }
        }

        await chatSemaphore.WaitAsync(cancellationToken);
        try
        {
            using (ResourceOccupiedReason.Rent("准备对话"))
            {
                if (skipInjection == false)
                {
                    //装载观察消息：独立成一条 observation，不拼进用户消息
                    string? observation = FlushObservationMessage();
                    if (observation != null)
                    {
                        observationMessage = new ChatMessageContent(AuthorRole.Tool, observation);
                        await EditChatHistoryAsync(thread => {
                            thread.ChatHistory.Add(observationMessage);
                            return Task.CompletedTask;
                        }, "装载观察消息");
                    }

                    //附加Poke消息
                    string? poke = FlushPokeMessage();
                    if (poke != null)
                        message.Content += "\n\n" + poke;
                }

                //预处理用户消息。重生时跳过：历史里那条已经是处理过的版本
                if (skipInjection == false)
                {
                    if (ChatContentSend != null)
                    {
                        foreach (Action<ChatMessageContent> func in ChatContentSend.GetInvocationList().Cast<Action<ChatMessageContent>>())
                        {
                            try
                            {
                                func(message);
                            }
                            catch (Exception ex)
                            {
                                AlifeLog.LogError(ex);
                            }
                        }
                    }

                    if (ChatSend != null)
                    {
                        foreach (Func<string, string> func in ChatSend.GetInvocationList().Cast<Func<string, string>>())
                        {
                            try
                            {
                                message.Content = func(message.Content ?? "");
                            }
                            catch (Exception ex)
                            {
                                AlifeLog.LogError(ex);
                            }
                        }
                    }
                }

                //装载用户消息。重新生成时消息已在历史里，不能再加一次
                if (addUserMessage)
                {
                    await EditChatHistoryAsync(thread => {
                        thread.ChatHistory.Add(message);
                        ChaseChatHistory(thread);
                        return Task.CompletedTask;
                    }, "装载用户消息");
                }

                //触发发送事件。重新生成时 UI 已有对应气泡，不要再建一条
                if (raiseChatSent)
                {
                    try
                    {
                        ChatSent?.Invoke(message.Content ?? "");
                    }
                    catch (Exception ex)
                    {
                        AlifeLog.LogError(ex);
                    }
                }
            }

            ChatResult? chatResult = null;
            using (ResourceOccupiedReason.Rent("执行对话"))
            {
                await EditChatHistoryAsync(async thread => {
                    if (languageModel == null)
                        throw new Exception("未检测到可用的语言模型");

                    chatResult = await languageModel.ChatStreamingAsync(
                        thread,
                        text => {
                            try
                            {
                                ChatReceived?.Invoke(text);
                            }
                            catch (Exception e)
                            {
                                AlifeLog.LogError(e);
                            }
                        },
                        think => {
                            try
                            {
                                ReasoningReceived?.Invoke(think);
                            }
                            catch (Exception e)
                            {
                                AlifeLog.LogError(e);
                            }
                        },
                        cancellationToken
                    );
                    ChaseChatHistory(thread);
                }, "接收回复");
            }
            if (chatResult == null)
                throw new Exception("执行对话失败，无法获取到结果！");

            using (ResourceOccupiedReason.Rent("完成对话"))
            {
                //对话通讯结束
                RaiseChatOverOnce();

                if (chatResult.Value.Exception != null)
                {
                    try
                    {
                        ChatExceptionThrow?.Invoke(chatResult.Value.Exception);
                    }
                    catch (Exception ex)
                    {
                        AlifeLog.LogError(ex);
                    }
                }

                //对话完全结束
                {
                    ChatContext chatContext = new() {
                        UserMessage = message.Content ?? "",
                        AIMessage = chatResult.Value.AIMessage,
                        CancellationToken = cancellationToken,
                    };

                    if (ChatFinishedAsync != null)
                    {
                        try
                        {
                            await Task.WhenAll(ChatFinishedAsync.GetInvocationList()
                                .Cast<Func<ChatContext, Task>>()
                                .Select(func => func(chatContext)));
                        }
                        catch (Exception e)
                        {
                            AlifeLog.LogError(e);
                        }
                    }

                    try
                    {
                        ChatFinished?.Invoke(chatContext);
                    }
                    catch (Exception ex)
                    {
                        AlifeLog.LogError(ex);
                    }
                }
            }

            return chatResult.Value;
        }
        finally
        {
            //取消/异常/正常三条路径都会走到这里。
            //必须确保 ChatOver 触发一次，否则 UI 的"输入中"气泡永远不消失。
            RaiseChatOverOnce();

            if (observationMessage != null)
            {
                try
                {
                    await EditChatHistoryAsync(thread => {
                        thread.ChatHistory.Remove(observationMessage);
                        return Task.CompletedTask;
                    }, "卸载观察消息");
                }
                catch (Exception e)
                {
                    AlifeLog.LogError(e);
                }
            }
            chatSemaphore.Release();
        }

        void ChaseChatHistory(ChatHistoryAgentThread agentThread)
        {
            for (; lastContentIndex < agentThread.ChatHistory.Count; lastContentIndex++)
            {
                try
                {
                    ChatHistoryAdd?.Invoke(agentThread.ChatHistory[lastContentIndex]);
                }
                catch (Exception e)
                {
                    AlifeLog.LogError(e);
                }
            }
        }
    }
    public Task<ChatResult> ChatAsync(string message, bool breakLast = true)
    {
        return ChatAsync(new ChatMessageContent(AuthorRole.User, message), breakLast);
    }

    // ---------------------------------------------------------------- 对话控制
    // 这四种操作都不碰 UI 消息列表：UI 有自己的一份（ClientChatStatistics），
    // 它通过 ChatOver / ChatSent 等事件自行同步。框架只负责「模型历史」与「在途生成」。

    /// <summary>停止当前生成。已生成的部分保留在历史里。</summary>
    public void Stop()
    {
        try { chatBreakSource.Cancel(); }
        catch { }
    }

    /// <summary>历史被删改后通知记忆类插件重新落盘。异常不能影响主流程。</summary>
    void RaiseHistoryRewritten()
    {
        try { ChatHistoryRewritten?.Invoke(); }
        catch (Exception e) { AlifeLog.LogError(e); }
    }

    /// <summary>
    /// 重新生成：删掉最后一条 AI 回复，用同一条用户消息重新生成。
    /// 返回 false 表示找不到可重生的用户消息（例如还没对话过）。
    /// </summary>
    public async Task<bool> RegenerateAsync(CancellationToken cancellationToken = default)
    {
        ChatMessageContent? user = null;
        bool removed = false;

        await EditChatHistoryAsync(thread => {
            IList<ChatMessageContent> history = thread.ChatHistory;

            //从尾部找最后一条用户消息；它之后的 AI/Tool 消息都是本轮产物
            int lastUser = -1;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Role == AuthorRole.User) { lastUser = i; break; }
            }
            if (lastUser < 0) return Task.CompletedTask;

            user = history[lastUser];
            for (int i = history.Count - 1; i > lastUser; i--)
                history.RemoveAt(i);

            removed = true;
            return Task.CompletedTask;
        }, "重新生成：移除上一轮回复").ConfigureAwait(false);

        //历史被改过，通知记忆重新落盘，别把丢弃的那一版留在记忆里
        RaiseHistoryRewritten();

        if (!removed || user == null) return false;

        //通知 UI：用户气泡不变，只替换最后一条 AI 气泡
        try { ChatRegenerating?.Invoke(user.Content ?? ""); }
        catch (Exception e) { AlifeLog.LogError(e); }

        //复用已在历史里的用户消息：不重复添加、不再触发 ChatSent
        await ChatAsyncCore(user, breakLast: true, addUserMessage: false, raiseChatSent: false, skipInjection: true);
        return true;
    }

    /// <summary>
    /// 修改最后一条用户消息并重新生成。newText 为空时等同 RegenerateAsync。
    /// </summary>
    public async Task<bool> EditAndRegenerateAsync(string newText, CancellationToken cancellationToken = default)
    {
        ChatMessageContent? user = null;
        bool found = false;

        await EditChatHistoryAsync(thread => {
            IList<ChatMessageContent> history = thread.ChatHistory;

            int lastUser = -1;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Role == AuthorRole.User) { lastUser = i; break; }
            }
            if (lastUser < 0) return Task.CompletedTask;

            user = history[lastUser];
            if (string.IsNullOrWhiteSpace(newText) == false)
                user.Content = newText;

            for (int i = history.Count - 1; i > lastUser; i--)
                history.RemoveAt(i);

            found = true;
            return Task.CompletedTask;
        }, "编辑并重发").ConfigureAwait(false);

        RaiseHistoryRewritten();

        if (!found || user == null) return false;

        //通知 UI：更新用户气泡内容并重建 AI 气泡
        try { ChatRegenerating?.Invoke(user.Content ?? ""); }
        catch (Exception e) { AlifeLog.LogError(e); }

        await ChatAsyncCore(user, breakLast: true, addUserMessage: false, raiseChatSent: false, skipInjection: true);
        return true;
    }

    /// <summary>
    /// 回退到本轮发起前：删掉最后一条用户消息及其之后的全部消息。
    /// 返回被删除的消息数，0 表示无可回退。
    /// </summary>
    public async Task<int> RollbackLastTurnAsync()
    {
        int removed = 0;

        await EditChatHistoryAsync(thread => {
            IList<ChatMessageContent> history = thread.ChatHistory;

            int lastUser = -1;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Role == AuthorRole.User) { lastUser = i; break; }
            }
            if (lastUser < 0) return Task.CompletedTask;

            removed = history.Count - lastUser;
            for (int i = history.Count - 1; i >= lastUser; i--)
                history.RemoveAt(i);
            return Task.CompletedTask;
        }, "回退到本轮发起前").ConfigureAwait(false);

        if (removed > 0) RaiseHistoryRewritten();

        return removed;
    }

    /// <summary>
    /// 回退到第 userTurnIndex 轮（0 起算，按用户消息数）。
    /// 删掉那条用户消息及之后的所有消息。UI 每条用户气泡都能挂这个操作。
    /// </summary>
    public async Task<int> RollbackToUserTurnAsync(int userTurnIndex)
    {
        int removed = 0;

        await EditChatHistoryAsync(thread => {
            IList<ChatMessageContent> history = thread.ChatHistory;

            int seen = 0;
            int target = -1;
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Role != AuthorRole.User) continue;
                if (seen++ == userTurnIndex) { target = i; break; }
            }
            if (target < 0) return Task.CompletedTask;

            removed = history.Count - target;
            for (int i = history.Count - 1; i >= target; i--)
                history.RemoveAt(i);
            return Task.CompletedTask;
        }, $"回退到第 {userTurnIndex + 1} 轮").ConfigureAwait(false);

        if (removed > 0) RaiseHistoryRewritten();

        return removed;
    }

    /// <summary>
    /// 修改第 userTurnIndex 轮的用户输入并重发。
    /// 先删掉那条及其后的消息，再用新内容走一次完整对话。
    /// </summary>
    public async Task<bool> EditUserTurnAndRegenerateAsync(int userTurnIndex, string newText)
    {
        ChatMessageContent? user = null;

        await EditChatHistoryAsync(thread => {
            IList<ChatMessageContent> history = thread.ChatHistory;

            int seen = 0;
            int target = -1;
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Role != AuthorRole.User) continue;
                if (seen++ == userTurnIndex) { target = i; break; }
            }
            if (target < 0) return Task.CompletedTask;

            user = history[target];
            user.Content = newText;

            for (int i = history.Count - 1; i >= target + 1; i--)
                history.RemoveAt(i);
            return Task.CompletedTask;
        }, $"编辑第 {userTurnIndex + 1} 轮并重发").ConfigureAwait(false);

        RaiseHistoryRewritten();

        if (user == null) return false;

        //通知 UI 建一条新的"输入中"气泡，否则流式内容没有落点，看起来就是没回复
        try { ChatRegenerating?.Invoke(user.Content ?? ""); }
        catch (Exception e) { AlifeLog.LogError(e); }

        //重发：此时用户消息已在历史里且在最后，不重复添加、不重复触发 ChatSent
        await ChatAsyncCore(user, breakLast: true, addUserMessage: false, raiseChatSent: false, skipInjection: true);
        return true;
    }

    public async void Chat(string content)
    {
        try
        {
            await ChatAsync(content);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }
    public void Poke(string message)
    {
        if (pokeMessageQueue.Any(s => s == message))
            return; //去除重复消息
        if (pokeMessageQueue.Count > 11)
            pokeMessageQueue.TryDequeue(out _); //消息过多，丢失早期消息

        pokeMessageQueue.Enqueue(message);
        lastPokeTime = DateTime.Now; //重新计时，防止后续还有Poke
    }
    public void Observe(string message)
    {
        if (observationMessageQueue.Any(s => s == message))
            return; //去除重复消息
        if (observationMessageQueue.Count > 11)
            observationMessageQueue.TryDequeue(out _); //消息过多，丢失早期消息

        observationMessageQueue.Enqueue(message);
    }

    ILanguageModel? languageModel;
    //上下文
    readonly ChatHistoryAgentThread chatHistoryAgentThread = new();
    readonly SemaphoreSlim chatHistorySemaphore = new(1, 1);
    List<ChatMessageContent> chatHistorySnapshot = new();
    int lastContentIndex;
    //对话
    readonly ConcurrentQueue<string> pokeMessageQueue = new();
    readonly ConcurrentQueue<string> observationMessageQueue = new();
    readonly SemaphoreSlim chatSemaphore = new(1, 1);
    CancellationTokenSource chatBreakSource = new();
    //计时器
    readonly CancellationTokenSource cancelTimerSource = new();
    DateTime lastPokeTime;

    public ChatBot()
    {
        StartPokePusher(0.5f, cancelTimerSource.Token);
    }
    public async ValueTask DisposeAsync()
    {
        await cancelTimerSource.CancelAsync();
        await chatBreakSource.CancelAsync();
        chatSemaphore.Dispose();
        chatBreakSource.Dispose();
        cancelTimerSource.Dispose();
    }

    string? FlushObservationMessage()
    {
        if (observationMessageQueue.Count == 0)
            return null;

        StringBuilder stringBuilder = new();
        foreach (string message in observationMessageQueue)
            stringBuilder.AppendLine(message);
        string observation = stringBuilder.ToString().Trim();
        observationMessageQueue.Clear();
        return $"{ObservationMessageTag}\n{observation}";
    }
    string? FlushPokeMessage()
    {
        if (pokeMessageQueue.Count == 0)
            return null;

        //组合消息
        StringBuilder stringBuilder = new();
        foreach (string message in pokeMessageQueue)
            stringBuilder.AppendLine(message);
        string poke = stringBuilder.ToString().Trim();
        pokeMessageQueue.Clear();

        if (PokeSend != null)
        {
            foreach (Delegate @delegate in PokeSend.GetInvocationList())
            {
                Func<string, string> pokeSend = (Func<string, string>)@delegate;
                poke = pokeSend.Invoke(poke);
            }
        }

        //发送消息
        return $"{PokeMessageTag}\n{poke}";
    }
    async void StartPokePusher(float debounceTime, CancellationToken cancellationToken = default)
    {
        try
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(debounceTime);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!IsChatOccupied && DateTime.Now - lastPokeTime > timeSpan)
                {
                    string? poke = FlushPokeMessage();
                    if (poke != null)
                        Chat(poke);
                }

                await Task.Delay(timeSpan, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}