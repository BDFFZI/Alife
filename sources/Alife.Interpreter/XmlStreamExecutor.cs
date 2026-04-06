using System.Text;
using System.Threading.Channels;

namespace Alife.Interpreter;

public class XmlStreamExecutor
{
    /// <summary>
    /// 安全区根标签名。设置后只有在该标签内部的内容才会被当作标签处理。
    /// </summary>
    public string? RootTagName { get; set; }

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

    /// <summary>刷新所有待执行的闭合标签调用（流结束时必须调用，调用者会等待执行结束）。</summary>
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

    /// <summary>重置全部状态。会立即清空当前指令队列中所有待处理的任务。</summary>
    public void Reset()
    {
        while (commandChannel.Reader.TryRead(out StreamCommand cmd))
            cmd.Completion?.TrySetCanceled();

        commandChannel.Writer.TryWrite(new StreamCommand(CommandType.Reset));
    }

    enum CommandType { Feed, Flush, Reset }
    record struct StreamCommand(CommandType Type, char Data = default, TaskCompletionSource? Completion = null);

    enum ParsedEventType { OpenTag, CloseTag, SelfClosingTag, Text }
    record struct ParsedEvent(ParsedEventType Type, string? TagName = null, Dictionary<string, string>? Attributes = null, char Char = default);

    sealed class TagEntry
    {
        public required string Name { get; init; }
        public required Dictionary<string, string> Attributes { get; init; }
        public StringBuilder Content { get; } = new();
    }

