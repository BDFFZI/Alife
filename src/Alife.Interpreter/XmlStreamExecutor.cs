using System.Text;

namespace Alife.Interpreter;

/// <summary>
/// XML 执行器：通过订阅 XmlStreamParser 的事件，结合 CompiledHandlerTable 的映射表，
/// 维护标签调用链栈和延迟调用逻辑。
/// </summary>
public class XmlStreamExecutor
{
    readonly XmlStreamParser parser;
    readonly XmlHandlerTable handlerTable;
    readonly HashSet<char> sentenceBreakers;

    // ── 标签栈 ──
    readonly Stack<TagEntry> tagStack = new();

    // ── 内容缓冲区 ──
    StringBuilder contentBuffer = new();

    sealed class TagEntry
    {
        public required string Name { get; init; }
        public required Dictionary<string, string> Attributes { get; init; }
    }

    public XmlStreamExecutor(XmlStreamParser parser, XmlHandlerTable handlerTable, IEnumerable<char>? sentenceBreakers = null)
    {
        this.parser = parser;
        this.handlerTable = handlerTable;
        this.sentenceBreakers = sentenceBreakers != null 
            ? new HashSet<char>(sentenceBreakers) 
            : new HashSet<char> { ',', '.', '!', '?', '，', '。', '！', '？' };

        this.parser.OpenTagParsed += OnOpenTag;
        this.parser.CloseTagParsed += OnCloseTag;
        this.parser.TextParsed += OnText;
    }

    /// <summary>向解析器输入一个字符。</summary>
    public async Task Feed(char ch) => await parser.Feed(ch);

    /// <summary>向解析器输入一个字符串。</summary>
    public async Task Feed(string text) => await parser.Feed(text);

    /// <summary>刷新所有待执行的闭合标签调用（流结束时必须调用）。</summary>
    public void Flush() { }

    /// <summary>重置全部状态以便复用。</summary>
    public void Reset()
    {
        parser.Reset();
        tagStack.Clear();
        contentBuffer.Clear();
    }

    // ═══════════════════════════════════════
    //  事件处理
    // ═══════════════════════════════════════

    async Task OnOpenTag(string tagName, IReadOnlyDictionary<string, string> attributes)
    {
        // 遇到新标签时，如果缓冲区有内容，说明这些内容属于它外层的 active 标签
        // 我们应该立刻把它们当做独立的 chunk 触发掉，保证顺序流式处理
        if (contentBuffer.Length > 0 && tagStack.Count > 0)
        {
            await ProcessCurrentStackAsync(null, contentBuffer.ToString(), null);
            contentBuffer.Clear();
        }

        tagStack.Push(new TagEntry
        {
            Name = tagName,
            Attributes = new Dictionary<string, string>(attributes)
        });
        await Task.CompletedTask;
    }

    async Task OnCloseTag(string tagName)
    {
        if (TryPopTag(tagName, out TagEntry closedEntry))
        {
            // 当前标签闭合，我们获取它闭合前累积的内容
            string currentTagContent = contentBuffer.ToString();
            contentBuffer.Clear();

            // 执行这个闭合标签，并且让内容往外层（父标签）冒泡
            await ProcessCurrentStackAsync(closedEntry, currentTagContent, null);
            return;
        }

        // 栈中无匹配标签，还原为文本内容
        contentBuffer.Append($"</{tagName}>");
        await Task.CompletedTask;
    }

    async Task OnText(char ch)
    {
        contentBuffer.Append(ch);

        // 自动断句功能：识别到特定标点符号时，提前触发调用链
        if (tagStack.Count > 0 && sentenceBreakers.Contains(ch))
        {
            await ProcessCurrentStackAsync(null, contentBuffer.ToString(), ch);
            contentBuffer.Clear();
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理当前标签栈中的所有处理器，从内向外冒泡传递内容执行。
    /// </summary>
    async Task ProcessCurrentStackAsync(TagEntry? closingEntry, string currentChunk, char? trigger)
    {
        // 构建完整执行链：栈中现有标签 + 刚刚弹出的闭合标签
        var chain = tagStack.Reverse().ToList();
        if (closingEntry != null)
        {
            chain.Add(closingEntry);
        }

        if (chain.Count == 0 || string.IsNullOrEmpty(currentChunk)) return;

        XmlTagContext context = new(chain.Select(e => new TagInfo
        {
            Name = e.Name,
            Attributes = new Dictionary<string, string>(e.Attributes)
        }).ToList(), trigger);

        List<Task> tasks = new();

        // 从内向外执行处理器
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            TagEntry entry = chain[i];
            
            // InvokeHandler 传入的是 ref currentChunk
            // 内部 handler 对 currentChunk 的修改会直接影响它传递给更外层父标签的内容（即冒泡阶段的数据变化）
            tasks.Add(InvokeHandler(entry, context, ref currentChunk));
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

    Task InvokeHandler(TagEntry entry, XmlTagContext context, ref string content)
    {
        if (handlerTable.Handlers.TryGetValue(entry.Name, out CompiledTagInvoker? invoker))
        {
            return invoker(context, ref content, entry.Attributes);
        }
        return Task.CompletedTask;
    }
}
