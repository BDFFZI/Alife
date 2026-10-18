using Alife.Client;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Client.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
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
            AndroidAlifeClient,
            AndroidAlifeMcp>();
        builder.Services.AddSingleton<AndroidWindowManager>();

        return builder.Build();
    }
}

/// <summary>MAUI 应用入口。</summary>
public class App : Microsoft.Maui.Controls.Application
{
    protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState)
        => new Microsoft.Maui.Controls.Window(new MainPage());
}
