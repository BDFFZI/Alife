using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 解析一条消息中的标签与函数，并统计其 UTF-8 字节数。
/// 用户消息：解析 [标签]，每次出现计一次；字节数 = 标签自身 + 其后普通文本（直到下一个标签或结尾）。
/// AI 消息：只解析 &lt;函数&gt;（AI 的函数执行）。成对函数（如 &lt;Speak&gt;...&lt;/Speak&gt;）的字节数
/// 包含开闭标签及中间内容；自闭合（如 &lt;Expression .../&gt;）只计自身。
/// 函数名不区分大小写。注释（&lt;!-- --&gt;）与 &lt;!----&gt; 不是函数，直接排除。
/// </summary>
public static class MarkupParser
{
    // 用户侧标签的标准形式：[名称] 或 [名称(参数)]，也可能是 [名称: 值]（如 [图片: https://…]）。
    // 名称必须以中文开头（框架标签都是中文语义标签，如 [系统推送消息]、[源(QChatService)]）。
    // 这一条排除掉正文里的方括号噪声：`[WinError 10061]`、`[i]`、`[main f292b0a]`、`[CQ:...]` 等。
    static readonly Regex TagPattern = new(
        @"\[(?<body>[\u4E00-\u9FFF][^\[\]\r\n()（）]*(?:[（(][^\[\]\r\n]*[）)])?)\]",
        RegexOptions.Compiled);

    // AI 侧开标签或自闭合标签：名字以字母开头，可带属性；不匹配 <!...>（注释/声明）与 <@...>（QQ @）。
    static readonly Regex OpenOrSelfTagPattern = new(
        @"<(?<name>[A-Za-z][A-Za-z0-9_]*)(?:\s[^<>]*?)?\s*(?<self>/)?>",
        RegexOptions.Compiled);

    // AI 侧闭标签：</名字>
    static readonly Regex CloseTagPattern = new(
        @"</(?<name>[A-Za-z][A-Za-z0-9_]*)\s*>",
        RegexOptions.Compiled);

    /// <summary>
    /// 解析用户消息中的标签，并计算"这个标签带来的内容体量"。
    /// 一个方括号要同时过两关才算标签：名字以中文开头、位置符合框架的注入方式（见 <see cref="StandsAsTag"/>）；
    /// 位置不对的方括号一律算正文，归到前一个标签名下。
    /// 字节数口径：<b>标签自身 + 其后的普通文本，直到遇到下一个标签或消息结尾</b>——
    /// 因为真正让模型多花钱的是标签之后那段内容，而不是方括号本身。
    /// </summary>
    public static IReadOnlyList<MarkupHit> ParseUserTags(string userMessage)
    {
        List<MarkupHit> hits = new();
        if (string.IsNullOrEmpty(userMessage))
            return hits;

        // 先收集所有真正算标签的出现，再按"到下一个标签为止"切段
        List<(string Name, int Index)> tags = new();
        int lastTagEnd = -1;
        foreach (Match match in TagPattern.Matches(userMessage))
        {
            if (StandsAsTag(userMessage, match.Index, lastTagEnd) == false)
                continue;

            tags.Add((match.Groups["body"].Value, match.Index));
            lastTagEnd = match.Index + match.Length;
        }

        for (int i = 0; i < tags.Count; i++)
        {
            int start = tags[i].Index;
            int end = i + 1 < tags.Count ? tags[i + 1].Index : userMessage.Length;
            string segment = userMessage.Substring(start, end - start);
            hits.Add(new MarkupHit(tags[i].Name, Utf8Bytes(segment), start));
        }

        return hits;
    }

    /// <summary>
    /// 这个方括号是否处在"框架注入标签"的位置上——框架只在两种位置写标签：
    /// 行首第一个字符（<c>[</c> 前面紧挨着换行，或整条消息的开头），或紧跟在上一个标签之后
    /// （附加标签，如 <c>[源(QChatService)][私聊消息(…)]</c>：框架把"哪条通道"和"里面装的是什么"写在同一行）。
    /// 其它位置的方括号一律算正文：被引用的历史/日志（<c>[12:54:24] …</c>、JSON 转义的历史）、
    /// 代码里的 <c>[Description(…)]</c>、以及 <c>如下记忆存档：[记忆存档(…)]</c> 这种"指出一个具体东西"的写法。
    /// </summary>
    static bool StandsAsTag(string text, int index, int lastTagEnd)
    {
        if (index == lastTagEnd)
            return true;

        return index == 0 || text[index - 1] == '\n';
    }

