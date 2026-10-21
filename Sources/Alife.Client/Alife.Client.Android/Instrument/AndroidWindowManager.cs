using Android.OS;

namespace Alife.Client.Android;

/// <summary>
/// 窗口注册表：管理插件创建的动态窗口（始终为系统悬浮窗）。
/// </summary>
sealed class AndroidWindowManager
{
    public IAlifeWindow Create(AlifeWindowOptions options)
    {
        AndroidPointerMonitor.EnsureStarted();
        AndroidAlifeWindow window = new(options, Guid.NewGuid().ToString("N"));
        windows.Add(window);
        window.Disposed += () => windows.Remove(window);
        return window;
    }

    public static Task<T> RunOnUi<T>(Func<T> function)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        new Handler(Looper.MainLooper!).Post(() => {
            try
            {
                completion.TrySetResult(function());
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        });
        return completion.Task;
    }

    internal IEnumerable<AndroidAlifeWindow> All => windows.ToArray();

    readonly List<AndroidAlifeWindow> windows = [];
}
