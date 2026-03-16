using System.Text;
using System.Threading.Channels;

namespace Alife.Interpreter;

/// <summary>
/// XML 执行器：通过订阅 XmlStreamParser 的事件，结合 CompiledHandlerTable 的映射表，
/// 维护标签调用链栈和延迟调用逻辑。
/// </summary>
public class XmlStreamExecutor
{
    readonly XmlStreamParser parser;
    readonly XmlHandlerTable handlerTable;
    readonly List<string> sentenceBreakers;
    readonly int minResultLength;

    // ── 内部缓冲区（Channel） ──
    readonly Channel<char> inputChannel = Channel.CreateUnbounded<char>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = false
    });

    // ── 标签栈 ──
    readonly Stack<TagEntry> tagStack = new();

    // ── 内容缓冲区 ──
    StringBuilder contentBuffer = new();

    sealed class TagEntry
    {
        public required string Name { get; init; }
        public required Dictionary<string, string> Attributes { get; init; }
    }

    public XmlStreamExecutor(XmlStreamParser parser, XmlHandlerTable handlerTable, IEnumerable<string>? sentenceBreakers = null, int minResultLength = 0)
    {
        this.parser = parser;
        this.handlerTable = handlerTable;
        this.minResultLength = minResultLength;
        this.sentenceBreakers = sentenceBreakers != null
            ? sentenceBreakers.ToList()
            : new List<string> { ",", ".", "!", "?", "，", "。", "！", "？" };

        this.parser.OpenTagParsed += OnOpenTagAsync;
        this.parser.CloseTagParsed += OnCloseTagAsync;
        this.parser.TextParsed += OnTextAsync;

        // 启动后台处理循环
        _ = ProcessInputLoopAsync();
    }

    /// <summary>向内部缓冲区输入一个字符（同步）。</summary>
    public void Feed(char ch) => inputChannel.Writer.TryWrite(ch);

    /// <summary>向内部缓冲区输入一个字符串（同步）。</summary>
    public void Feed(string text)
    {
        foreach (char ch in text)
        {
            inputChannel.Writer.TryWrite(ch);
        }
    }

    /// <summary>向解析器输入一个字符（异步版本）。</summary>
    public async Task FeedAsync(char ch)
    {
        await inputChannel.Writer.WriteAsync(ch);
    }

    /// <summary>向解析器输入一个字符串（异步版本）。</summary>
    public async Task FeedAsync(string text)
    {
        foreach (char ch in text)
        {
            await inputChannel.Writer.WriteAsync(ch);
        }
    }

    /// <summary>刷新所有待执行的闭合标签调用（流结束时必须调用）。</summary>
    public void Flush() { }

    /// <summary>重置全部状态以便复用。当输入不完整 XML 导致状态错误时调用此方法恢复。</summary>
    public void Reset()
    {
        // 清空内部通道中的待处理字符
        while (inputChannel.Reader.TryRead(out _)) { }

        parser.Reset();
        tagStack.Clear();
        contentBuffer.Clear();
    }

    private async Task ProcessInputLoopAsync()
    {
        while (await inputChannel.Reader.WaitToReadAsync())
        {
            while (inputChannel.Reader.TryRead(out char ch))
            {
                try
                {
                    await parser.FeedAsync(ch);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }
    }

    // ═══════════════════════════════════════
    //  事件处理
    // ═══════════════════════════════════════

    async Task OnOpenTagAsync(string tagName, IReadOnlyDictionary<string, string> attributes)
    {
        // 遇到新标签时，如果缓冲区有内容，说明这些内容属于它外层的 active 标签
        // 我们应该立刻把它们当做独立的 chunk 触发掉，保证顺序流式处理
        if (contentBuffer.Length > 0 && tagStack.Count > 0)
        {
            await ProcessCurrentStackAsync(null, contentBuffer.ToString(), null, false);
            contentBuffer.Clear();
        }

        tagStack.Push(new TagEntry {
            Name = tagName,
            Attributes = new Dictionary<string, string>(attributes)
        });
    }

    async Task OnCloseTagAsync(string tagName)
    {
        if (TryPopTag(tagName, out TagEntry closedEntry))
        {
            // 当前标签闭合，我们获取它闭合前累积的内容
            string currentTagContent = contentBuffer.ToString();
            contentBuffer.Clear();

            // 执行这个闭合标签，并且让内容往外层（父标签）冒泡
            await ProcessCurrentStackAsync(closedEntry, currentTagContent, null, true);
            return;
        }

        // 栈中无匹配标签，还原为文本内容
        contentBuffer.Append($"</{tagName}>");
    }

    async Task OnTextAsync(char ch)
    {
        contentBuffer.Append(ch);

        // 自动断句功能：检测到指定的断句字符串，且长度满足要求时，提前触发调用链
        if (tagStack.Count > 0)
        {
            string currentText = contentBuffer.ToString();
            foreach (var breaker in sentenceBreakers)
            {
                if (currentText.EndsWith(breaker))
                {
                    // 计算句长（不计入断句符本身）
                    int accumulatedLength = currentText.Length - breaker.Length;
                    if (accumulatedLength >= minResultLength)
                    {
                        await ProcessCurrentStackAsync(null, currentText, breaker, false);
                        contentBuffer.Clear();
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 处理当前标签栈中的所有处理器，从内向外冒泡传递内容执行。
    /// </summary>
    async Task ProcessCurrentStackAsync(TagEntry? closingEntry, string currentChunk, string? trigger, bool isClosing)
    {
        // 构建完整执行链：栈中现有标签 + 刚刚弹出的闭合标签
        var chain = tagStack.Reverse().ToList();
        if (closingEntry != null)
        {
            chain.Add(closingEntry);
        }

        if (chain.Count == 0) return;
        if (string.IsNullOrEmpty(currentChunk) && !isClosing) return;

        XmlTagContext context = new(chain.Select(e => new TagInfo {
            Name = e.Name,
            Attributes = new Dictionary<string, string>(e.Attributes)
        }).ToList(), trigger, isClosing);

        List<Task> tasks = new();

        // 从内向外执行处理器
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            TagEntry entry = chain[i];

            // InvokeHandlerAsync 传入的是 ref currentChunk
            // 内部 handler 对 currentChunk 的修改会直接影响它传递给更外层父标签的内容（即冒泡阶段的数据变化）
            tasks.Add(InvokeHandlerAsync(entry, context, ref currentChunk));
        }

        // 所有的 handler task 返回后，统一 await 确保异步操作顺序完成（例如等待 think 标签中的异步延时）
        await Task.WhenAll(tasks);
    }

    // ═══════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════

    bool TryPopTag(string name, out TagEntry entry)
    {
        if (tagStack.Count > 0 && tagStack.Peek().Name == name)
        {
            entry = tagStack.Pop();
            return true;
        }

        if (tagStack.Any(e => e.Name == name))
        {
            while (tagStack.Count > 0)
            {
                TagEntry popped = tagStack.Pop();
                if (popped.Name == name)
                {
                    entry = popped;
                    return true;
                }
            }
        }

        entry = null!;
        return false;
    }

    Task InvokeHandlerAsync(TagEntry entry, XmlTagContext context, ref string content)
    {
        if (handlerTable.Handlers.TryGetValue(entry.Name.ToLowerInvariant(), out CompiledTagInvoker? invoker))
        {
            return invoker(context, ref content, entry.Attributes);
        }
        return Task.CompletedTask;
    }
}
