using System.Collections.Concurrent;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;


public class ChatBot : IAsyncDisposable
{
    public event Action<string>? ChatSent;
    public event Action<string>? ChatReceived;
    public event Action? ChatOver;
    public event Action<ChatMessageContent>? ChatHistoryAdd;
    public ChatHistory ChatHistory => llmAgentThread.ChatHistory;
    public bool IsChatting => isChatting.CurrentCount == 0;

    public async IAsyncEnumerable<string> ChatStreamingAsync(string message, AuthorRole? role = null)
    {
        if (IsChatting) //打断上一次的聊天
        {
            if (cancelChatSource != null)
                await cancelChatSource.CancelAsync();
        }

        await isChatting.WaitAsync();
        {
            message = $"[发送时间：{DateTime.Now}]{message}";
            llmAgentThread.ChatHistory.AddMessage(role ?? AuthorRole.User, message);
            cancelChatSource = new CancellationTokenSource();

            for (; lastContentIndex < ChatHistory.Count; lastContentIndex++)
                ChatHistoryAdd?.Invoke(ChatHistory[lastContentIndex]);

            ChatSent?.Invoke(message);
            string? error = null;
            var enumerator = llmAgent
                .InvokeStreamingAsync(llmAgentThread, cancellationToken: cancelChatSource.Token)
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

                string? content = enumerator.Current.Message?.Content;
                if (content != null) //只要不是空都要接受，包括空白符，因为会有回车之类的符号
                {
                    yield return content;
                    ChatReceived?.Invoke(content);
                }
            }
            ChatOver?.Invoke();

            for (; lastContentIndex < ChatHistory.Count; lastContentIndex++)
                ChatHistoryAdd?.Invoke(ChatHistory[lastContentIndex]);

            if (error != null)
            {
                llmAgentThread.ChatHistory.AddMessage(AuthorRole.System, error);
                yield return error;
            }
        }
        isChatting.Release();
    }
    public async Task<string> ChatAsync(string message, AuthorRole? role = null)
    {
        StringBuilder stringBuilder = new StringBuilder();
        await foreach (string content in ChatStreamingAsync(message, role))
            stringBuilder.Append(content);
        return stringBuilder.ToString();
    }
    public async void Chat(string content, AuthorRole? role = null)
    {
        try
        {
            await ChatAsync(content, role);
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
        lastAutoFlushTime = 0; //重新计时，防止后续还有Poke
    }
    public bool Flush()
    {
        if (IsChatting)
            return false;

        if (messageCache.Count != 0)
        {
            //组合消息
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("[来自系统的消息缓存](这不是主人的消息，请先用think思考后再看情况回复)");
            foreach (string message in messageCache)
                stringBuilder.AppendLine(message);
            messageCache.Clear();

            //发送消息
            Chat(stringBuilder.ToString());
        }
        return true;
    }

    readonly ChatCompletionAgent llmAgent;
    readonly ChatHistoryAgentThread llmAgentThread;
    readonly ConcurrentQueue<string> messageCache;
    readonly SemaphoreSlim isChatting;
    CancellationTokenSource? cancelChatSource;
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
        isChatting = new SemaphoreSlim(1, 1);

        Update();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Run(() => {
            while (IsChatting)
            {
                while (messageCache.Count != 0)
                    Flush();
            }
        });
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
                    Flush();
                    lastAutoFlushTime = currentTime;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
