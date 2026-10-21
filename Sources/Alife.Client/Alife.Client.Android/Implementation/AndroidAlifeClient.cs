using Alife.Foundation;
using Alife.Framework;

namespace Alife.Client.Android;

sealed class AndroidAlifeClient(AndroidAlifeFoundation foundation, IServiceProvider provider) : IAlifeClient
{
    public Task UpgradeClient(string version, Action<int>? onProgress = null)
    {
        throw new NotSupportedException("Android 客户端只能通过安装新版 APK 更新。");
    }

    public void SetStorageFolderPath(string path)
    {
        foundation.SetStorageFolderPath(path);
    }

    public void SetRuntimeFolderPath(string path)
    {
        foundation.SetRuntimeFolderPath(path);
    }

    public bool GetAutoStart()
    {
        return File.Exists(KeepAlivePaths.AutoStartFile);
    }
    public void SetAutoStart(bool value)
    {
        if (value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeepAlivePaths.AutoStartFile)!);
            File.WriteAllText(KeepAlivePaths.AutoStartFile, "");
            return;
        }
        if (File.Exists(KeepAlivePaths.AutoStartFile))
            File.Delete(KeepAlivePaths.AutoStartFile);
    }

    public IReadOnlyList<EnvironmentItem> GetEnvironmentItems()
    {
        return AndroidEnvironment.GetEnvironmentItems(foundation);
    }

    public async Task InstallEnvironmentAsync(string id, IProgress<string>? progress = null)
    {
        await AndroidEnvironment.InstallEnvironmentAsync(foundation, id, progress);

        if (id is "python")
        {
            try
            {
                await provider.GetRequiredService<PluginSystem>().SyncLocalPluginEnvironment();
            }
            catch (Exception e)
            {
                AlifeLog.LogWarning("环境安装完成后同步插件失败：" + e.Message);
            }
        }
    }

    /// <summary>Last observed touch/hover position in physical SCREEN pixels, never viewport-local pixels.</summary>
    internal static long LastCursor = -1;

    public static System.Drawing.Point LastCursorPosition
    {
        get
        {
            long value = Interlocked.Read(ref LastCursor);
            return value < 0 ? default : new((int)(value >> 32), (int)value);
        }
        set => ObservePointer(value);
    }

    internal static void ObservePointer(System.Drawing.Point position, bool pressed = false)
    {
        Interlocked.Exchange(ref LastCursor, ((long)position.X << 32) | (uint)position.Y);
        MainThread.BeginInvokeOnMainThread(() => AndroidAlifeWindow.UpdatePointer(position, pressed));
    }
}

/// <summary>全局路径与自启动标记等常量。</summary>
static class KeepAlivePaths
{
    public static string AutoStartFile { get; } =
        Path.Combine(MainApplication.Instance!.FilesDir!.AbsolutePath, "Alife", "AutoStart");
}