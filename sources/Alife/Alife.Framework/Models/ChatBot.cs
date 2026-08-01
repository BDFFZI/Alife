using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using ChatMessageContent=Microsoft.SemanticKernel.ChatMessageContent;

namespace Alife.Framework;

public struct ChatContext
{
    public string UserMessage { get; init; }
    public string AIMessage { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public class ChatBot : IAsyncDisposable
{
    public const string PokeMessageTag = "[来自系统的杂项消息推送]";

    public event Func<string, string>? PokeSend;//Poke消息过滤
    public event Func<string, string>? ChatSend;//消息过滤

    public event Action? ChatRequesting;//对话被请求（信号量首次被锁住）
    public event Action? ChatReleased;//对话已释放（信号量完全解锁）

    public event Action<string>? ChatSent;//消息发送后
    public event Action<string>? ChatReceived;//接收到消息
    public event Action<string>? ReasoningReceived;//接收到思考消息
    public event Action? ChatOver;//接收结束

    public event Action<ChatMessageContent>? ChatHistoryAdd;//对话产生的消息块（如果手动插入且不更新结束索引，则也将记录）
    public event Action<Exception>? ChatExceptionThrow;//对话中出现的异常
    public event Action<TokenUsage>? TokenUsed;//对话产生的Token消耗

    public event Action<ChatContext>? ChatFinished;//对话结束
    public event Func<ChatContext, Task>? ChatFinishedAsync;//对话结束(异步)

    public ChatHistoryAgentThread ChatHistoryAgentThread => chatHistoryAgentThread;
    public ChatHistory ChatHistory => chatHistoryAgentThread.ChatHistory;
    public bool IsChatting => chatRequestCount != 0;
    public string? ChatOccupiedReason { get; set; }//当前llm被占用的利用描述
    public CancellationTokenSource ChatBreakTokenSource => chatBreakSource;

    public async Task RequestChatAsync(CancellationToken cancellationToken = default, string? reason = null)
    {
        if (Interlocked.Increment(ref chatRequestCount) == 1)
            ChatRequesting?.Invoke();
        await chatSemaphore.WaitAsync(cancellationToken);
        ChatOccupiedReason = reason;
    }
    public void ReleaseChat()
    {
        ChatOccupiedReason = null;
        chatSemaphore.Release();
        if (Interlocked.Decrement(ref chatRequestCount) == 0)
            ChatReleased?.Invoke();
    }

    public async Task<string> ChatAsync(string message)
    {
        CancellationToken cancellationToken;

        lock (this)
        {
            if (IsChatting)//打断上一次的聊天
                chatBreakSource.Cancel();
            chatBreakSource = new CancellationTokenSource();
            cancellationToken = chatBreakSource.Token;
        }

        await RequestChatAsync(cancellationToken);
        try
        {
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
            chatHistoryAgentThread.ChatHistory.AddUserMessage(message);

            //触发用户消息块增加
            ChaseChatHistory();
            try
            {
                ChatSent?.Invoke(message);
            }
            catch (Exception ex)
            {
                AlifeLog.LogError(ex);
            }

            //进行实际对话
            Exception? error = null;
            TokenUsage tokenUsage = new();
            string aiMessage = await languageModel.ChatStreamingAsync(
                chatHistoryAgentThread,
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
                usage => {
                    tokenUsage += usage;
                },
                exception => {
                    if (exception is not OperationCanceledException)
                        error = exception;
                },
                cancellationToken
            );

            //触发ai消息块增加
            ChaseChatHistory();
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
                    CancellationToken = cancellationToken
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

                return aiMessage;
            }
        }
        finally
        {
            ReleaseChat();
        }
    }
    public async void Chat(string content)
    {
        try
        {
            await ChatAsync(content);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    public void Poke(string message)
    {
        while (messageCache.Count > 11)
            messageCache.TryDequeue(out _);
        messageCache.Enqueue(message);
        lastAutoFlushTime = 0;//重新计时，防止后续还有Poke
    }
    public async Task ImplicitChatAsync(string message)
    {
        await RequestChatAsync();
        ChatHistory.AddUserMessage(message);
        ReleaseChat();
    }

    public void UpdateHistoryEndIndex()
    {
        lastContentIndex = ChatHistory.Count;
    }

    readonly ILanguageModel languageModel;
    readonly ChatHistoryAgentThread chatHistoryAgentThread = new();
    readonly ConcurrentQueue<string> messageCache = new();
    readonly SemaphoreSlim chatSemaphore = new(1, 1);
    CancellationTokenSource chatBreakSource = new();
    int chatRequestCount;
    int lastContentIndex;

    //计时器
    readonly CancellationTokenSource cancelTimerSource = new();
    int currentTime;
    int lastAutoFlushTime;

    public ChatBot(ILanguageModel languageModel)
    {
        this.languageModel = languageModel;

        StartTimer(1, cancelTimerSource.Token);
    }
    public async ValueTask DisposeAsync()
    {
        await cancelTimerSource.CancelAsync();
        await chatBreakSource.CancelAsync();
        chatSemaphore.Dispose();
        chatBreakSource.Dispose();
        cancelTimerSource.Dispose();
    }

    async Task TryFlushMessageCache(CancellationToken cancellationToken = default)
    {
        if (messageCache.Count == 0)
            return;
        if (IsChatting)
            return;

        await RequestChatAsync(cancellationToken);
        try
        {
            //组合消息
            StringBuilder stringBuilder = new();
            foreach (string message in messageCache.Distinct())
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
        finally
        {
            ReleaseChat();
        }
    }
    async void StartTimer(int expectedDeltaTime, CancellationToken cancellationToken = default)
    {
        try
        {
            PeriodicTimer periodicTimer = new(TimeSpan.FromSeconds(expectedDeltaTime));
            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                currentTime += expectedDeltaTime;
                if (currentTime - lastAutoFlushTime > 2)
                {
                    await TryFlushMessageCache(cancellationToken);
                    lastAutoFlushTime = currentTime;
                }
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    void ChaseChatHistory()
    {
        for (; lastContentIndex < ChatHistory.Count; lastContentIndex++)
        {
            try
            {
                ChatHistoryAdd?.Invoke(ChatHistory[lastContentIndex]);
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }
}
