using Android.App;
using Android.Runtime;
using Application = Microsoft.Maui.Controls.Application;

namespace Alife.Client.Android;

[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    /// <summary>全局实例：供悬浮窗、路径等脱离 Activity 的场景使用。</summary>
    public static MainApplication? Instance { get; private set; }

    /// <summary>客户端版本号，取自 APK 的 VersionName。</summary>
    public static string ClientVersion { get; private set; } = null!;

    protected override MauiApp CreateMauiApp()
    {
        Instance = this;
        InitializeEnvironment();
        return BuildMauiApp();
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
    MauiApp BuildMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        //进程内宿主：直接注册 Alife 框架与客户端（不再有独立 backend 进程）
        builder.Services.AddAlifeClient<
            AndroidAlifeFoundation,
            AndroidCSharpCompiler,
            AndroidNuGetEnvironmentInstaller,
            AndroidPipEnvironmentInstaller,
            AndroidAlifeApplication,
            AndroidAlifeClient>();
        builder.Services.AddSingleton<AndroidWindowManager>();

        return builder.Build();
    }
}

/// <summary>MAUI 应用入口。</summary>
public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(new MainPage());
}