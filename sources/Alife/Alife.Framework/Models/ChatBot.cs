using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI.Chat;
using ChatMessageContent=Microsoft.SemanticKernel.ChatMessageContent;

namespace Alife.Framework;

public class ChatBot : IAsyncDisposable
{
    public const string ThinkContentPrefix = "__THINK__";
    public const string PokeMessageTag = "[来自系统的杂项消息推送]";

    public event Func<string, string>? PokeSend;//Poke消息过滤
    public event Func<string, string>? ChatSend;//消息过滤
    public event Action<string>? ChatSent;//消息发送前
    public event Action<ChatInputSentEventArgs>? ChatInputSent;
    public event Action<string>? ChatReceived;//消息接收到
    public event Action<string>? ReasoningReceived;//思考消息接收到
    public event Action? ChatOver;//消息结束

    public event Action<ChatMessageContent>? ChatHistoryAdd;
    public event Action<ChatTokenUsage>? TokenUsed;
    public ChatHistory ChatHistory => llmAgentThread.ChatHistory;
    public bool IsChatting => chatSemaphore.CurrentCount == 0;
    public CancellationTokenSource ChatBreakTokenSource => chatBreakSource;

    public async Task RequestChatAsync(CancellationToken cancellationToken = default)
    {
        await chatSemaphore.WaitAsync(cancellationToken);
    }

    public void ReleaseChat()
    {
        chatSemaphore.Release();
    }

    public async IAsyncEnumerable<string> ChatStreamingAsync(string message, AuthorRole? role = null, ChatInputVisibility visibility = ChatInputVisibility.Visible)
    {
        if (IsChatting)//打断上一次的聊天
        {
            await chatBreakSource.CancelAsync();
        }

        await RequestChatAsync();
        try
        {
            chatBreakSource = new CancellationTokenSource();

            if (ChatSend != null)
            {
                foreach (Delegate @delegate in ChatSend.GetInvocationList())
                {
                    Func<string, string> chatSend = (Func<string, string>)@delegate;
                    message = chatSend.Invoke(message);
                }
            }

            message = message.Trim();
            llmAgentThread.ChatHistory.AddMessage(role ?? AuthorRole.User, message);

            ChaseChatHistory();

            ChatSent?.Invoke(message);
            ChatInputSent?.Invoke(new ChatInputSentEventArgs(message, visibility));
            string? error = null;
            StringBuilder cleanResponseBuilder = new();// 用于存储不含思考过程的最终回复

            await using IAsyncEnumerator<AgentResponseItem<StreamingChatMessageContent>> enumerator = llmAgent
                .InvokeStreamingAsync(llmAgentThread, cancellationToken: chatBreakSource.Token)
                .GetAsyncEnumerator();
            while (true)
            {
                try
                {
                    if (await enumerator.MoveNextAsync() == false)
                        break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    error = e.ToString();
                    break;
                }

                string? content = enumerator.Current.Message.Content;
                if (content != null)
                {
                    //前置报文会对思考内容进行特殊处理，以便兼容思考模式
                    if (content.StartsWith(ThinkContentPrefix))
                    {
                        string reasoningPart = content.Substring(ThinkContentPrefix.Length);
                        if (!string.IsNullOrEmpty(reasoningPart))
                        {
                            ReasoningReceived?.Invoke(reasoningPart);
                        }
                    }
                    else
                    {
                        yield return content;
                        ChatReceived?.Invoke(content);
                        cleanResponseBuilder.Append(content);
                    }
                }

                var metaData = enumerator.Current.Message.Metadata;
                if (metaData != null)
                {
                    // 尝试从元数据中提取思考过程 (支持原生支持此字段的 SDK)
                    if (metaData.TryGetValue("ReasoningContent", out object? reasoning) ||
                        metaData.TryGetValue("reasoning_content", out reasoning))
                    {
                        string? reasoningStr = reasoning?.ToString();
                        if (!string.IsNullOrEmpty(reasoningStr))
                        {
                            ReasoningReceived?.Invoke(reasoningStr);
                        }
                    }

                    if (metaData.TryGetValue("Usage", out object? usage))
                    {
                        if (usage is ChatTokenUsage chatTokenUsage)
                        {
                            Console.WriteLine("[ChatBot]" + KernelPrinter.ToTokenLog(metaData));
                            TokenUsed?.Invoke(chatTokenUsage);
                        }
                    }
                }
            }

            // 在同步历史记录前，清洗掉可能存入 ChatHistory 的思考内容（防止污染上下文）
            if (llmAgentThread.ChatHistory.Count > 0)
            {
                ChatMessageContent lastMsg = llmAgentThread.ChatHistory[^1];
                if (lastMsg.Role == AuthorRole.Assistant && (lastMsg.Content?.Contains(ThinkContentPrefix) ?? false))
                    lastMsg.Content = cleanResponseBuilder.ToString();
            }

            ChatOver?.Invoke();

            ChaseChatHistory();

            if (error != null)
            {
                llmAgentThread.ChatHistory.AddMessage(AuthorRole.System, error);
                yield return error;
            }
        }
        finally
        {
            ReleaseChat();
        }
    }

