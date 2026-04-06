using System.Text;
using System.Threading.Channels;

namespace Alife.Interpreter;

public class XmlStreamExecutor
{
    public void Feed(char ch) => commandChannel.Writer.TryWrite(new StreamCommand(CommandType.Feed, ch));

    public void Feed(string text)
    {
        foreach (char ch in text) Feed(ch);
    }

    public async Task FeedAsync(char ch)
    {
        await commandChannel.Writer.WriteAsync(new StreamCommand(CommandType.Feed, ch));
    }

    public async Task FeedAsync(string text)
    {
        foreach (char ch in text) await FeedAsync(ch);
    }

    public async Task FlushAsync()
    {
        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await commandChannel.Writer.WriteAsync(new StreamCommand(CommandType.Flush, Completion: tcs));
        await tcs.Task;
    }

    public void Flush()
    {
        commandChannel.Writer.TryWrite(new StreamCommand(CommandType.Flush));
    }

    public void Reset()
    {
        while (commandChannel.Reader.TryRead(out StreamCommand cmd))
            cmd.Completion?.TrySetCanceled();

        commandChannel.Writer.TryWrite(new StreamCommand(CommandType.Reset));
    }

    enum CommandType { Feed, Flush, Reset }

    record struct StreamCommand(CommandType Type, char Data = default, TaskCompletionSource? Completion = null);

    sealed class TagEntry
    {
        public required string Name { get; init; }
        public required IReadOnlyDictionary<string, string> Attributes { get; init; }
        public StringBuilder Content { get; } = new();
    }

