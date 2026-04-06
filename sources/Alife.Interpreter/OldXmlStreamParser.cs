using System.Text;

namespace Alife.Interpreter;

public class OldXmlStreamParser
{
    public event Action<string, IReadOnlyDictionary<string, string>>? OpenTagParsed;
    public event Action<string>? CloseTagParsed;
    public event Action<string, IReadOnlyDictionary<string, string>>? ShotTagParsed;
    public event Action<char>? TextParsed;

    public void Feed(char ch)
    {
        if (state != ParserState.Content)
        {
            rawBuffer.Append(ch);
        }

        switch (state)
        {
            case ParserState.Content: HandleContent(ch); break;
            case ParserState.Entity: HandleEntity(ch); break;
            case ParserState.TagOpen: HandleTagOpen(ch); break;
            case ParserState.ReadTagName: HandleReadTagName(ch); break;
            case ParserState.WaitAttrOrClose: HandleWaitAttrOrClose(ch); break;
            case ParserState.ReadAttrName: HandleReadAttrName(ch); break;
            case ParserState.WaitAttrEquals: HandleWaitAttrEquals(ch); break;
            case ParserState.WaitAttrQuote: HandleWaitAttrQuote(ch); break;
            case ParserState.ReadAttrValue: HandleReadAttrValue(ch); break;
            case ParserState.ReadCloseTag: HandleReadCloseTag(ch); break;
        }
    }

    public void Feed(string text)
    {
        foreach (char ch in text)
            Feed(ch);
    }

    public void Flush()
    {
        if (state != ParserState.Content)
        {
            EmitText(rawBuffer);
        }

        while (tagStack.Count > 0)
        {
            CloseTagParsed?.Invoke(tagStack.Pop());
        }

        ResetInner();
    }

    public void Reset()
    {
        ResetInner();
        tagStack.Clear();
    }

    void ResetInner()
    {
        rawBuffer.Clear();
        tagBuffer.Clear();
        attrNameBuffer.Clear();
        attrValueBuffer.Clear();
        currentTagAttrs.Clear();
        entityBuffer.Clear();
        isSelfClosing = false;
        state = ParserState.Content;
    }

    /// <summary>
    /// 当违反任何结构限制时，直接将积累的 rawBuffer 退化为普通文本，瞬间完成状态恢复。
    /// </summary>
    void AbortAndRevert(bool retryCurrentChar)
    {
        if (retryCurrentChar)
        {
            // 这是由于当前字符导致的异常，先把这个字符剔除出 rawBuffer
            char offendingChar = rawBuffer[^1];
            rawBuffer.Length--;

            EmitText(rawBuffer);
            ParserState prevReturnState = returnState;
            ResetInner();

            // 如果异常发生在标签内部，我们应该退回到 Content 状态重试当前字符
            // 如果异常发生在包裹着 Entity 的 Content 状态中，也是退回到 Content
            if (prevReturnState == ParserState.ReadAttrValue)
            {
                // 注意：如果是在标签的属性值内发生的 Entity 解析异常，我们不能仅回退 Entity，我们必须摧毁整个不合法的标签！
                // 因为 XML 规定属性值中的实体一旦不合法，整个标签便破损。
            }
            Feed(offendingChar);
        }
        else
        {
            EmitText(rawBuffer);
            ResetInner();
        }
    }

    enum ParserState
    {
        /// <summary>默认状态：普通文本内容。</summary>
        Content,
        /// <summary>实体转义：遇到 `&amp;` 进入，等待 `;` 结束并匹配实体。</summary>
        Entity,
        /// <summary>标签起始：刚遇到 `&lt;`，等待下一字符判断是开/闭标签还是普通符号。</summary>
        TagOpen,
        /// <summary>读取标签名：如 `&lt;speak` 正在拼接 `speak`。</summary>
        ReadTagName,
        /// <summary>等待属性或闭合：标签名读完，等空格、等 `&gt;` 或等 `/`。</summary>
        WaitAttrOrClose,
        /// <summary>读取属性名：如正拼接 `mode`。</summary>
        ReadAttrName,
        /// <summary>等待等号：属性名后若紧跟空格，处于此状态等待 `=`。</summary>
        WaitAttrEquals,
        /// <summary>等待引号：读完 `=` 后，等待 `"` 或 `'` 包裹属性值。</summary>
        WaitAttrQuote,
        /// <summary>读取属性值：已被引号包裹，正在拼接内容。</summary>
        ReadAttrValue,
        /// <summary>读取闭合标签名：遇到 `&lt;/` 后拼接名字。</summary>
        ReadCloseTag,
    }

    ParserState state = ParserState.Content;
    readonly StringBuilder rawBuffer = new();
    readonly StringBuilder tagBuffer = new();
    readonly StringBuilder attrNameBuffer = new();
    readonly StringBuilder attrValueBuffer = new();
    readonly StringBuilder entityBuffer = new();
    readonly Stack<string> tagStack = new();
    Dictionary<string, string> currentTagAttrs = new();
    char quoteChar;
    bool isSelfClosing;
    ParserState returnState;

