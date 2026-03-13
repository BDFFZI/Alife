using System.Text;

namespace Alife.Core;

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

    /// <summary>开标签解析完成（标签名, 属性字典）</summary>
    public event Func<string, IReadOnlyDictionary<string, string>, Task>? OpenTagParsed;

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

    public async Task Feed(char ch)
    {
        switch (state)
        {
            case ParserState.Content:         await HandleContent(ch); break;
            case ParserState.TagOpen:          await HandleTagOpen(ch); break;
            case ParserState.ReadTagName:      await HandleReadTagName(ch); break;
            case ParserState.WaitAttrOrClose:  await HandleWaitAttrOrClose(ch); break;
            case ParserState.ReadAttrName:     await HandleReadAttrName(ch); break;
            case ParserState.WaitAttrEquals:   await HandleWaitAttrEquals(ch); break;
            case ParserState.WaitAttrQuote:    await HandleWaitAttrQuote(ch); break;
            case ParserState.ReadAttrValue:    await HandleReadAttrValue(ch); break;
            case ParserState.ReadCloseTag:     await HandleReadCloseTag(ch); break;
        }
    }

    public async Task Feed(string text)
    {
        foreach (char ch in text)
        {
            await Feed(ch);
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
    }

    // ═══════════════════════════════════════
    //  事件发射辅助
    // ═══════════════════════════════════════

    async Task EmitText(char ch)
    {
        if (TextParsed != null) await TextParsed.Invoke(ch);
    }

    async Task EmitText(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++)
        {
            await EmitText(sb[i]);
        }
    }

    async Task EmitText(string s)
    {
        foreach (char c in s)
        {
            await EmitText(c);
        }
    }

    async Task EmitRevertedTagAsText()
    {
        await EmitText('<');
        await EmitText(tagBuffer);
        foreach (KeyValuePair<string, string> kv in currentTagAttrs)
        {
            await EmitText(' '); await EmitText(kv.Key); await EmitText("=\""); await EmitText(kv.Value); await EmitText('"');
        }
        if (attrNameBuffer.Length > 0)
        {
            await EmitText(' '); await EmitText(attrNameBuffer);
        }
    }

    // ═══════════════════════════════════════
    //  状态处理
    // ═══════════════════════════════════════

    async Task HandleContent(char ch)
    {
        if (ch == '<')
        {
            state = ParserState.TagOpen;
            tagBuffer.Clear();
            return;
        }
        await EmitText(ch);
    }

    async Task HandleTagOpen(char ch)
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

        await EmitText('<');
        await EmitText(ch);
        state = ParserState.Content;
    }

    async Task HandleReadTagName(char ch)
    {
        if (ch == '>')
        {
            await EmitOpenTag();
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

        await EmitText('<');
        await EmitText(tagBuffer);
        await EmitText(ch);
        state = ParserState.Content;
    }

    async Task HandleWaitAttrOrClose(char ch)
    {
        if (ch == '>')
        {
            await EmitOpenTag();
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

        await EmitRevertedTagAsText();
        await EmitText(ch);
        state = ParserState.Content;
    }

    async Task HandleReadAttrName(char ch)
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

        await EmitRevertedTagAsText();
        await EmitText(ch);
        state = ParserState.Content;
    }

    async Task HandleWaitAttrEquals(char ch)
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

        await EmitRevertedTagAsText();
        await EmitText(ch);
        state = ParserState.Content;
    }

    async Task HandleWaitAttrQuote(char ch)
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

        await EmitRevertedTagAsText();
        await EmitText(ch);
        state = ParserState.Content;
    }

    async Task HandleReadAttrValue(char ch)
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

    async Task HandleReadCloseTag(char ch)
    {
        if (ch == '>')
        {
            if (CloseTagParsed != null) await CloseTagParsed.Invoke(tagBuffer.ToString());
            state = ParserState.Content;
            return;
        }

        if (IsTagChar(ch))
        {
            tagBuffer.Append(ch);
            return;
        }

        await EmitText("</");
        await EmitText(tagBuffer);
        await EmitText(ch);
        state = ParserState.Content;
    }

    async Task EmitOpenTag()
    {
        string tagName = tagBuffer.ToString();
        if (OpenTagParsed != null) await OpenTagParsed.Invoke(tagName, currentTagAttrs);
        if (isSelfClosing)
        {
            if (CloseTagParsed != null) await CloseTagParsed.Invoke(tagName);
        }
    }

    static bool IsTagChar(char ch) => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.';
    static bool IsAttrChar(char ch) => IsTagChar(ch);
    static bool IsWhitespace(char ch) => ch is ' ' or '\t' or '\r' or '\n';
}
