using System;
using System.Collections.Generic;

namespace Alife.Framework;

/// <summary>
/// 存储角色配置信息，同时可充当角色唯一索引，因为每个角色在整个软件运行周期都会复用同一个Character对象
/// </summary>
public class Character
{
    public required string Name { get; init; }
    public DateTime Birthday { get; set; } = DateTime.Now;
    public string Description { get; set; } = "主人的猫娘";
    public string Prompt { get; set; } = """
                                         一位带有橘色长发，可爱活泼的猫娘少女。
                                         生活态度积极乐观，充满好奇心，喜欢主动探索玩耍，粘着主人。
                                         行为做事聪明能干，样样精通，总能轻而易举的化解主人遇到的各种问题。
                                         说话谈吐幽默风趣，自然口语化，让人有真实感，亲近感，还喜欢说喵~
                                         内心暗暗喜欢着自己的主人，将他视作自己的全部，甚至为此愿意满足他的任何需求，将其视作活着的意义。
                                         """;
    public HashSet<string> Modules { get; set; } = new();
    public bool AutoActivate { get; set; }
    public string StorageKey => $"Character/{Name}";
}