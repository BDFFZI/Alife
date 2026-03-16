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
        public StringBuilder Content { get; } = new();
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
    public async Task FlushAsync()
    {
        if (contentBuffer.Length > 0 && tagStack.Count > 0)
        {
            await ProcessCurrentStackAsync(null, contentBuffer.ToString(), null, TagStatus.Content);
            contentBuffer.Clear();
        }

        while (tagStack.Count > 0)
        {
            TagEntry entry = tagStack.Pop();
            await ProcessCurrentStackAsync(entry, "", null, TagStatus.Closing);
        }
    }

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

    async Task OnOpenTagAsync(string tagName, IReadOnlyDictionary<string, string> attributes, bool isSelfClosing)
    {
        // 遇到新标签时，如果缓冲区有内容，说明这些内容属于它外层的 active 标签
        if (contentBuffer.Length > 0)
        {
            if (tagStack.Count > 0)
            {
                await ProcessCurrentStackAsync(null, contentBuffer.ToString(), null, TagStatus.Content);
            }
            contentBuffer.Clear(); // 无论是否有外层，新标签开启时都应清空缓冲区，防止“你好<say>”中的“你好”流进 say 中
        }

        var entry = new TagEntry {
            Name = tagName,
            Attributes = new Dictionary<string, string>(attributes)
        };
        tagStack.Push(entry);

        if (isSelfClosing)
        {
            await ProcessCurrentStackAsync(entry, "", null, TagStatus.OneShot);
            tagStack.Pop(); // 弹出
            return;
        }

        // [Specification 1] 开区间必定执行一次
        await ProcessCurrentStackAsync(null, "", null, TagStatus.Opening);
    }

    async Task OnCloseTagAsync(string tagName)
    {
        // 查找栈中是否有匹配的标签
        if (tagStack.Any(e => e.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
        {
            // 自动闭合所有嵌套在目标标签内部的其它未闭合标签
            while (tagStack.Count > 0)
            {
                TagEntry entry = tagStack.Pop();
                string lastChunk = contentBuffer.ToString();
                contentBuffer.Clear();

                await ProcessCurrentStackAsync(entry, lastChunk, null, TagStatus.Closing);

                if (entry.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))
                    break;
            }
            return;
        }

        // 栈中无匹配标签（孤儿闭合标签）：触发一次 OneShot
        var orphanEntry = new TagEntry { Name = tagName, Attributes = new() };
        await ProcessCurrentStackAsync(orphanEntry, "", null, TagStatus.OneShot);
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
                        // [Specification 2] 有内容时根据分词情况可能调用若干次
                        await ProcessCurrentStackAsync(null, currentText, breaker, TagStatus.Content);
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
    async Task ProcessCurrentStackAsync(TagEntry? closingEntry, string currentChunk, string? trigger, TagStatus eventStatus)
    {
        // 构建完整执行链：栈中现有标签 + 刚刚弹出的闭合标签
        var chain = tagStack.Reverse().ToList();
        if (closingEntry != null)
        {
            chain.Add(closingEntry);
        }

        if (chain.Count == 0) return;
        
        // 如果既不是开也不是关，且没内容，则不触发
        if (string.IsNullOrEmpty(currentChunk) && eventStatus == TagStatus.Content) return;

        string tempChunk = currentChunk;

        // [Specification 2] 从内向外顺序执行处理器，支持中间修改内容并冒泡
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            TagEntry entry = chain[i];

            // 如果内容已被之前的处理器拦截完了，且当前不是该事件的目标标签（目标标签即使内容为空也要触发 Opening/Closing）
            if (i < chain.Count - 1 && string.IsNullOrEmpty(tempChunk)) break;

            // 只有最内层才是真正的 Opening/Closing
            TagStatus statusForThisTag = (i == chain.Count - 1) ? eventStatus : TagStatus.Content;
            
            // FullContent = 之前存的 + 本次处理的起点
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

            // 顺序执行，以便支持 ref 修改内容
            await InvokeHandlerAsync(entry, context, ref tempChunk);
        }

        // [Specification 3] 运行完后，将最终（可能被修改过）的文本追加到还在栈中的每个标签自己的完整串中
        foreach (var entry in tagStack)
        {
            entry.Content.Append(tempChunk);
        }
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
