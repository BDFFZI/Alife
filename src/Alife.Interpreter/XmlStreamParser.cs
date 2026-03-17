using System.Text;

namespace Alife.Interpreter;

/// <summary>
/// 纯 XML 流式解析器：逐字符解析字符流，通过事件通知使用者解析到的内容。
/// 职责单一 —— 只负责解析，不包含任何业务逻辑。
/// </summary>
public class XmlStreamParser
{
    enum ParserState
    {
        Content, TagOpen, ReadTagName, WaitAttrOrClose,
        ReadAttrName, WaitAttrEquals, WaitAttrQuote, ReadAttrValue,
        ReadCloseTag,
    }

    /// <summary>开标签解析完成（标签名, 属性字典, 是否自闭合）</summary>
    public event Func<string, IReadOnlyDictionary<string, string>, bool, Task>? OpenTagParsed;

    /// <summary>闭标签解析完成（标签名）</summary>
    public event Func<string, Task>? CloseTagParsed;

    /// <summary>文本字符（逐字符实时通知）</summary>
    public event Func<char, Task>? TextParsed;

    // ── 状态机 ──
    ParserState state = ParserState.Content;
    readonly StringBuilder tagBuffer = new();
    readonly StringBuilder attrNameBuffer = new();
    readonly StringBuilder attrValueBuffer = new();
    Dictionary<string, string> currentTagAttrs = new();
    char quoteChar;
    bool isSelfClosing;

    /// <summary>安全区根标签名。如果设置，则只有在该标签内部的内容才会被解析为 XML 标签。</summary>
    public string? RootTagName { get; set; }
    private int rootDepth = 0;

    public async Task FeedAsync(char ch)
    {
        switch (state)
        {
            case ParserState.Content:         await HandleContentAsync(ch); break;
            case ParserState.TagOpen:          await HandleTagOpenAsync(ch); break;
            case ParserState.ReadTagName:      await HandleReadTagNameAsync(ch); break;
            case ParserState.WaitAttrOrClose:  await HandleWaitAttrOrCloseAsync(ch); break;
            case ParserState.ReadAttrName:     await HandleReadAttrNameAsync(ch); break;
            case ParserState.WaitAttrEquals:   await HandleWaitAttrEqualsAsync(ch); break;
            case ParserState.WaitAttrQuote:    await HandleWaitAttrQuoteAsync(ch); break;
            case ParserState.ReadAttrValue:    await HandleReadAttrValueAsync(ch); break;
            case ParserState.ReadCloseTag:     await HandleReadCloseTagAsync(ch); break;
        }
    }

    public async Task FeedAsync(string text)
    {
        foreach (char ch in text)
        {
            await FeedAsync(ch);
        }
    }

    public void Reset()
    {
        state = ParserState.Content;
        tagBuffer.Clear();
        attrNameBuffer.Clear();
        attrValueBuffer.Clear();
        currentTagAttrs.Clear();
        isSelfClosing = false;
        rootDepth = 0;
    }

    // ═══════════════════════════════════════
    //  事件发射辅助
    // ═══════════════════════════════════════

    async Task EmitTextAsync(char ch)
    {
        if (TextParsed != null) await TextParsed.Invoke(ch);
    }

