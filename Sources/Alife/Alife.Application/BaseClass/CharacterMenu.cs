using Microsoft.AspNetCore.Components;

namespace Alife.Framework;

/// <summary>
/// 角色工作区菜单内容组件的基类。
/// 宿主页面用同一组参数渲染所有菜单组件（含插件组件），因此插件菜单与内置菜单可以互相替换。
/// 需要标记为菜单项时配合 <see cref="CharacterMenuAttribute"/> 使用。
/// </summary>
public abstract class CharacterMenu : ComponentBase
{
    [Parameter, EditorRequired] public Character Character { get; set; } = null!;
    [Parameter] public ChatActivity? ChatActivity { get; set; }
}
