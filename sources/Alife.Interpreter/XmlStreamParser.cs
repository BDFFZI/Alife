using System.Text;

public class XmlStreamParser
{
    public Stack<string> TagStack => tagStack;
    public event Action<string, IReadOnlyDictionary<string, string>>? OpenTagParsed;
    public event Action<string>? CloseTagParsed;
    public event Action<string, IReadOnlyDictionary<string, string>>? ShotTagParsed;
    public event Action<char>? ContentParsed;

    public void Feed(char ch)
    {
        if (isAnnotation)
        {
            switch (ch)
            {
                case '>':
                    if (annotationBuffer.ToString().EndsWith("--"))
                        ClearAnnotation();
                    break;
                default:
                    annotationBuffer.Append(ch);
                    break;
            }
            return;
        }

        if (isCharEscaping)
        {
            switch (ch)
            {
                case ';':
                    escapingBuffer.Append(ch);
                    FlashEscaping();
                    break;
                default:
                    escapingBuffer.Append(ch);
                    break;
            }

            return;
        }

        if (ch == '&')
        {
            escapingBuffer.Append(ch);
            isCharEscaping = true;
            return;
        }

        if (isTagParsing == false)
        {
            switch (ch)
            {
                case '<':
                    isTagParsing = true;
                    break;
                default:
                    HandleContentChar(ch);
                    break;
            }

            return;
        }

        if (isValueParsing)
        {
            switch (ch)
            {
                case '"':
                    FlashAttributeValue();
                    break;
                default:
                    HandleTagChar(ch);
                    break;
            }

            return;
        }

        switch (ch)
        {
            case ' ':
            case '=':
                FlushTagOrAttributeName();
                break;
            case '/':
                FlushTagOrAttributeName();
                tagMode = currentTagName == null ? 1 : 2;
                break;
            case '>':
                FlushTagOrAttributeName();
                FlashTag();
                break;
            case '"':
                if (currentTagAttributeName != null)
                    isValueParsing = true;
                break;
            case '!':
                ClearTag();
                ClearEscaping();
                isAnnotation = true;
                break;
            default:
                HandleTagChar(ch);
                break;
        }
    }
    public void Feed(string text)
    {
        foreach (char ch in text)
            Feed(ch);
    }
    public void Flush()
    {
        while (tagStack.TryPeek(out string? tag))
        {
            CloseTagParsed?.Invoke(tag);
            tagStack.Pop();
        }
        Reset();
    }

    public void Reset()
    {
        tagStack.Clear();
        ClearAnnotation();
        ClearEscaping();
        ClearTag();
    }

    //注释状态
    bool isAnnotation;
    readonly StringBuilder annotationBuffer = new();

    //转义状态
    bool isCharEscaping;
    readonly StringBuilder escapingBuffer = new();

    //解析状态
    bool isTagParsing;
    readonly StringBuilder tagBuffer = new();
    string? currentTagName;
    string? currentTagAttributeName;
    bool isValueParsing;
    readonly Dictionary<string, string> parsedAttributes = new();
    /// 0：开标签；1：闭标签；2：自闭合标签
    int tagMode;

    readonly Stack<string> tagStack = new();

    void HandleContentChar(char ch)
    {
        ContentParsed?.Invoke(ch);
    }
    void HandleTagChar(char ch)
    {
        tagBuffer.Append(ch);
    }

    void FlashEscaping()
    {
        isCharEscaping = false;
        string content = escapingBuffer.ToString();
        escapingBuffer.Clear();

        char? escaping = content switch {
            "&#34;" or "&quot;" => '"',
            "&#38;" or "&amp;" => '&',
            "&#60;" or "&lt;" => '<',
            "&#62;" or "&gt;" => '>',
            "&#160;" or "&nbsp;" => ' ',
            _ => null
        };

        if (escaping != null)
        {
            if (isTagParsing)
                HandleTagChar(escaping.Value);
            else
                HandleContentChar(escaping.Value);
        }
        else
        {
            if (isTagParsing)
            {
                foreach (char ch in content)
                    HandleTagChar(ch);
            }
            else
            {
                foreach (char ch in content)
                    HandleContentChar(ch);
            }
        }
    }

    string ExtractTagContent()
    {
        string content = tagBuffer.ToString();
        tagBuffer.Clear();
        return content;
    }
    /// <summary>
    /// 仅用于触发名称输入完成
    /// </summary>
    void FlushTagOrAttributeName()
    {
        if (currentTagName == null) //正在解析名称
        {
            if (tagBuffer.Length != 0)
                currentTagName = ExtractTagContent();
        }
        else if (currentTagAttributeName == null) //正在解析属性名
        {
            if (tagBuffer.Length != 0)
                currentTagAttributeName = ExtractTagContent();
        }
    }
    void FlashAttributeValue()
    {
        if (currentTagAttributeName == null)
            throw new Exception("缺少属性名！请检查调用顺序。");

        string currentTagAttributeValue = ExtractTagContent();
        parsedAttributes[currentTagAttributeName] = currentTagAttributeValue;
        currentTagAttributeName = null;
        isValueParsing = false;
    }
    void FlashTag()
    {
        if (currentTagName != null)
        {
            switch (tagMode)
            {
                case 0:
                    tagStack.Push(currentTagName);
                    OpenTagParsed?.Invoke(currentTagName, parsedAttributes);
                    break;
                case 1:
                    if (tagStack.TryPeek(out string? lastTag) && lastTag == currentTagName)
                    {
                        CloseTagParsed?.Invoke(currentTagName);
                        tagStack.Pop();
                    }
                    break;
                case 2:
                    tagStack.Push(currentTagName);
                    ShotTagParsed?.Invoke(currentTagName, parsedAttributes);
                    tagStack.Pop();
                    break;
            }
        }

        isTagParsing = false;
        currentTagName = null;
        currentTagAttributeName = null;
        parsedAttributes.Clear();
        tagMode = 0;
    }

    void ClearTag()
    {
        isTagParsing = false;
        tagBuffer.Clear();
        currentTagName = null;
        currentTagAttributeName = null;
        isValueParsing = false;
        parsedAttributes.Clear();
        tagMode = 0;
    }
    void ClearEscaping()
    {
        isCharEscaping = false;
        escapingBuffer.Clear();
    }
    void ClearAnnotation()
    {
        isAnnotation = false;
        annotationBuffer.Clear();
    }
}