    /// <summary>
    /// 解析 AI 消息中的函数，返回**互斥**的字节归属（同一段文本只归一个函数）：
    /// - 成对函数：从开标签到匹配的闭标签整段；同名嵌套用栈配对，因此不会截断；
    /// - 自闭合函数：只计自身；
    /// - 父函数只保留"不属于任何子函数"的部分（开闭标签与散落文本），子函数的字节不再重复计入父函数；
    /// - **没有配对闭标签的开标签一律不识别**：正文里的代码（如 <c>Task&lt;string&gt;</c>、
    ///   <c>List&lt;TokenUsageContext&gt;</c>）全是这种形态，若按"延伸到消息末尾"处理会把后面所有内容吞掉。
    /// 注释（&lt;!-- --&gt;）与 &lt;@...&gt; 不是函数，直接排除。
    /// </summary>
    public static IReadOnlyList<MarkupHit> ParseAiFunctions(string aiMessage)
    {
        List<MarkupHit> hits = new();
        if (string.IsNullOrEmpty(aiMessage))
            return hits;

        List<TagEvent> events = [];
        foreach (Match open in OpenOrSelfTagPattern.Matches(aiMessage))
        {
            events.Add(new TagEvent(
                open.Index,
                open.Length,
                open.Groups["name"].Value,
                open.Groups["self"].Success ? TagKind.SelfClosing : TagKind.Open));
        }
        foreach (Match close in CloseTagPattern.Matches(aiMessage))
            events.Add(new TagEvent(close.Index, close.Length, close.Groups["name"].Value, TagKind.Close));

        events.Sort((left, right) => left.Index.CompareTo(right.Index));

        List<SpanNode> nodes = [];
        Stack<OpenTag> stack = new();

        foreach (TagEvent item in events)
        {
            if (item.Kind == TagKind.SelfClosing)
            {
                nodes.Add(new SpanNode(item.Name, item.Index, item.Index + item.Length));
                continue;
            }

            if (item.Kind == TagKind.Open)
            {
                stack.Push(new OpenTag(item.Name, item.Index));
                continue;
            }

            // 闭标签：从栈顶向下找同名。路上遇到的"没有自己闭标签"的开标签直接丢弃——
            // 它们多半是正文里的代码（注释里的 <string> 就夹在 <Python> 块中间），
            // 若按"在这里收尾"给它们造区间，就会凭空多出一段别人的内容。
            int closeEnd = item.Index + item.Length;
            while (stack.Count > 0)
            {
                OpenTag top = stack.Pop();
                if (string.Equals(top.Name, item.Name, StringComparison.OrdinalIgnoreCase) == false)
                    continue;

                nodes.Add(new SpanNode(top.Name, top.Start, closeEnd));
                break;
            }
        }

        // 扫描结束后仍未闭合的开标签：不识别。正文里的 <string>、<ChatResult> 之类都会走到这里，
        // 若给它们"延伸到消息末尾"的区间，会把后面真正的函数与正文全部吞掉。
        stack.Clear();

        if (nodes.Count == 0)
            return hits;

        // 建父子关系：按 Start 升序、End 降序，用栈确定直接父节点
        nodes.Sort((left, right) => left.Start != right.Start
            ? left.Start.CompareTo(right.Start)
            : right.End.CompareTo(left.End));

        int[] parent = new int[nodes.Count];
        for (int i = 0; i < parent.Length; i++)
            parent[i] = -1;

        Stack<int> openIndices = new();
        for (int i = 0; i < nodes.Count; i++)
        {
            while (openIndices.Count > 0 && nodes[openIndices.Peek()].End <= nodes[i].Start)
                openIndices.Pop();

            if (openIndices.Count > 0 && nodes[openIndices.Peek()].End >= nodes[i].End)
                parent[i] = openIndices.Peek();

            openIndices.Push(i);
        }

        // 父函数扣除直接子函数的字节，得到"互斥"归属
        int[] exclusive = new int[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            SpanNode node = nodes[i];
            exclusive[i] = Utf8Bytes(aiMessage.Substring(node.Start, node.End - node.Start));
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            if (parent[i] < 0)
                continue;
            exclusive[parent[i]] -= exclusive[i];
            if (exclusive[parent[i]] < 0)
                exclusive[parent[i]] = 0;
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            if (exclusive[i] <= 0)
                continue;
            hits.Add(new MarkupHit(nodes[i].Name, exclusive[i], nodes[i].Start));
        }

        return hits;
    }

    /// <summary>AI 侧标签扫描到的事件。</summary>
    readonly record struct TagEvent(int Index, int Length, string Name, TagKind Kind);

    /// <summary>尚未闭合的开标签。</summary>
    readonly record struct OpenTag(string Name, int Start);

    /// <summary>已配对的函数片段（字符下标区间）。</summary>
    readonly record struct SpanNode(string Name, int Start, int End);

