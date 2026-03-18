namespace Alife.Interpreter;

internal delegate Task CompiledTagInvoker(
    XmlTagContext context,
    ref string content,
    IReadOnlyDictionary<string, string> attributes);

/// <summary>
/// 参数元数据。
/// </summary>
/// <param name="Name">参数名</param>
/// <param name="Type">类型名称</param>
/// <param name="IsOptional">是否可选</param>
/// <param name="Description">描述</param>
/// <param name="PossibleValues">如果是枚举，包含可能的值</param>
/// <param name="IsContent">是否映射到标签内容（Inner Text）</param>
public sealed record XmlParameterInfo(string Name, string Type, bool IsOptional, string Description = "", string[]? PossibleValues = null, bool IsContent = false);

/// <summary>
/// 编译后的标签处理程序映射表。
/// </summary>
public class XmlHandlerTable
{
    internal IReadOnlyDictionary<string, CompiledTagInvoker> Handlers { get; }
    public IReadOnlyDictionary<string, string> Descriptions { get; }
    public IReadOnlyDictionary<string, List<XmlParameterInfo>> TagParameters { get; }
    public IReadOnlyDictionary<string, string> ClassDescriptions { get; }
    public IReadOnlyDictionary<string, string> TagToClass { get; }

    public IEnumerable<string> RegisteredTags => Handlers.Keys;

    internal XmlHandlerTable(
        Dictionary<string, CompiledTagInvoker> handlers,
        Dictionary<string, string> descriptions,
        Dictionary<string, List<XmlParameterInfo>> tagParameters,
        Dictionary<string, string> classDescriptions,
        Dictionary<string, string> tagToClass)
    {
        Handlers = handlers;
        Descriptions = descriptions;
        TagParameters = tagParameters;
        ClassDescriptions = classDescriptions;
        TagToClass = tagToClass;
    }

    /// <summary>
    /// 将当前的标签表动态翻译为 AI 可理解的文档。
    /// </summary>
    public string GenerateDocumentation()
    {
        var sb = new System.Text.StringBuilder();

        // 按类名分组
        var tagsByClass = RegisteredTags
            .GroupBy(t => TagToClass.TryGetValue(t, out var className) ? className : "其它")
            .OrderBy(g => g.Key);

        foreach (var group in tagsByClass)
        {
            string className = group.Key;
            string classDesc = ClassDescriptions.TryGetValue(className, out var d) ? d : "";

            sb.AppendLine($"### {className}");
            if (!string.IsNullOrEmpty(classDesc))
            {
                sb.AppendLine($"> {classDesc}");
            }
            sb.AppendLine();

            foreach (var tagName in group)
            {
                string description = Descriptions.TryGetValue(tagName, out var desc) ? desc : "无说明";
                var parameters = TagParameters.TryGetValue(tagName, out var @params) ? @params : new List<XmlParameterInfo>();

                var attrs = parameters.Where(p => !p.IsContent).ToList();
                var content = parameters.FirstOrDefault(p => p.IsContent);

                if (content == null && attrs.Count == 0)
                {
                    sb.Append($"- <{tagName} /> : {description}");
                }
                else
                {
                    sb.Append($"- <{tagName}");
                    foreach (var p in attrs)
                    {
                        string pDesc = string.IsNullOrEmpty(p.Description) ? "" : $" (可选：{p.Description})";
                        sb.Append($" {p.Name}=\"{p.Type}{pDesc}\"");
                    }

                    if (content != null)
                    {
                        sb.Append(">");
                        string cDesc = string.IsNullOrEmpty(content.Description) ? "内容" : content.Description;
                        sb.Append(cDesc);
                        sb.Append($"</{tagName}> : {description}");
                    }
                    else
                    {
                        sb.Append(" />");
                        sb.Append($" : {description}");
                    }
                }
                sb.AppendLine();
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