    async Task EmitTextAsync(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++)
        {
            await EmitTextAsync(sb[i]);
        }
    }

    async Task EmitTextAsync(string s)
    {
        foreach (char c in s)
        {
            await EmitTextAsync(c);
        }
    }

    async Task EmitRevertedTagAsTextAsync()
    {
        await EmitTextAsync('<');
        await EmitTextAsync(tagBuffer);
        foreach (KeyValuePair<string, string> kv in currentTagAttrs)
        {
            await EmitTextAsync(' '); await EmitTextAsync(kv.Key); await EmitTextAsync("=\""); await EmitTextAsync(kv.Value); await EmitTextAsync('"');
        }
        if (attrNameBuffer.Length > 0)
        {
            await EmitTextAsync(' '); await EmitTextAsync(attrNameBuffer);
        }
    }

    // ═══════════════════════════════════════
    //  状态处理
    // ═══════════════════════════════════════

    async Task HandleContentAsync(char ch)
    {
        if (ch == '<')
        {
            state = ParserState.TagOpen;
            tagBuffer.Clear();
            return;
        }
        await EmitTextAsync(ch);
    }

    async Task HandleTagOpenAsync(char ch)
    {
        if (ch == '/')
        {
            state = ParserState.ReadCloseTag;
            tagBuffer.Clear();
            return;
        }
        
        if (IsTagChar(ch))
        {
            state = ParserState.ReadTagName;
            tagBuffer.Clear();
            tagBuffer.Append(ch);
            currentTagAttrs = new();
            isSelfClosing = false;
            return;
        }

        await EmitTextAsync('<');
        await EmitTextAsync(ch);
        state = ParserState.Content;
    }

    async Task HandleReadTagNameAsync(char ch)
    {
        if (ch == '>')
        {
            await EmitOpenTagAsync();
            state = ParserState.Content;
            return;
        }

        if (IsWhitespace(ch))
        {
            state = ParserState.WaitAttrOrClose;
            return;
        }

        if (IsTagChar(ch))
        {
            tagBuffer.Append(ch);
            return;
        }

        if (ch == '/')
        {
            isSelfClosing = true;
            state = ParserState.WaitAttrOrClose;
            return;
        }

        await EmitTextAsync('<');
        await EmitTextAsync(tagBuffer);
        await EmitTextAsync(ch);
        state = ParserState.Content;
    }

    async Task HandleWaitAttrOrCloseAsync(char ch)
    {
        if (ch == '>')
        {
            await EmitOpenTagAsync();
            state = ParserState.Content;
            return;
        }

        if (IsWhitespace(ch))
        {
            return;
        }

        if (ch == '/')
        {
            isSelfClosing = true;
            return;
        }

        if (IsAttrChar(ch))
        {
            attrNameBuffer.Clear();
            attrNameBuffer.Append(ch);
            state = ParserState.ReadAttrName;
            return;
        }

        await EmitRevertedTagAsTextAsync();
        await EmitTextAsync(ch);
        state = ParserState.Content;
    }

    async Task HandleReadAttrNameAsync(char ch)
    {
        if (ch == '=')
        {
            state = ParserState.WaitAttrQuote;
            return;
        }

        if (IsWhitespace(ch))
        {
            state = ParserState.WaitAttrEquals;
            return;
        }

        if (IsAttrChar(ch))
        {
            attrNameBuffer.Append(ch);
            return;
        }

        if (ch == '>')
        {
            await EmitOpenTagAsync();
            state = ParserState.Content;
            return;
        }

        if (ch == '/')
        {
            isSelfClosing = true;
            state = ParserState.WaitAttrOrClose;
            return;
        }

        await EmitRevertedTagAsTextAsync();
        await EmitTextAsync(ch);
        state = ParserState.Content;
    }

    async Task HandleWaitAttrEqualsAsync(char ch)
    {
        if (ch == '=')
        {
            state = ParserState.WaitAttrQuote;
            return;
        }

        if (IsWhitespace(ch))
        {
            return;
        }

        if (ch == '>')
        {
            await EmitOpenTagAsync();
            state = ParserState.Content;
            return;
        }

        if (ch == '/')
        {
            isSelfClosing = true;
            state = ParserState.WaitAttrOrClose;
            return;
        }

        await EmitRevertedTagAsTextAsync();
        await EmitTextAsync(ch);
        state = ParserState.Content;
    }

    async Task HandleWaitAttrQuoteAsync(char ch)
    {
        if (ch == '"' || ch == '\'')
        {
            quoteChar = ch;
            attrValueBuffer.Clear();
            state = ParserState.ReadAttrValue;
            return;
        }

        if (IsWhitespace(ch))
        {
            return;
        }

        if (ch == '>')
        {
            await EmitOpenTagAsync();
            state = ParserState.Content;
            return;
        }

        if (ch == '/')
        {
            isSelfClosing = true;
            state = ParserState.WaitAttrOrClose;
            return;
        }

        await EmitRevertedTagAsTextAsync();
        await EmitTextAsync(ch);
        state = ParserState.Content;
    }

    async Task HandleReadAttrValueAsync(char ch)
    {
        if (ch == quoteChar)
        {
            currentTagAttrs[attrNameBuffer.ToString()] = attrValueBuffer.ToString();
            state = ParserState.WaitAttrOrClose;
            return;
        }

        attrValueBuffer.Append(ch);
        await Task.CompletedTask;
    }

    async Task HandleReadCloseTagAsync(char ch)
    {
        if (ch == '>')
        {
            string tagName = tagBuffer.ToString();
            if (RootTagName != null && tagName.Equals(RootTagName, StringComparison.OrdinalIgnoreCase))
            {
                rootDepth--;
            }
            else if (RootTagName == null || rootDepth > 0)
            {
                if (CloseTagParsed != null) await CloseTagParsed.Invoke(tagName);
            }
            state = ParserState.Content;
            return;
        }

        if (IsTagChar(ch))
        {
            tagBuffer.Append(ch);
            return;
        }

        await EmitTextAsync("</");
        await EmitTextAsync(tagBuffer);
        await EmitTextAsync(ch);
        state = ParserState.Content;
    }

    async Task EmitOpenTagAsync()
    {
        string tagName = tagBuffer.ToString();

        // 处理根标签逻辑
        if (RootTagName != null && tagName.Equals(RootTagName, StringComparison.OrdinalIgnoreCase))
        {
            rootDepth++;
            if (isSelfClosing) rootDepth--; // 自闭合根标签瞬开瞬关
            return; // 根标签本身不作为业务标签发射
        }

        // 如果设置了根标签且当前不在根标签内，则将标签还原为文本
        if (RootTagName != null && rootDepth <= 0)
        {
            await EmitRevertedTagAsTextAsync();
            if (isSelfClosing) await EmitTextAsync(" /");
            await EmitTextAsync('>');
            return;
        }

        if (OpenTagParsed != null) await OpenTagParsed.Invoke(tagName, currentTagAttrs, isSelfClosing);
        // [FIX] Self-closing tags should NOT emit CloseTagParsed again, 
        // as the executor already handles the one-shot/pop logic in OnOpenTagAsync.
    }

    static bool IsTagChar(char ch) => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':';
    static bool IsAttrChar(char ch) => IsTagChar(ch);
    static bool IsWhitespace(char ch) => ch is ' ' or '\t' or '\r' or '\n';
}
