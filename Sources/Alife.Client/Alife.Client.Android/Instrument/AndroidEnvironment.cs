using Android.Content;
using Android.Provider;
using Alife.Client;
using Alife.Foundation;

namespace Alife.Client.Android;

public static class AndroidEnvironment
{
    public static bool IsStorageReady { get; private set; }
    public static bool IsShizukuReady { get; private set; }
    public static bool IsPythonReady { get; private set; }
    public static bool IsDotNetReady { get; private set; }

    public static IReadOnlyList<EnvironmentItem> GetEnvironmentItems()
    {
        SyncEnvironment();
        return [
            new("storage", "所有文件访问", IsStorageReady, true),
            new("shizuku", "Shizuku", IsShizukuReady, true),
            new("python", "Python 3.14 + pip", IsPythonReady, true),
            new("dotnet", ".NET SDK 10（NuGet 还原）", IsDotNetReady, true),
        ];
    }

    public static async Task InstallEnvironmentAsync(string id, IProgress<string>? progress = null)
    {
        switch (id)
        {
            case "storage":
                await InstallStorageAsync();
                break;
            case "shizuku":
                await InstallShizukuAsync();
                break;
            case "python":
                await InstallPythonAsync(progress);
                break;
            case "dotnet":
                await InstallDotNetAsync(progress);
                break;
            default:
                throw new ArgumentException("未知的环境项。", nameof(id));
        }
        SyncEnvironment();
    }

    public static void SyncEnvironment()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
#pragma warning disable CA1416
            IsStorageReady = global::Android.OS.Environment.IsExternalStorageManager;
#pragma warning restore CA1416
        }
        else
        {
            IsStorageReady = true;
        }

        IsShizukuReady = ShizukuBridge.IsAvailable && ShizukuBridge.HasPermission;
        IsPythonReady = IsShizukuReady && ShizukuPythonInstaller.IsInstalled;
        IsDotNetReady = Directory.Exists(new DotNetInstallation().SdkPath);
    }

    static async Task InstallStorageAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30) == false)
            return;

        if (ShizukuBridge.IsAvailable && ShizukuBridge.HasPermission)
        {
            try
            {
                string packageName = MainApplication.Instance?.PackageName ?? "com.alife.client";
                await ShizukuBridge.ExecuteAsync($"appops set {packageName} MANAGE_EXTERNAL_STORAGE allow");
            }
            catch (Exception e)
            {
                AlifeLog.LogWarning("通过 Shizuku 授予所有文件访问权限失败：" + e.Message);
            }
        }

#pragma warning disable CA1416
        bool granted = global::Android.OS.Environment.IsExternalStorageManager;
#pragma warning restore CA1416
        if (granted)
        {
            if (AlifeFoundation.Instance is AndroidAlifeFoundation foundation)
                await foundation.TryUseDocumentsStorageAsync();
            return;
        }

#pragma warning disable CA1416
        Intent intent = new(
            Settings.ActionManageAppAllFilesAccessPermission,
            global::Android.Net.Uri.Parse("package:" + (MainApplication.Instance?.PackageName ?? "com.alife.client")));
#pragma warning restore CA1416
        intent.AddFlags(ActivityFlags.NewTask);
        MainApplication.Instance?.StartActivity(intent);

        throw new InvalidOperationException("请在系统设置中授予所有文件访问权限，然后返回重试。");
    }

    static async Task InstallShizukuAsync()
    {
        if (ShizukuBridge.IsAvailable == false)
        {
            Intent? launch = MainApplication.Instance?.PackageManager?
                .GetLaunchIntentForPackage("moe.shizuku.privileged.api");
            if (launch == null)
                throw new InvalidOperationException("请先安装 Shizuku。");

            launch.AddFlags(ActivityFlags.NewTask);
            MainApplication.Instance!.StartActivity(launch);
            await ShizukuBridge.WaitForBinderAsync(TimeSpan.FromSeconds(15));
        }

        if (ShizukuBridge.IsAvailable == false)
            throw new InvalidOperationException("Shizuku 未运行，请在 Shizuku 中启动服务。");
        if (ShizukuBridge.HasPermission == false)
            await ShizukuBridge.RequestPermissionAsync();
        if (ShizukuBridge.HasPermission == false)
            throw new InvalidOperationException("未获得 Shizuku 授权。");
    }

    static async Task InstallPythonAsync(IProgress<string>? progress)
    {
        if (ShizukuBridge.IsAvailable == false || ShizukuBridge.HasPermission == false)
            throw new InvalidOperationException("请先完成 Shizuku 连接与授权。");

        if (AlifeFoundation.Instance is AndroidAlifeFoundation foundation)
            await foundation.TryUseDocumentsStorageAsync();

        if (IsStorageShellReadable() == false)
            throw new InvalidOperationException("Python 安装需要 Storage 位于文档目录，请先授予所有文件访问权限并重启应用。");

        await ShizukuPythonInstaller.InstallAsync(progress, CancellationToken.None);
    }

    static async Task InstallDotNetAsync(IProgress<string>? progress)
    {
        DotNetInstallation installation = new();
        await installation.EnsureAsync((message, _, _) => progress?.Report(message), CancellationToken.None);
    }

    static bool IsStorageShellReadable()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30) == false)
            return true;

#pragma warning disable CA1416
        string? documents = global::Android.OS.Environment
            .GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDocuments)?
            .AbsolutePath;
#pragma warning restore CA1416
        if (string.IsNullOrEmpty(documents))
            return false;

        return Path.GetFullPath(AlifePath.StorageFolderPath)
            .StartsWith(Path.GetFullPath(documents), StringComparison.OrdinalIgnoreCase);
    }
}
