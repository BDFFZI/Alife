using System;
using System.Collections.Generic;

namespace Alife.Framework;

/// <summary>
/// 存储角色配置信息，同时可充当角色唯一索引，因为每个角色在整个软件运行周期都会复用同一个Character对象
/// </summary>
public class Character : ICloneable
{
    public required string Name { get; init; }
    public DateTime Birthday { get; set; } = DateTime.Now;
    public string Description { get; set; } = "";
    public string Prompt { get; set; } = "";
    public HashSet<string> Modules { get; set; } = new();
    public bool AutoActivate { get; set; }
    public string StorageKey => $"Character\\{Name}";

    public object Clone()
    {
        return new Character() {
            Birthday = Birthday,
            Name = Name,
            Prompt = Prompt,
            Description = Description,
            Modules = [.. Modules],
            AutoActivate = AutoActivate,
        };
    }
}
