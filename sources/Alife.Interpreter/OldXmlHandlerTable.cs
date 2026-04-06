using System.Text;

namespace Alife.Interpreter;

internal delegate Task CompiledTagInvoker(
    XmlTagContext context,
    ref string content,
    IReadOnlyDictionary<string, string> attributes);
public sealed record XmlParameterInfo(
    string Name,
    string Type,
    bool IsOptional,
    string Description = "",
    string[]? PossibleValues = null,
    bool IsContent = false);
public class XmlHandlerTable
{
    public IEnumerable<string> RegisteredTags => Handlers.Keys;
    public IReadOnlyDictionary<string, string> Descriptions { get; }
    public IReadOnlyDictionary<string, List<XmlParameterInfo>> TagParameters { get; }
    public IReadOnlyDictionary<string, string> ClassDescriptions { get; }
    public IReadOnlyDictionary<string, string> TagToClass { get; }

    internal IReadOnlyDictionary<string, CompiledTagInvoker> Handlers { get; }
    internal IReadOnlyList<CompiledTagInvoker> CatchAllHandlers { get; }

    internal XmlHandlerTable(
        Dictionary<string, CompiledTagInvoker> handlers,
        List<CompiledTagInvoker> catchAllHandlers,
        Dictionary<string, string> descriptions,
        Dictionary<string, List<XmlParameterInfo>> tagParameters,
        Dictionary<string, string> classDescriptions,
        Dictionary<string, string> tagToClass)
    {
        Handlers = handlers;
        CatchAllHandlers = catchAllHandlers;
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
        StringBuilder sb = new();

        IOrderedEnumerable<IGrouping<string, string>> tagsByClass = RegisteredTags
            .GroupBy(t => TagToClass.TryGetValue(t, out string? className) ? className : "其它")
            .OrderBy(g => g.Key);

        foreach (IGrouping<string, string> group in tagsByClass)
        {
            string className = group.Key;
            string classDesc = ClassDescriptions.TryGetValue(className, out string? d) ? d : "";

            sb.AppendLine($"### {className}");
            if (string.IsNullOrEmpty(classDesc) == false)
            {
                sb.AppendLine($"> {classDesc}");
            }
            sb.AppendLine();

            foreach (string tagName in group)
            {
                string description = Descriptions.TryGetValue(tagName, out string? desc) ? desc : "无说明";
                List<XmlParameterInfo> parameters = TagParameters.TryGetValue(tagName, out List<XmlParameterInfo>? @params) ? @params : [];

                List<XmlParameterInfo> attrs = parameters.Where(p => p.IsContent == false).ToList();
                XmlParameterInfo? content = parameters.FirstOrDefault(p => p.IsContent);

                if (content == null && attrs.Count == 0)
                {
                    sb.AppendLine($"- <{tagName} /> : {description}");
                    continue;
                }

                sb.Append($"- <{tagName}");
                foreach (XmlParameterInfo p in attrs)
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
                sb.AppendLine();
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
