
using System;

namespace Alife.Framework;

/// <summary>
/// 系统管理中心菜单项声明特性。
/// 标记在一个 Blazor 组件类上，使其自动作为「系统管理中心」的一个左侧子菜单项被装载。
/// 内置菜单与插件菜单一视同仁，均通过本特性驱动，符合开闭原则：新增菜单无需改动框架页面代码。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class SystemMenuAttribute(
    string key,
    string name,
    string iconType,
    Type contentComponent,
    int order = 0)
    : Attribute
{
    /// <summary>菜单唯一标识，对应 TabPane 的 Key，也用于跨页导航定位。</summary>
    public string Key { get; private set; } = key;

    /// <summary>菜单显示文字。</summary>
    public string Name { get; private set; } = name;

    /// <summary>AntDesign 图标字符串名（kebab-case，如 "home"、"cloud-download"）。用字符串而非枚举，避免插件强依赖 AntDesign 类型。</summary>
    public string IconType { get; private set; } = iconType;

    /// <summary>菜单内容组件类型（Blazor 组件），与 ModuleAttribute.editorUI 同构。</summary>
    public Type ContentComponent { get; private set; } = contentComponent;

    /// <summary>排序权重，越小越靠前；内置菜单占用低位，插件菜单默认高位插到末尾。</summary>
    public int Order { get; private set; } = order;
}
