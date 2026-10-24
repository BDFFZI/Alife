using Alife.Foundation;
using Alife.Framework;

namespace Alife.Client.Android;

sealed class AndroidAlifeClient(AndroidAlifeFoundation foundation, IServiceProvider provider) : IAlifeClient
{
    public async Task UpgradeClient(string version, Action<int>? onProgress = null)
    {
        //按 tag 精确取该版本，避免检查与安装之间版本漂移
        string downloadUrl = $"{ClientUpgrade.ReleasesDownloadUrl}/v{version}/{UpdateAssetName}";

        string updateDir = Path.Combine(foundation.TempFolderPath, "Update");
        if (Directory.Exists(updateDir))
            Directory.Delete(updateDir, true);

        string packagePath = Path.Combine(updateDir, UpdateAssetName);
        await AlifeUtility.DownloadFileAsync(downloadUrl, packagePath, (read, total) => {
            if (total > 0)
                onProgress?.Invoke((int)(read * 100 / total));
        });

        AndroidPackageInstaller.Install(packagePath);
    }

    public void SetStorageFolderPath(string path)
    {
        foundation.SetStorageFolderPath(path);
    }

    public void SetRuntimeFolderPath(string path)
    {
        foundation.SetRuntimeFolderPath(path);
    }

    public void SetTempFolderPath(string path)
    {
        foundation.SetTempFolderPath(path);
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

    public IReadOnlyList<string> GetRecommendedPluginIds()
    {
        return
        [
            //地基
            "Alife.Function.FunctionCaller",
            "Alife.Function.Mcp",
            "Alife.Function.Skill",
            //环境
            "Alife.Function.Developer",
            "Alife.Function.Memory",
            "Alife.Function.MessageFilter",
            "Alife.Function.SystemEvent",
            "Alife.Function.VirtualWorld",
            //工具
            "Alife.Function.FileService",
            "Alife.Function.Execution",
            "Alife.Function.TimedTask",
            "Alife.Function.Todo",
            "Alife.Function.Vision",
            //交互
            "Alife.Function.DeskPet",
            "Alife.Function.Speech",
            //感官
            "Alife.Function.Language.OpenAI",
            "Alife.Function.Auditory.SenseVoice",
            "Alife.Function.Speech.VITS",
        ];
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

    const string UpdateAssetName = "Alife.Client.Android.apk";
}

/// <summary>全局路径与自启动标记等常量。</summary>
static class KeepAlivePaths
{
    public static string AutoStartFile { get; } =
        Path.Combine(MainApplication.Instance!.FilesDir!.AbsolutePath, "Alife", "AutoStart");
}