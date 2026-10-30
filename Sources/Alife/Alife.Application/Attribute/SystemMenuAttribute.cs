namespace Alife.Framework;

/// <summary>
/// 声明一个「系统管理中心」的左侧菜单项；标记的组件类本身就是菜单内容，由宿主页面用 DynamicComponent 渲染。
/// </summary>
public sealed class SystemMenuAttribute(
    string key,
    string name,
    string iconType,
    int order = 0)
    : UIAttribute
{
    public string Key { get; } = key;
    public string Name { get; } = name;

    /// <summary>AntDesign 图标字符串名（kebab-case，如 "home"）。用字符串而非枚举，避免插件强依赖 AntDesign 类型。</summary>
    public string IconType { get; } = iconType;

    public int Order { get; } = order;
}