    /// <summary>标签种类。</summary>
    enum TagKind
    {
        Open,
        Close,
        SelfClosing,
    }
    /// <summary>UTF-8 字节数（中文一个字 3 字节，ASCII 一个字节）。</summary>
    public static int Utf8Bytes(string text) => Encoding.UTF8.GetByteCount(text);

}

/// <summary>一次标签/函数出现：名字、字节数、在原文中的位置（用于同分时"先出现者优先"）。</summary>
public readonly record struct MarkupHit(string Name, int Bytes, int Index);

/// <summary>
/// 标签/函数排除规则：被排除的名字不参与排行与分摊（但仍保存在记录里，可随时恢复）。
/// 默认不过滤任何名字；`系统推送消息`、`时间` 等噪声标签同样按字节占比参与统计。
/// 按名字精确匹配，不区分大小写（函数名已确认不区分大小写，标签同样处理）。
/// </summary>
public sealed class TagFilter
{
    /// <summary>默认不排除任何名字：现在按实际字节占比分摊，不存在"主导"被噪声抢走的问题。</summary>
    public static readonly string[] DefaultExcluded = [];

    readonly HashSet<string> excluded;

    /// <summary>
    /// 默认忽略这些标签的参数——它们的参数是框架/插件自己的内部取值，带着会把同一类标签拆成几十个条目：
    /// `时间(2026-11-01 12:43:20)`、`记忆存档(1-20261027124635-20261027195855)`。
    /// 业务字段（如 `私聊消息(1330958515,半点星光)` 里的号码与昵称）不在这里——那是"谁发来的"，有意义，
    /// 而且不能对某个通讯插件特殊处理；需要时在界面上按名字加进"忽略参数"。
    /// 这是一条普通配置（界面上可增删），不是代码里的特判。
    /// </summary>
    public static readonly string[] DefaultIgnoreParams = ["时间", "记忆存档"];

    readonly HashSet<string> ignoreParams;

    // 名字里被引号包起来的内容（`对“1330958515：…”的回复`）——每条消息都不一样，统计时抹掉。
    static readonly Regex QuotedContentPattern = new(
        "“[^”]*”|\"[^\"]*\"",
        RegexOptions.Compiled);

    // 「名字: 取值」形式（`图片: https://…`、`表情: 277`）——取值每条消息都不同，只留冒号前的名字。
    static readonly Regex NameValuePattern = new(
        @"^([^:：]+)[:：].*$",
        RegexOptions.Compiled);

    public TagFilter(IEnumerable<string>? excludedNames = null, IEnumerable<string>? ignoreParamNames = null)
    {
        excluded = new HashSet<string>(excludedNames ?? DefaultExcluded, StringComparer.OrdinalIgnoreCase);
        ignoreParams = new HashSet<string>(ignoreParamNames ?? DefaultIgnoreParams, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>基础名（去掉括号参数）；没有参数时返回自身。</summary>
    public static string BaseNameOf(string name)
    {
        int parenIndex = name.IndexOfAny(['(', '（']);
        return parenIndex >= 0 ? name.Substring(0, parenIndex) : name;
    }

    /// <summary>
    /// 按"忽略参数"配置规范化名字：
    /// 1. 名字里的引号内容与「名字: 取值」的取值一律抹掉——`对“1330958515：…”的回复` → `对…的回复`、
    ///    `图片: https://…` → `图片`，否则每来一条消息就多一个条目；
    /// 2. 配置里的基础名再去掉括号参数（`时间(x)` → `时间`）；其余名字原样保留（`源(QChatService)` 仍带参数）。
    /// </summary>
    public string Normalize(string name)
    {
        string plain = QuotedContentPattern.Replace(name, "…");
        Match match = NameValuePattern.Match(plain);
        if (match.Success)
            plain = match.Groups[1].Value.Trim();

        string baseName = BaseNameOf(plain);
        if (baseName.Length == plain.Length)
            return plain;

        return ignoreParams.Contains(baseName) ? baseName : plain;
    }

    /// <summary>
    /// 名字是否被排除（即不参与排行与分摊）。比较的是括号前的名称部分，
    /// 因此 时间(2026-10-27 20:33:58) 与 时间 视为同一标签。
    /// </summary>
    public bool IsExcluded(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // 先按完整名匹配（可精确排除 源(QChat)），再按基础名匹配（可整类排除 时间(...)）
        if (excluded.Contains(name.Trim()))
            return true;

        int parenIndex = name.IndexOfAny(['(', '（']);
        if (parenIndex < 0)
            return false;

        return excluded.Contains(name.Substring(0, parenIndex));
    }
}