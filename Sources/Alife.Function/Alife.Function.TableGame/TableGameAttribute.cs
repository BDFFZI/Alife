using System;
using Alife.Framework;

namespace Alife.Function.TableGame;

/// <summary>
/// 声明一款可以出现在游戏中心里的桌游。标记的组件就是游戏的完整界面，须继承 <see cref="TableGameUI"/>。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TableGameAttribute(string name, string description, Type iconComponent, int order = 0) : UIAttribute
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    /// <summary>游戏中心列表里的图标组件。</summary>
    public Type IconComponent { get; } = iconComponent;

    /// <summary>游戏顺序：越大越靠上，默认 0 落在最下面。</summary>
    public int Order { get; } = order;
}
