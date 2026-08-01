using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.FunctionCaller;

[Flags]
public enum FunctionMode
{
    All = ~0,
    Content = 0b_01,
    OneShot = 0b_10,
}

[AttributeUsage(AttributeTargets.Method)]
public class XmlFunctionAttribute(FunctionMode mode, string? name = null, int order = 0) : Attribute
{
    public string? Name { get; } = name;
    public int Order { get; } = order;
    public FunctionMode Mode { get; } = mode;
}

public class XmlFunction : IComparable<XmlFunction>
{
    public required string Name { get; init => field = value.ToLower(); }
    public int Order { get; init; }
    public FunctionMode Mode { get; init; }
    public string? Description { get; init; }
    public string? ContentName { get; init; }
    public string? ContentDescription { get; init; }
    public List<XmlParameter> Parameters { get; init; } = new();
    public required Func<XmlContext, CancellationToken, Task> Invoker { get; init; }

    public string Document()
    {
        StringBuilder sb = new();
        bool hasXmlForm = false;

        sb.Append($"<{Name}");
        foreach (XmlParameter param in Parameters)
        {
            if (param.IsXmlForm)
            {
                hasXmlForm = true;
                continue;
            }

            sb.Append($" {param.Document()}");
        }

        if (ContentName == null)
        {
            sb.Append("/>");
        }
        else
        {
            sb.Append(">");

            if (hasXmlForm)
            {
                sb.AppendLine();
                foreach (XmlParameter param in Parameters)
                {
                    if (param.IsXmlForm == false)
                        continue;
                    sb.AppendLine($"\t{param.Document()}");
                }
            }

            string cDesc = string.IsNullOrEmpty(ContentDescription)
                ? ""
                : $"（{ContentDescription}）";
            sb.Append($"{ContentName}{cDesc}</{Name}>");
        }

        if (string.IsNullOrEmpty(Description) == false)
            sb.Append($"：{Description}");

        return sb.ToString();
    }

    public int CompareTo(XmlFunction? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (other is null) return 1;
        return Order.CompareTo(other.Order);
    }
}