    readonly XmlStreamParser parser;
    readonly XmlHandlerTable handlerTable;
    readonly List<string> sentenceBreakers;
    readonly int minResultLength;
    readonly Channel<StreamCommand> commandChannel = Channel.CreateUnbounded<StreamCommand>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = false
    });

    readonly Stack<TagEntry> tagStack = new();
    readonly Queue<Func<Task>> eventQueue = new();
    readonly StringBuilder contentBuffer = new();

    public XmlStreamExecutor(XmlStreamParser parser, XmlHandlerTable handlerTable, IEnumerable<string>? sentenceBreakers = null, int minResultLength = 0)
    {
        this.parser = parser;
        this.handlerTable = handlerTable;
        this.minResultLength = minResultLength;
        this.sentenceBreakers = sentenceBreakers?.ToList() ?? [",", ".", "!", "?", "，", "。", "！", "？"];

        this.parser.OpenTagParsed += (name, attrs) => eventQueue.Enqueue(() => OnOpenTagAsync(name, attrs));
        this.parser.ShotTagParsed += (name, attrs) => eventQueue.Enqueue(() => OnSelfClosingTagAsync(name, attrs));
        this.parser.CloseTagParsed += (name) => eventQueue.Enqueue(() => OnCloseTagAsync(name));
        this.parser.ContentParsed += (ch) => eventQueue.Enqueue(() => OnTextAsync(ch.ToString()));

        _ = ProcessInputLoopAsync();
    }

    async Task ProcessInputLoopAsync()
    {
        while (await commandChannel.Reader.WaitToReadAsync())
        {
            while (commandChannel.Reader.TryRead(out StreamCommand cmd))
            {
                try
                {
                    switch (cmd.Type)
                    {
                        case CommandType.Feed:
                            parser.Feed(cmd.Data);
                            await ProcessEventQueueAsync();
                            break;
                        case CommandType.Flush:
                            parser.Flush();
                            await ProcessEventQueueAsync();
                            contentBuffer.Clear();
                            cmd.Completion?.TrySetResult();
                            break;
                        case CommandType.Reset:
                            parser.Reset();
                            eventQueue.Clear();
                            tagStack.Clear();
                            contentBuffer.Clear();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Executor Error]: {ex}");
                    cmd.Completion?.TrySetException(ex);
                }
            }
        }
    }

    async Task ProcessEventQueueAsync()
    {
        while (eventQueue.TryDequeue(out Func<Task>? taskFunc))
        {
            await taskFunc();
        }
    }

    async Task OnOpenTagAsync(string tagName, IReadOnlyDictionary<string, string> attributes)
    {
        if (contentBuffer.Length > 0 && tagStack.Count > 0)
        {
            await ProcessCurrentStackAsync(null, contentBuffer.ToString(), null, CallMode.Content);
            contentBuffer.Clear();
        }

        tagStack.Push(new TagEntry { Name = tagName, Attributes = attributes });
        await ProcessCurrentStackAsync(null, "", null, CallMode.Opening);
    }

    async Task OnSelfClosingTagAsync(string tagName, IReadOnlyDictionary<string, string> attributes)
    {
        if (contentBuffer.Length > 0 && tagStack.Count > 0)
        {
            await ProcessCurrentStackAsync(null, contentBuffer.ToString(), null, CallMode.Content);
            contentBuffer.Clear();
        }

        TagEntry entry = new() { Name = tagName, Attributes = attributes };
        await ProcessCurrentStackAsync(entry, "", null, CallMode.OneShot);
    }

    async Task OnCloseTagAsync(string tagName)
    {
        if (tagStack.Count == 0) return;

        TagEntry entry = tagStack.Pop();
        string lastChunk = contentBuffer.ToString();
        contentBuffer.Clear();

        await ProcessCurrentStackAsync(entry, lastChunk, null, CallMode.Closing);
    }

    async Task OnTextAsync(string text)
    {
        contentBuffer.Append(text);

        if (tagStack.Count > 0)
        {
            string currentText = contentBuffer.ToString();
            foreach (string breaker in sentenceBreakers)
            {
                if (currentText.EndsWith(breaker))
                {
                    int accumulatedLength = currentText.Length - breaker.Length;
                    if (accumulatedLength >= minResultLength)
                    {
                        await ProcessCurrentStackAsync(null, currentText, breaker, CallMode.Content);
                        contentBuffer.Clear();
                        break;
                    }
                }
            }
        }
    }

    async Task ProcessCurrentStackAsync(TagEntry? closingEntry, string currentChunk, string? trigger, CallMode eventStatus)
    {
        List<TagEntry> chain = tagStack.Reverse().ToList();
        if (closingEntry != null)
            chain.Add(closingEntry);

        if (chain.Count == 0) return;
        if (string.IsNullOrEmpty(currentChunk) && eventStatus == CallMode.Content) return;

        string tempChunk = currentChunk;

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            TagEntry entry = chain[i];

            if (i < chain.Count - 1 && string.IsNullOrEmpty(tempChunk)) break;

            CallMode statusForThisTag = (i == chain.Count - 1) ? eventStatus : CallMode.Content;
            string fullContentForThisTag = entry.Content.ToString() + tempChunk;

            XmlTagContext context = new(
                chain.Take(i + 1).Select(e => new TagInfo { Name = e.Name, Attributes = new Dictionary<string, string>(e.Attributes) }).ToList(),
                trigger,
                statusForThisTag,
                fullContentForThisTag,
                tempChunk
            );

            await InvokeHandlerAsync(entry, context, ref tempChunk);
        }

        foreach (TagEntry entry in tagStack)
        {
            entry.Content.Append(tempChunk);
        }
    }

    Task InvokeHandlerAsync(TagEntry entry, XmlTagContext context, ref string content)
    {
        Task namedTask = handlerTable.Handlers.TryGetValue(entry.Name.ToLowerInvariant(), out CompiledTagInvoker? invoker)
            ? invoker(context, ref content, new Dictionary<string, string>(entry.Attributes))
            : Task.CompletedTask;

        if (handlerTable.CatchAllHandlers.Count == 0)
            return namedTask;

        List<Task> tasks = [namedTask];
        foreach (CompiledTagInvoker catchAll in handlerTable.CatchAllHandlers)
            tasks.Add(catchAll(context, ref content, new Dictionary<string, string>(entry.Attributes)));

        return Task.WhenAll(tasks);
    }
}
