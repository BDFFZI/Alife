namespace Alife.Framework;

/// <summary>
/// 声明一个侧栏按钮：标记的组件类自己就是这个按钮，图标、提示、点击行为全部写在组件里，所以这里不带任何显示参数。
/// 宿主只按 <see cref="Order"/> 排序、按开关过滤，然后把它渲染进侧栏；点开后是导航、覆盖层还是别的形态，同样由组件自己决定。
/// </summary>
public sealed class SideBarItemAttribute : UIAttribute
{
    /// <summary>侧栏顺序：越大越靠上，默认 0 落在最下面（客户端自带按钮都 ≥ 10）；相同时按组件类型名排。</summary>
    public int Order { get; set; }
}
