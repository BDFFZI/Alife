using System;
using System.Collections.Generic;

namespace Alife.Function.FunctionCaller;

public enum CallMode
{
    Opening = 0,
    Content = 1,
    Closing = 2,
    OneShot = 3,
}

[AttributeUsage(AttributeTargets.Parameter)]
public class XmlContentAttribute : Attribute {}

public class XmlContext
{
    public CallMode CallMode { get; init; }
    public string Content { get; set; } = "";
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
}
