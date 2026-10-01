namespace Alife.Client.Core;

public static class ClientNavigation
{
    public static void RegisterNavigator(Action<string> handler)
    {
        TabRequested += handler;
        if (pendingTab != null)
        {
            handler.Invoke(pendingTab);
            pendingTab = null;
        }
    }
    public static void UnregisterNavigator(Action<string> handler) => TabRequested -= handler;
    public static void RequestNavigation(string path)
    {
        if (TabRequested == null)
        {
            pendingTab = path;
            return;
        }

        TabRequested.Invoke(path);
    }

    static event Action<string>? TabRequested;
    static string? pendingTab;
}