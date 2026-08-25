using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Microsoft.SemanticKernel.Agents;
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
    public const string PokeMessageTag = "[来自系统的杂项消息推送]";

    public event Func<string, string>? PokeSend; //Poke消息过滤
    public event Func<string, string>? ChatSend; //消息过滤

    public event Action<string>? ChatSent; //消息发送后
    public event Action<string>? ChatReceived; //接收到消息
    public event Action<string>? ReasoningReceived; //接收到思考消息
    public event Action? ChatOver; //接收结束

    public event Action<ChatMessageContent>? ChatHistoryAdd; //对话产生的消息块（如果手动插入且不更新结束索引，则也将记录）
    public event Action<Exception>? ChatExceptionThrow; //对话中出现的异常
    public event Action<TokenUsage>? TokenUsed; //对话产生的Token消耗

    public event Action<ChatContext>? ChatFinished; //对话结束
    public event Func<ChatContext, Task>? ChatFinishedAsync; //对话结束(异步)

    public event Action? ChatHistoryEditing; //对话被请求（信号量首次被锁住）
    public event Action? ChatHistoryEdited; //对话已释放（信号量完全解锁）

    public bool IsChatOccupied => chatSemaphore.CurrentCount == 0;
    public bool IsChatHistoryOccupied => chatHistorySemaphore.CurrentCount == 0;
    public OccupationNotepad ResourceOccupiedReason { get; set; } = new();
    public IReadOnlyList<ChatMessageContent> ChatHistory => chatHistorySnapshot;
    public CancellationTokenSource ChatBreakTokenSource => chatBreakSource;
    public ILanguageModel LanguageModel => languageModel;

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

    public async Task<ChatResult> ChatAsync(string message, bool breakLast = true)
    {
        CancellationToken cancellationToken;

        lock (this)
        {
            if (breakLast)
                chatBreakSource.Cancel(); //打断上一次的聊天
            chatBreakSource = new CancellationTokenSource();
            cancellationToken = chatBreakSource.Token;
        }

        await chatSemaphore.WaitAsync(cancellationToken);
        try
        {
            using (ResourceOccupiedReason.Rent("发送消息"))
            {
                //预处理用户消息
                if (ChatSend != null)
                {
                    foreach (Func<string, string> func in ChatSend.GetInvocationList().Cast<Func<string, string>>())
                    {
                        try
                        {
                            message = func(message);
                        }
                        catch (Exception ex)
                        {
                            AlifeLog.LogError(ex);
                        }
                    }
                }
                message = message.Trim();

                //装载用户消息
                await EditChatHistoryAsync(thread => {
                    thread.ChatHistory.AddUserMessage(message);
                    ChaseChatHistory(thread);
                    return Task.CompletedTask;
                }, "装载用户消息");

                //触发发送事件
                try
                {
                    ChatSent?.Invoke(message);
                }
                catch (Exception ex)
                {
                    AlifeLog.LogError(ex);
                }
            }

            Exception? error = null;
            TokenUsage tokenUsage = new();
            string aiMessage = "";
            StringBuilder aiThinking = new();
            //装载AI消息
            await EditChatHistoryAsync(async thread => {
                aiMessage = await languageModel.ChatStreamingAsync(
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
                            aiThinking.Append(think);
                            ReasoningReceived?.Invoke(think);
                        }
                        catch (Exception e)
                        {
                            AlifeLog.LogError(e);
                        }
                    },
                    usage => {
                        tokenUsage += usage;
                    },
                    exception => {
                        if (exception is not OperationCanceledException)
                            error = exception;
                    },
                    cancellationToken
                );
                ChaseChatHistory(thread);
            }, "接收回复");

            using (ResourceOccupiedReason.Rent("分析对话"))
            {
                //对话通讯结束
                try
                {
                    ChatOver?.Invoke();
                }
                catch (Exception e)
                {
                    AlifeLog.LogError(e);
                }

                if (error != null)
                {
                    try
                    {
                        ChatExceptionThrow?.Invoke(error);
                    }
                    catch (Exception ex)
                    {
                        AlifeLog.LogError(ex);
                    }
                }
                try
                {
                    AlifeLog.LogInformation("[ChatBot] " + tokenUsage);
                    TokenUsed?.Invoke(tokenUsage);
                }
                catch (Exception ex)
                {
                    AlifeLog.LogError(ex);
                }

                //对话完全结束
                {
                    ChatContext chatContext = new() {
                        UserMessage = message,
                        AIMessage = aiMessage,
                        CancellationToken = cancellationToken,
                    };

                    try
                    {
                        ChatFinished?.Invoke(chatContext);
                    }
                    catch (Exception ex)
                    {
                        AlifeLog.LogError(ex);
                    }

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

                    return new ChatResult {
                        AIMessage = aiMessage,
                        AIThinking = aiThinking.ToString(),
                        Exception = error
                    };
                }
            }
        }
        finally
        {
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
        if (messageCache.Any(s => s == message))
            return;
        if (messageCache.Count > 11)
            messageCache.TryDequeue(out _);
        
        messageCache.Enqueue(message);
        lastPokeTime = DateTime.Now; //重新计时，防止后续还有Poke
    }


    readonly ILanguageModel languageModel;
    //上下文
    readonly ChatHistoryAgentThread chatHistoryAgentThread = new();
    readonly SemaphoreSlim chatHistorySemaphore = new(1, 1);
    List<ChatMessageContent> chatHistorySnapshot = new();
    int lastContentIndex;
    //对话
    readonly ConcurrentQueue<string> messageCache = new();
    readonly SemaphoreSlim chatSemaphore = new(1, 1);
    CancellationTokenSource chatBreakSource = new();
    //计时器
    readonly CancellationTokenSource cancelTimerSource = new();
    DateTime lastPokeTime;

    public ChatBot(ILanguageModel languageModel)
    {
        this.languageModel = languageModel;

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

    void TryFlushMessageCache()
    {
        if (messageCache.Count == 0)
            return;
        if (IsChatOccupied)
            return;

        //组合消息
        StringBuilder stringBuilder = new();
        foreach (string message in messageCache)
            stringBuilder.AppendLine(message);
        string poke = stringBuilder.ToString().Trim();
        messageCache.Clear();

        if (PokeSend != null)
        {
            foreach (Delegate @delegate in PokeSend.GetInvocationList())
            {
                Func<string, string> pokeSend = (Func<string, string>)@delegate;
                poke = pokeSend.Invoke(poke);
            }
        }

        //发送消息
        Chat($"{PokeMessageTag}\n{poke}");
    }
    async void StartPokePusher(float debounceTime, CancellationToken cancellationToken = default)
    {
        try
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(debounceTime);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (DateTime.Now - lastPokeTime > timeSpan)
                    TryFlushMessageCache();
                await Task.Delay(timeSpan, cancellationToken);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}