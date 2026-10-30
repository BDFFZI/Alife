namespace Alife.Client;

public static class ClientNavigation
{
    /// <summary>请求导航到「系统管理中心」的某个菜单页，按内容组件的类型定位，改名不影响调用方。</summary>
    public static void RegisterNavigator(Action<Type> handler)
    {
        TabRequested += handler;
        if (pendingTab != null)
        {
            handler.Invoke(pendingTab);
            pendingTab = null;
        }
    }
    public static void UnregisterNavigator(Action<Type> handler) => TabRequested -= handler;
    public static void RequestNavigation(Type target)
    {
        if (TabRequested == null)
        {
            pendingTab = target;
            return;
        }

        TabRequested.Invoke(target);
    }

    static event Action<Type>? TabRequested;
    static Type? pendingTab;
}
