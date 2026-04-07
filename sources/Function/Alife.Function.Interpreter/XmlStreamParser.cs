using System.Text;

namespace Alife.Function.Interpreter;

public class XmlStreamParser
{
    public IReadOnlyList<string> TagStack => tagStack;
    public IReadOnlyDictionary<string, string> TagParameters => parsedAttributes;
    public Func<Task>? TagOpened { get; set; }
    public Func<Task>? TagClosed { get; set; }
    public Func<Task>? TagShotted { get; set; }
    public Func<char, Task>? ContentGot { get; set; }

    public async Task Feed(char ch)
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
                    await FlashEscaping();
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
                    await HandleContentChar(ch);
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
                await FlashTag();
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
    public async Task Feed(string text)
    {
        foreach (char ch in text)
            await Feed(ch);
    }
    public async Task Flush()
    {
        while (tagStack.Count != 0)
        {
            if (TagClosed != null)
                await TagClosed.Invoke();
            tagStack.RemoveAt(tagStack.Count - 1);
        }

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

    readonly List<string> tagStack = new();

    async Task HandleContentChar(char ch)
    {
        if (ContentGot != null)
            await ContentGot.Invoke(ch);
    }
    void HandleTagChar(char ch)
    {
        tagBuffer.Append(ch);
    }

    async Task FlashEscaping()
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
                await HandleContentChar(escaping.Value);
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
                    await HandleContentChar(ch);
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
    async Task FlashTag()
    {
        if (currentTagName != null)
        {
            switch (tagMode)
            {
                case 0:
                    tagStack.Add(currentTagName);
                    if (TagOpened != null)
                        await TagOpened.Invoke();
                    break;
                case 1:
                    if (tagStack.Count != 0 && tagStack.Last() == currentTagName)
                    {
                        if (TagClosed != null)
                            await TagClosed.Invoke();
                        tagStack.RemoveAt(tagStack.Count - 1);
                    }
                    break;
                case 2:
                    tagStack.Add(currentTagName);
                    if (TagShotted != null)
                        await TagShotted.Invoke();
                    tagStack.RemoveAt(tagStack.Count - 1);
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
