using System;
using System.Collections.Immutable;

namespace Alife.Function.FunctionCaller;

public enum CallMode
{
    Opening = 0,
    Content = 1,
    Closing = 2,
    OneShot = 3,
}

[AttributeUsage(AttributeTargets.Parameter)]
public class XmlContentAttribute : Attribute { }

public class XmlContext
{
    public CallMode CallMode { get; init; }
    public string Content { get; set; } = "";
    public required ImmutableDictionary<string, string> Parameters { get; init; }
    public required ImmutableList<string> CallChain { get; init; }
}