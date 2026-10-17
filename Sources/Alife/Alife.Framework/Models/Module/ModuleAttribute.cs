using System;

namespace Alife.Framework;

public class ModuleAttribute(
    string name,
    string description,
    string? url = null,
    Type? editorUI = null,
    Type? globalUI = null,
    int launchOrder = 0,
    string defaultCategory = "")
    : Attribute
{
    public string Name { get; private set; } = name;
    public string Description { get; private set; } = description;
    public string? Url { get; private set; } = url;
    public Type? EditorUI { get; set; } = editorUI;
    public Type? GlobalUI { get; set; } = globalUI;
    public int LaunchOrder { get; set; } = launchOrder;
    public string DefaultCategory { get; private set; } = defaultCategory;
}
