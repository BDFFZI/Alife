namespace Alife.Framework;

/// <summary>
/// 声明一个角色工作区的左侧菜单项；标记的组件类本身就是菜单内容，须继承 <see cref="CharacterMenu"/> 以接收宿主喂的参数。
/// 身份就是被标记的组件类型本身（宿主用它做菜单项的键），这里只留显示用的字段。
/// </summary>
public sealed class CharacterMenuAttribute(
    string name,
    string iconType,
    int order = 0)
    : UIAttribute
{
    public string Name { get; } = name;

    /// <summary>AntDesign 图标字符串名（kebab-case，如 "comment"）。用字符串而非枚举，避免插件强依赖 AntDesign 类型。</summary>
    public string IconType { get; } = iconType;

    /// <summary>菜单顺序：越大越靠上，默认 0 落在最下面（客户端自带菜单都 ≥ 10，把默认值留给插件排在末尾）。</summary>
    public int Order { get; } = order;
}
