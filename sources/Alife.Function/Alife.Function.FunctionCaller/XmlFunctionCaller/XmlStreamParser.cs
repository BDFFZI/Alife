using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Alife.Function.FunctionCaller;

public class XmlStreamParser
{
    public IEnumerable<string> PlainAreas => plainAreas;
    public IReadOnlyList<string> TagStack => tagStack;
    public IReadOnlyDictionary<string, string> TagParameters => parsedAttributes;
    public Func<Task>? TagOpened { get; set; }
    public Func<Task>? TagClosed { get; set; }
    public Func<Task>? TagShotted { get; set; }
    public Func<char, Task>? ContentGot { get; set; }
    public event Action<string, Exception>? Error;

    public async Task Feed(char ch)
    {
        if (tagStack.Count != 0 && plainAreas.Contains(tagStack.Last()))
        {
            tagBuffer.Append(ch);
            string plainContent = tagBuffer.ToString();
            if (plainContent.Last() == '>')
            {
                string newContent = Regex.Replace(plainContent, $"<\\s*/\\s*{tagStack.Last()}\\s*>", "");
                foreach (char c in newContent)
                    await HandleContentChar(c);
                tagBuffer.Clear();

                if (newContent != plainContent)
                {
                    tagMode = 1;
                    currentTagName = tagStack.Last();
                    await FlashTag();
                }
            }

            return;
        }

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
                case '"' or '<':
                    await FlashEscaping();
                    await Feed(ch);
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
                    if (attributeQuoteChar == '"')
                        FlashAttributeValue();
                    else
                        HandleTagChar(ch);
                    break;
                case '\'':
                    if (attributeQuoteChar == '\'')
                        FlashAttributeValue();
                    else
                        HandleTagChar(ch);
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
            case '\'':
                if (currentTagAttributeName != null)
                {
                    attributeQuoteChar = ch;
                    isValueParsing = true;
                }
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

    public async Task Flush(bool checkError = false)
    {
        if (checkError)
        {
            if (tagBuffer.Length != 0)
            {
                Error?.Invoke(currentTagName ?? "无名标签", new Exception("检测到没有关闭的标签，请检查语法格式是否正确完整。"));
            }
        }

        while (tagStack.Count != 0)
        {
            if (TagClosed != null)
                await TagClosed.Invoke();
            tagStack.RemoveAt(tagStack.Count - 1);
        }

        ClearAnnotation();
        ClearEscaping();
        ClearTag();
        parsedAttributes.Clear();
    }

    public XmlStreamParser(IEnumerable<string>? plainAreas = null)
    {
        if (plainAreas == null)
            this.plainAreas = [];
        else
            this.plainAreas = [..plainAreas.Select(t => t.ToLower())];
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
    char attributeQuoteChar;
    readonly Dictionary<string, string> parsedAttributes = new();
    readonly HashSet<string> plainAreas;
    readonly StringBuilder contentBuffer = new StringBuilder();

    /// 0：开标签；1：闭标签；2：自闭合标签
    int tagMode;

    readonly List<string> tagStack = new();

    async Task HandleContentChar(char ch)
    {
        if (ContentGot != null)
        {
            contentBuffer.Append(ch);
            await ContentGot.Invoke(ch);
        }
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
        if (currentTagName == null)//正在解析名称
        {
            if (tagBuffer.Length != 0)
                currentTagName = ExtractTagContent().ToLower();
        }
        else if (currentTagAttributeName == null)//正在解析属性名
        {
            if (tagBuffer.Length != 0)
                currentTagAttributeName = ExtractTagContent().ToLower();
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
                    contentBuffer.Clear();
                    tagStack.Add(currentTagName);
                    if (TagOpened != null)
                        await TagOpened.Invoke();
                    break;
                case 1:
                    if (tagStack.Contains(currentTagName) == false)
                    {
                        Error?.Invoke(currentTagName, new Exception($"检测到无效的孤儿闭标签：{currentTagName}"));
                        break;//无效的孤儿闭标签（未触发事件和入栈，直接无视即可）
                    }

                    while (tagStack.Last() != currentTagName)
                    {
                        //移除无效的孤儿开标签
                        Error?.Invoke(tagStack.Last(), new Exception($"检测到无效的孤儿开标签：{tagStack.Last()}"));
                        if (TagClosed != null)
                            await TagClosed.Invoke();//因为入栈且调用过函数，所以要回调
                        tagStack.RemoveAt(tagStack.Count - 1);
                    }

                    if (TagClosed != null)
                        await TagClosed.Invoke();
                    tagStack.RemoveAt(tagStack.Count - 1);
                    parsedAttributes[currentTagName] = contentBuffer.ToString();
                    contentBuffer.Clear();
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
        if (tagStack.Count == 0)//TODO 缺少正确的Xml参数环境，目前等于不清除，虽然确保闭标签时也能拿到参数，但可能污染其他标签。
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
        attributeQuoteChar = '\0';
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