    readonly OldXmlStreamParser parser;
    readonly XmlHandlerTable handlerTable;
    readonly List<string> sentenceBreakers;
    readonly int minResultLength;
    readonly Channel<StreamCommand> commandChannel = Channel.CreateUnbounded<StreamCommand>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = false
    });
    readonly Stack<TagEntry> tagStack = new();
    readonly List<ParsedEvent> eventBuffer = new();
    StringBuilder contentBuffer = new();
    int rootDepth = 0;

    public XmlStreamExecutor(OldXmlStreamParser parser, XmlHandlerTable handlerTable, IEnumerable<string>? sentenceBreakers = null, int minResultLength = 0)
    {
        this.parser = parser;
        this.handlerTable = handlerTable;
        this.minResultLength = minResultLength;
        this.sentenceBreakers = sentenceBreakers != null
            ? sentenceBreakers.ToList()
            : new List<string> { ",", ".", "!", "?", "，", "。", "！", "？" };

        // 同步事件：仅缓冲，不做异步处理
        this.parser.OpenTagParsed += (name, attrs) =>
            eventBuffer.Add(new ParsedEvent(ParsedEventType.OpenTag, name, new Dictionary<string, string>(attrs)));
        this.parser.ShotTagParsed += (name, attrs) =>
            eventBuffer.Add(new ParsedEvent(ParsedEventType.SelfClosingTag, name, new Dictionary<string, string>(attrs)));
        this.parser.CloseTagParsed += (name) =>
            eventBuffer.Add(new ParsedEvent(ParsedEventType.CloseTag, name));
        this.parser.TextParsed += (ch) =>
            eventBuffer.Add(new ParsedEvent(ParsedEventType.Text, Char: ch));

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
                            await ProcessBufferedEventsAsync();
                            break;
                        case CommandType.Flush:
                            await InternalFlushAsync();
                            cmd.Completion?.TrySetResult();
                            break;
                        case CommandType.Reset:
                            InternalReset();
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

    async Task ProcessBufferedEventsAsync()
    {
        foreach (ParsedEvent evt in eventBuffer)
        {
            switch (evt.Type)
            {
                case ParsedEventType.OpenTag:
                    if (RootTagName != null && evt.TagName!.Equals(RootTagName, StringComparison.OrdinalIgnoreCase))
                    {
                        rootDepth++;
                        break;
                    }
                    if (RootTagName != null && rootDepth <= 0)
                    {
                        await RevertOpenTagToTextAsync(evt);
                        break;
                    }
                    await OnOpenTagAsync(evt.TagName!, evt.Attributes!);
                    break;

                case ParsedEventType.SelfClosingTag:
                    if (RootTagName != null && evt.TagName!.Equals(RootTagName, StringComparison.OrdinalIgnoreCase))
                    {
                        // 根标签是自闭合的通常没有意义，但在计深层面保持0不变即可，忽略触发业务
                        break;
                    }
                    if (RootTagName != null && rootDepth <= 0)
                    {
                        await RevertSelfClosingTagToTextAsync(evt);
                        break;
                    }
                    await OnSelfClosingTagAsync(evt.TagName!, evt.Attributes!);
                    break;

                case ParsedEventType.CloseTag:
                    if (RootTagName != null && evt.TagName!.Equals(RootTagName, StringComparison.OrdinalIgnoreCase))
                    {
                        rootDepth--;
                        break;
                    }
                    if (RootTagName != null && rootDepth <= 0)
                    {
                        await RevertCloseTagToTextAsync(evt.TagName!);
                        break;
                    }
                    await OnCloseTagAsync(); // Parser 取消了 tagName 的比对需求，我们只需闭合顶层
                    break;

                case ParsedEventType.Text:
                    await OnTextAsync(evt.Char);
                    break;
            }
        }
        eventBuffer.Clear();
    }

    async Task RevertOpenTagToTextAsync(ParsedEvent evt)
    {
        await OnTextAsync('<');
        foreach (char c in evt.TagName!) await OnTextAsync(c);
        if (evt.Attributes != null)
        {
            foreach (KeyValuePair<string, string> kv in evt.Attributes)
            {
                await OnTextAsync(' ');
                foreach (char c in kv.Key) await OnTextAsync(c);
                await OnTextAsync('=');
                await OnTextAsync('"');
                foreach (char c in kv.Value) await OnTextAsync(c);
                await OnTextAsync('"');
            }
        }
        await OnTextAsync('>');
    }

    async Task RevertSelfClosingTagToTextAsync(ParsedEvent evt)
    {
        await OnTextAsync('<');
        foreach (char c in evt.TagName!) await OnTextAsync(c);
        if (evt.Attributes != null)
        {
            foreach (KeyValuePair<string, string> kv in evt.Attributes)
            {
                await OnTextAsync(' ');
                foreach (char c in kv.Key) await OnTextAsync(c);
                await OnTextAsync('=');
                await OnTextAsync('"');
                foreach (char c in kv.Value) await OnTextAsync(c);
                await OnTextAsync('"');
            }
        }
        await OnTextAsync(' ');
        await OnTextAsync('/');
        await OnTextAsync('>');
    }

    async Task RevertCloseTagToTextAsync(string tagName)
    {
        await OnTextAsync('<');
        await OnTextAsync('/');
        foreach (char c in tagName) await OnTextAsync(c);
        await OnTextAsync('>');
    }

    async Task InternalFlushAsync()
    {
        parser.Flush();
        await ProcessBufferedEventsAsync();
        
        // 此时由于 parser 已经抛出所有的 Close 事件，Executor 栈本应被完全清空。
        // contentBuffer 在关闭期间已经被全部分发。
        // 但若是根本没有进入任何标签的安全文本，可能还遗留在 contentBuffer 中，它没有可被派发的栈（业务安全忽略）
        contentBuffer.Clear();
    }

    void InternalReset()
    {
        parser.Reset();
        tagStack.Clear();
        contentBuffer.Clear();
        eventBuffer.Clear();
        rootDepth = 0;
    }

    async Task OnOpenTagAsync(string tagName, Dictionary<string, string> attributes)
    {
        if (contentBuffer.Length > 0 && tagStack.Count > 0)
        {
            await ProcessCurrentStackAsync(null, contentBuffer.ToString(), null, TagStatus.Content);
            contentBuffer.Clear();
        }

        TagEntry openEntry = new() {
            Name = tagName,
            Attributes = attributes
        };
        tagStack.Push(openEntry);
        await ProcessCurrentStackAsync(null, "", null, TagStatus.Opening);
    }

    async Task OnSelfClosingTagAsync(string tagName, Dictionary<string, string> attributes)
    {
        if (contentBuffer.Length > 0 && tagStack.Count > 0)
        {
            await ProcessCurrentStackAsync(null, contentBuffer.ToString(), null, TagStatus.Content);
            contentBuffer.Clear();
        }

        TagEntry entry = new() {
            Name = tagName,
            Attributes = attributes
        };
        await ProcessCurrentStackAsync(entry, "", null, TagStatus.OneShot);
    }

    async Task OnCloseTagAsync()
    {
        // 因为 Parser 已经严格保证了 Close 事件和栈结构的一一对应关系
        TagEntry entry = tagStack.Pop();
        string lastChunk = contentBuffer.ToString();
        contentBuffer.Clear();

        await ProcessCurrentStackAsync(entry, lastChunk, null, TagStatus.Closing);
    }

    async Task OnTextAsync(char ch)
    {
        contentBuffer.Append(ch);

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
                        await ProcessCurrentStackAsync(null, currentText, breaker, TagStatus.Content);
                        contentBuffer.Clear();
                        break;
                    }
                }
            }
        }
    }

    async Task ProcessCurrentStackAsync(TagEntry? closingEntry, string currentChunk, string? trigger, TagStatus eventStatus)
    {
        List<TagEntry> chain = tagStack.Reverse().ToList();
        if (closingEntry != null)
            chain.Add(closingEntry);

        if (chain.Count == 0) return;
        if (string.IsNullOrEmpty(currentChunk) && eventStatus == TagStatus.Content) return;

        string tempChunk = currentChunk;

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            TagEntry entry = chain[i];

            if (i < chain.Count - 1 && string.IsNullOrEmpty(tempChunk)) break;

            TagStatus statusForThisTag = (i == chain.Count - 1) ? eventStatus : TagStatus.Content;

            string fullContentForThisTag = entry.Content.ToString() + tempChunk;

            XmlTagContext context = new(
                chain.Take(i + 1).Select(e => new TagInfo {
                    Name = e.Name,
                    Attributes = new Dictionary<string, string>(e.Attributes)
                }).ToList(),
                trigger,
                statusForThisTag,
                fullContentForThisTag,
                tempChunk
            );

            await InvokeHandlerAsync(entry, context, ref tempChunk);
        }

        foreach (TagEntry entry in tagStack)
            entry.Content.Append(tempChunk);
    }

    Task InvokeHandlerAsync(TagEntry entry, XmlTagContext context, ref string content)
    {
        Task namedTask = handlerTable.Handlers.TryGetValue(entry.Name.ToLowerInvariant(), out CompiledTagInvoker? invoker)
            ? invoker(context, ref content, entry.Attributes)
            : Task.CompletedTask;

        if (handlerTable.CatchAllHandlers.Count == 0)
            return namedTask;

        List<Task> tasks = [namedTask];
        foreach (CompiledTagInvoker catchAll in handlerTable.CatchAllHandlers)
            tasks.Add(catchAll(context, ref content, entry.Attributes));

        return Task.WhenAll(tasks);
    }
}
