using System.Collections.Concurrent;
using System.Text;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;


public class ChatBot : IAsyncDisposable
{
    public event Action? ChatStart;
    public event Action? ChatEnd;
    public event Action<string>? ChatHandle;
    public ChatHistory ChatHistory => llmAgentThread.ChatHistory;
    public bool IsChatting => isChatting.CurrentCount == 0;

    public async IAsyncEnumerable<string> ChatStreamingAsync(string message)
    {
        await isChatting.WaitAsync();
        {
            llmAgentThread.ChatHistory.AddMessage(AuthorRole.User, message);

            string? error = null;
            var enumerator = llmAgent.InvokeStreamingAsync(llmAgentThread).GetAsyncEnumerator();

            ChatStart?.Invoke();
            while (true)
            {
                try
                {
                    if (await enumerator.MoveNextAsync() == false)
                        break;
                }
                catch (Exception e)
                {
                    error = e.ToString();
                    break;
                }

                string? content = enumerator.Current.Message?.Content;
                if (string.IsNullOrWhiteSpace(content) == false)
                {
                    yield return content;
                    ChatHandle?.Invoke(content);
                }
            }
            ChatEnd?.Invoke();

            if (error != null)
            {
                llmAgentThread.ChatHistory.AddMessage(AuthorRole.System, error);
                yield return error;
            }
        }
        isChatting.Release();
    }
    public async Task<string> ChatAsync(string message)
    {
        StringBuilder stringBuilder = new StringBuilder();
        await foreach (string content in ChatStreamingAsync(message))
            stringBuilder.Append(content);
        return stringBuilder.ToString();
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
    }
    public bool Flush()
    {
        if (IsChatting)
            return false;

        if (messageCache.Count != 0)
        {
            //组合消息
            StringBuilder stringBuilder = new StringBuilder();
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
    readonly PeriodicTimer periodicTimer;
    readonly SemaphoreSlim isChatting;

    async void Update()
    {
        try
        {
            while (await periodicTimer.WaitForNextTickAsync())
            {
                //定时推送缓存文本
                Flush();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    public ChatBot(ChatCompletionAgent llmAgent, ChatHistoryAgentThread llmAgentThread)
    {
        this.llmAgent = llmAgent;
        this.llmAgentThread = llmAgentThread;
        messageCache = new ConcurrentQueue<string>();
        isChatting = new SemaphoreSlim(1, 1);

        periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        Update();
    }

    public async ValueTask DisposeAsync()
    {
        periodicTimer.Dispose();
        await Task.Run(() => {
            while (Flush() == false) { Thread.Sleep(1000); }
            while (IsChatting) { Thread.Sleep(1000); }
        });
    }
}