    void EmitText(char ch) => TextParsed?.Invoke(ch);
    void EmitText(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++) EmitText(sb[i]);
    }

    void HandleContent(char ch)
    {
        if (ch == '&')
        {
            rawBuffer.Append(ch);
            returnState = ParserState.Content;
            state = ParserState.Entity;
            return;
        }

        if (ch == '<')
        {
            rawBuffer.Append(ch);
            state = ParserState.TagOpen;
            return;
        }

        EmitText(ch);
    }

    void HandleEntity(char ch)
    {
        if (ch == ';')
        {
            string entity = entityBuffer.ToString();
            entityBuffer.Clear();

            char? resolved = ResolveEntity(entity);
            if (resolved != null)
            {
                if (returnState == ParserState.Content)
                {
                    EmitText(resolved.Value);
                    ResetInner(); // 实体解析成功，清空 rawBuffer
                }
                else
                {
                    attrValueBuffer.Append(resolved.Value);
                    state = returnState; // 回到属性值读取，不抛弃整体 rawBuffer
                }
            }
            else
            {
                // 无效实体（如 &invalid;），破坏了结构
                AbortAndRevert(retryCurrentChar: false);
            }
        }
        else if (char.IsLetterOrDigit(ch) || ch == '#')
        {
            entityBuffer.Append(ch);
            if (entityBuffer.Length > 10)
            {
                AbortAndRevert(retryCurrentChar: false);
            }
        }
        else
        {
            AbortAndRevert(retryCurrentChar: true);
        }
    }

    static char? ResolveEntity(string entity)
    {
        return entity switch {
            "lt" => '<',
            "gt" => '>',
            "amp" => '&',
            "quot" => '"',
            "apos" => '\'',
            _ when entity.StartsWith("#x") && int.TryParse(entity.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hex) => (char)hex,
            _ when entity.StartsWith("#") && int.TryParse(entity.Substring(1), out int dec) => (char)dec,
            _ => null
        };
    }

    void HandleTagOpen(char ch)
    {
        if (ch == '/')
        {
            state = ParserState.ReadCloseTag;
            return;
        }
        if (IsTagChar(ch))
        {
            state = ParserState.ReadTagName;
            tagBuffer.Append(ch);
            return;
        }
        AbortAndRevert(retryCurrentChar: true);
    }

    void HandleReadTagName(char ch)
    {
        if (ch == '>')
        {
            EmitOpenTag();
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
        AbortAndRevert(retryCurrentChar: true);
    }

    void HandleWaitAttrOrClose(char ch)
    {
        if (ch == '>')
        {
            EmitOpenTag();
            return;
        }
        if (IsWhitespace(ch)) return;
        if (ch == '/')
        {
            isSelfClosing = true;
            return;
        }
        if (IsAttrChar(ch))
        {
            state = ParserState.ReadAttrName;
            attrNameBuffer.Clear();
            attrNameBuffer.Append(ch);
            return;
        }
        AbortAndRevert(retryCurrentChar: true);
    }

    void HandleReadAttrName(char ch)
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
            EmitOpenTag();
            return;
        }
        if (ch == '/')
        {
            isSelfClosing = true;
            state = ParserState.WaitAttrOrClose;
            return;
        }
        AbortAndRevert(retryCurrentChar: true);
    }

    void HandleWaitAttrEquals(char ch)
    {
        if (ch == '=')
        {
            state = ParserState.WaitAttrQuote;
            return;
        }
        if (IsWhitespace(ch)) return;
        if (ch == '>')
        {
            EmitOpenTag();
            return;
        }
        if (ch == '/')
        {
            isSelfClosing = true;
            state = ParserState.WaitAttrOrClose;
            return;
        }
        AbortAndRevert(retryCurrentChar: true);
    }

    void HandleWaitAttrQuote(char ch)
    {
        if (ch == '"' || ch == '\'')
        {
            state = ParserState.ReadAttrValue;
            quoteChar = ch;
            attrValueBuffer.Clear();
            return;
        }
        if (IsWhitespace(ch)) return;
        if (ch == '>')
        {
            EmitOpenTag();
            return;
        }
        if (ch == '/')
        {
            isSelfClosing = true;
            state = ParserState.WaitAttrOrClose;
            return;
        }
        AbortAndRevert(retryCurrentChar: true);
    }

    void HandleReadAttrValue(char ch)
    {
        if (ch == quoteChar)
        {
            currentTagAttrs[attrNameBuffer.ToString()] = attrValueBuffer.ToString();
            state = ParserState.WaitAttrOrClose;
            return;
        }

        if (ch == '&')
        {
            returnState = ParserState.ReadAttrValue;
            state = ParserState.Entity;
            return;
        }

        attrValueBuffer.Append(ch);
    }

    void HandleReadCloseTag(char ch)
    {
        if (ch == '>')
        {
            string tagName = tagBuffer.ToString();
            if (!tagStack.Any(t => t.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
            {
                // 拦截孤儿闭标签（利用 rawBuffer 直通输出）
                AbortAndRevert(retryCurrentChar: false);
                return;
            }

            // 自动闭合并触发内层嵌套标签（向外回溯直到命中目标标签）
            while (tagStack.Count > 0)
            {
                string top = tagStack.Pop();
                CloseTagParsed?.Invoke(top);
                if (top.Equals(tagName, StringComparison.OrdinalIgnoreCase)) break;
            }

            ResetInner();
            return;
        }

        if (IsTagChar(ch))
        {
            tagBuffer.Append(ch);
            return;
        }
        AbortAndRevert(retryCurrentChar: true);
    }

    void EmitOpenTag()
    {
        string tagName = tagBuffer.ToString();
        if (isSelfClosing)
        {
            ShotTagParsed?.Invoke(tagName, currentTagAttrs);
        }
        else
        {
            tagStack.Push(tagName);
            OpenTagParsed?.Invoke(tagName, currentTagAttrs);
        }
        ResetInner();
    }

    static bool IsTagChar(char ch) => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':';
    static bool IsAttrChar(char ch) => IsTagChar(ch);
    static bool IsWhitespace(char ch) => ch is ' ' or '\t' or '\r' or '\n';
}
