
namespace Alife.Client.Android;

/// <summary>
/// 窗口注册表：管理插件创建的动态窗口（对话框或系统悬浮窗）。
/// </summary>
public sealed class AndroidWindowManager
{
    public Alife.Client.IAlifeWindow Create(Alife.Client.AlifeWindowOptions options)
    {
        AndroidWebWindow window = new(options, Guid.NewGuid().ToString("N"));
        windows.Add(window);
        return window;
    }

    internal IEnumerable<AndroidWebWindow> All => windows.ToArray();

    readonly List<AndroidWebWindow> windows = [];
}
