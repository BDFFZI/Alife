using Android.App;
using Android.Runtime;

namespace Alife.Client.Android;

[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    /// <summary>全局实例：供悬浮窗、路径等脱离 Activity 的场景使用。</summary>
    public static MainApplication? Instance { get; private set; }

    /// <summary>客户端版本号，取自 APK 的 VersionName。</summary>
    public static string ClientVersion { get; private set; } = "0.0.0";

    protected override MauiApp CreateMauiApp()
    {
        Instance = this;
        InitializeEnvironment();
        return MauiProgram.CreateMauiApp();
    }

    /// <summary>进程启动时设置环境变量与版本号；Storage/Runtime 由 AndroidAlifeFoundation 管理。</summary>
    void InitializeEnvironment()
    {
        string home = FilesDir!.AbsolutePath;
        Environment.SetEnvironmentVariable("HOME", home);
        Environment.SetEnvironmentVariable("TMPDIR", CacheDir!.AbsolutePath);
        Environment.SetEnvironmentVariable("DOTNET_CLI_HOME", home);
        Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
        Environment.SetEnvironmentVariable("DOTNET_NOLOGO", "1");

        ClientVersion = PackageManager?.GetPackageInfo(PackageName!, 0)?.VersionName ?? "0.0.0";
    }
}