    public async Task<string> ChatAsync(string message, AuthorRole? role = null, ChatInputVisibility visibility = ChatInputVisibility.Visible)
    {
        StringBuilder stringBuilder = new StringBuilder();
        await foreach (string content in ChatStreamingAsync(message, role, visibility))
            stringBuilder.Append(content);
        return stringBuilder.ToString();
    }

    public async void Chat(string content, AuthorRole? role = null, ChatInputVisibility visibility = ChatInputVisibility.Visible)
    {
        try
        {
            await ChatAsync(content, role, visibility);
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
        messageCache.Enqueue($"{message}\n");
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

    readonly ChatCompletionAgent llmAgent;
    readonly ChatHistoryAgentThread llmAgentThread;
    readonly ConcurrentQueue<string> messageCache;
    readonly SemaphoreSlim chatSemaphore;
    CancellationTokenSource chatBreakSource = new();

    int lastContentIndex;

    //计时器
    CancellationTokenSource? cancelTimerSource;
    int currentTime;
    int lastAutoFlushTime;
    const int DeltaTime = 1;


    public ChatBot(ChatCompletionAgent llmAgent, ChatHistoryAgentThread llmAgentThread)
    {
        this.llmAgent = llmAgent;
        this.llmAgentThread = llmAgentThread;
        messageCache = new ConcurrentQueue<string>();
        chatSemaphore = new SemaphoreSlim(1, 1);

        Update();
    }

    public async ValueTask DisposeAsync()
    {
        if (cancelTimerSource != null)
            await cancelTimerSource.CancelAsync();

        while (IsChatting || !messageCache.IsEmpty)
        {
            await TryFlushMessageCache();

            // 在WPF中awaiter的实现是推送给UI线程（主线程）执行任务，而不是线程池。
            // 这导致很多继体需要在同一个线程中处理。如果其中一个卡死，那后面就无法执行，因此存在死锁的风险。
            // 比如XmlFunctionCall在每次发消息都会申请锁，因此IsChatting始终为true，结果为ture时该代码成了死循环，卡死了主线程
            // 结果XmlFunctionCall用于释放的续体正好也被推送到主线程的队尾，然而他永远等不到释放的机会。
            // 互相等待对方，但永远等不到，于是构成了死锁。
            // 利用 Task.Yield，则可以释放线程，将自己重新插入到队尾，于是 Xml 就可以释放他的信号量了。
            await Task.Yield();
        }
    }

    async void Update()
    {
        try
        {
            cancelTimerSource = new CancellationTokenSource();
            PeriodicTimer periodicTimer = new(TimeSpan.FromSeconds(DeltaTime));
            while (await periodicTimer.WaitForNextTickAsync(cancelTimerSource.Token))
            {
                currentTime += DeltaTime;
                if (currentTime - lastAutoFlushTime > 2)
                {
                    await TryFlushMessageCache(cancelTimerSource.Token);
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

    async Task TryFlushMessageCache(CancellationToken cancellationToken = default)
    {
        if (messageCache.Count == 0)
            return;

        await RequestChatAsync(cancellationToken);
        try
        {
            //组合消息
            StringBuilder stringBuilder = new();
            foreach (string message in messageCache)
                stringBuilder.AppendLine(message);
            string poke = stringBuilder.ToString();
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
            Chat($"{PokeMessageTag}\n{poke}", visibility: ChatInputVisibility.Internal);
        }
        finally
        {
            ReleaseChat();
        }
    }

    void ChaseChatHistory()
    {
        for (; lastContentIndex < ChatHistory.Count; lastContentIndex++)
            ChatHistoryAdd?.Invoke(ChatHistory[lastContentIndex]);
    }
}
