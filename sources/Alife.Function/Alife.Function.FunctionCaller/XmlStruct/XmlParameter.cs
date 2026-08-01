using System;

namespace Alife.Function.FunctionCaller;

/// <summary>
/// 被标记的参数，在生成文档时会将其显示为嵌套xml
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public class XmlFormAttribute : Attribute;

public record XmlParameter
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Type { get; init; }
    public bool IsXmlForm { get; init; }

    public string Document()
    {
        string pDesc = string.IsNullOrEmpty(Description) ? "" : $"（{Description}）";
        return IsXmlForm ? $"<{Name}>{Type}{pDesc}</{Name}>" : $"{Name}=\"{Type}\"{pDesc}";
    }
}
