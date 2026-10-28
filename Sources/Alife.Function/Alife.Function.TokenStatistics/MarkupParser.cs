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
    // 用户侧标签的标准形式：[名称] 或 [名称(参数)]。
    // 名称以字母、汉字开头；参数可含时间、标点等。这样排除 [MiB]、[{...}] 之类的非标签噪声。
    static readonly Regex TagPattern = new(
        @"\[(?<body>[\p{L}][^\[\]\r\n()（）]*(?:[（(][^\[\]\r\n]*[）)])?)\]",
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
    /// 字节数口径：<b>标签自身 + 其后的普通文本，直到遇到下一个标签或消息结尾</b>——
    /// 因为真正让模型多花钱的是标签之后那段内容，而不是方括号本身。
    /// 排除 [CQ:...] 这类内部指令（以 CQ: 开头，不是语义标签）；被排除的指令文本归属前一个标签。
    /// 注意：以数字开头的时间戳如 [2026-10-27 20:25:00] 目前会被计为标签，
    /// 是否排除尚待用户确认（见交接文档）。
    /// </summary>
    public static IReadOnlyList<MarkupHit> ParseUserTags(string userMessage)
    {
        List<MarkupHit> hits = new();
        if (string.IsNullOrEmpty(userMessage))
            return hits;

        // 先收集所有真正算标签的出现（跳过 CQ: 指令），再按"到下一个标签为止"切段
        List<(string Name, int Index)> tags = new();
        foreach (Match match in TagPattern.Matches(userMessage))
        {
            string body = match.Groups["body"].Value;
            if (body.StartsWith("CQ:", StringComparison.Ordinal))
                continue;
            tags.Add((body, match.Index));
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
    /// 解析 AI 消息中的函数，返回**互斥**的字节归属（同一段文本只归一个函数）：
    /// - 成对函数：从开标签到匹配的闭标签整段；同名嵌套用栈配对，因此不会截断；
    /// - 自闭合函数：只计自身；
    /// - 父函数只保留"不属于任何子函数"的部分（开闭标签与散落文本），子函数的字节不再重复计入父函数；
    /// - 未闭合的开标签（或未匹配的闭标签）按宽容策略就近收尾，避免整段内容丢失。
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

        int messageLength = aiMessage.Length;
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

            // 闭标签：从栈顶向下找同名；路上遇到的未闭合容器按"在这里收尾"处理
            int closeEnd = item.Index + item.Length;
            while (stack.Count > 0)
            {
                OpenTag top = stack.Pop();
                nodes.Add(new SpanNode(top.Name, top.Start, closeEnd));
                if (string.Equals(top.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                    break;
            }
        }

        // 扫描结束后仍未闭合的：收到消息末尾（比只计开标签更接近真实内容）
        while (stack.Count > 0)
        {
            OpenTag top = stack.Pop();
            nodes.Add(new SpanNode(top.Name, top.Start, messageLength));
        }

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

    readonly record struct CloseHit(string Name, int Index, int Length);
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
    /// 默认对「时间」忽略参数：`时间(2026-10-28 11:00:00)` 与 `时间` 合并为同一类。
    /// 这是一条普通配置（界面上可增删），不是代码里的特判。
    /// </summary>
    public static readonly string[] DefaultIgnoreParams = ["时间"];

    readonly HashSet<string> ignoreParams;

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
    /// 按"忽略参数"配置规范化名字：配置里的基础名去掉参数（`时间(x)` → `时间`），
    /// 其余名字原样保留（`源(QChat)` 仍带参数）。
    /// </summary>
    public string Normalize(string name)
    {
        string baseName = BaseNameOf(name);
        if (baseName.Length == name.Length)
            return name;

        return ignoreParams.Contains(baseName) ? baseName : name;
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