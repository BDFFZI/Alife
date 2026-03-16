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

    public IEnumerable<string> RegisteredTags => Handlers.Keys;

    internal XmlHandlerTable(
        Dictionary<string, CompiledTagInvoker> handlers,
        Dictionary<string, string> descriptions,
        Dictionary<string, List<XmlParameterInfo>> tagParameters)
    {
        Handlers = handlers;
        Descriptions = descriptions;
        TagParameters = tagParameters;
    }

    /// <summary>
    /// 将当前的标签表动态翻译为 AI 可理解的文档。
    /// </summary>
    public string GenerateDocumentation()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var tagName in RegisteredTags)
        {
            string description = Descriptions.TryGetValue(tagName, out var desc) ? desc : "无说明";
            var parameters = TagParameters.TryGetValue(tagName, out var @params) ? @params : new List<XmlParameterInfo>();

            var attrs = parameters.Where(p => !p.IsContent).ToList();
            var content = parameters.FirstOrDefault(p => p.IsContent);

            sb.Append($"- <{tagName}");
            foreach (var p in attrs)
            {
                sb.Append($" {p.Name}=\"...\"");
            }
            
            if (content != null)
            {
                sb.Append($">内容</{tagName}>");
            }
            else
            {
                sb.Append(" />");
            }
            sb.AppendLine($": {description}");
        }
        return sb.ToString();
    }
}
