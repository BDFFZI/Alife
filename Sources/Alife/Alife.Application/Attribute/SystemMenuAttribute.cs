namespace Alife.Framework;

/// <summary>
/// 声明一个「系统管理中心」的左侧菜单项；标记的组件类本身就是菜单内容，由宿主页面用 DynamicComponent 渲染。
/// 身份就是被标记的组件类型本身（宿主用它做标签页的键），这里只留显示用的字段。
/// </summary>
public sealed class SystemMenuAttribute(
    string name,
    string iconType,
    int order = 0)
    : UIAttribute
{
    public string Name { get; } = name;

    /// <summary>AntDesign 图标字符串名（kebab-case，如 "home"）。用字符串而非枚举，避免插件强依赖 AntDesign 类型。</summary>
    public string IconType { get; } = iconType;

    /// <summary>菜单顺序：越大越靠上，默认 0 落在最下面（客户端自带菜单都 ≥ 10，把默认值留给插件排在末尾）。</summary>
    public int Order { get; } = order;
}
