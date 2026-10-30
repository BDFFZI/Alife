namespace Alife.Framework;

/// <summary>
/// 声明一个角色工作区的左侧菜单项；标记的组件类本身就是菜单内容，须继承 <see cref="CharacterMenu"/> 以接收宿主喂的参数。
/// </summary>
public sealed class CharacterMenuAttribute(
    string key,
    string name,
    string iconType,
    int order = 0)
    : UIAttribute
{
    public string Key { get; } = key;
    public string Name { get; } = name;

    /// <summary>AntDesign 图标字符串名（kebab-case，如 "comment"）。用字符串而非枚举，避免插件强依赖 AntDesign 类型。</summary>
    public string IconType { get; } = iconType;

    public int Order { get; } = order;
}
