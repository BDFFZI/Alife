using Alife.Foundation;
using Alife.Framework;
using Android.Util;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Client.Android;

public sealed class AndroidAlifeClient(
    AndroidWindowManager windowManager,
    AndroidAlifeFoundation foundation,
    IServiceProvider provider) : Alife.Client.IAlifeClient
{
    public Task UpgradeClient(string version, Action<int>? onProgress = null)
    {
        throw new NotSupportedException("Android 客户端请通过安装新版 APK 更新。");
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

    public IReadOnlyList<Alife.Client.EnvironmentItem> GetEnvironmentItems()
    {
        return AndroidEnvironment.GetEnvironmentItems();
    }

    public async Task InstallEnvironmentAsync(string id, IProgress<string>? progress = null)
    {
        await AndroidEnvironment.InstallEnvironmentAsync(id, progress);

        if (id is "python" or "dotnet")
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

    public Task RevealFolderAsync(string path)
    {
        throw new NotSupportedException("Android 客户端不支持打开文件管理器。");
    }
    public Task RevealFileAsync(string path)
    {
        throw new NotSupportedException("Android 客户端不支持定位文件。");
    }
    public Task<string?> SelectFolderAsync()
    {
        throw new NotSupportedException("Android 客户端不支持选择目录。");
    }

    public System.Drawing.Size GetScreenSize()
    {
        DisplayMetrics? metrics = MainApplication.Instance?.Resources?.DisplayMetrics;
        return metrics is null ? new(1080, 1920) : new(metrics.WidthPixels, metrics.HeightPixels);
    }
    public System.Drawing.Point GetCursorPosition() => System.Drawing.Point.Empty;
    public float GetDpiScale() => MainApplication.Instance?.Resources?.DisplayMetrics?.Density ?? 2f;

    public Task<Alife.Client.IAlifeWindow> CreateWindowAsync(Alife.Client.AlifeWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Alife.Client.IAlifeWindow window = windowManager.Create(options);
        return Task.FromResult(window);
    }
}

/// <summary>全局路径，自动启动标记等。</summary>
public static class KeepAlivePaths
{
    public static string AutoStartFile { get; } =
        Path.Combine(MainApplication.Instance!.FilesDir!.AbsolutePath, "Alife", "AutoStart");
}
