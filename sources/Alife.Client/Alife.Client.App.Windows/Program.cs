using System.Text;
using Alife.Client.Core;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Web.WebView2.WinForms;
using MenuItem = ElectronNET.API.Entities.MenuItem;

namespace Alife;

public static class Program
{
    public static BrowserWindow MainWindow { get; private set; } = null!;

    static void Main(string[] args)
    {
        //TODO 为了支持基于 WebView2 的浏览器插件而加载，未来应当去除
        Console.WriteLine(typeof(WebView2).Assembly);

        //控制台编码设置
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        //功能注册
        var builder = WebApplication.CreateBuilder(args);
        {
            //前端框架
            builder.Services.AddRazorComponents().AddInteractiveServerComponents();
            //前端组件库
            builder.Services.AddAntDesign();
            //前端载体
            if (Environment.GetEnvironmentVariable("DISABLE_ElectronENT") == null)
            {
                builder.Services.AddElectron();
                builder.UseElectron(args, OnElectronAppReady);
            }

            //Alife后端
            builder.Services.AddAlifeClient<WindowsAlifeClient>();
        }

        //功能配置
        {
            //提高Blazor Server信号传输上限，支持超长文本输入
            builder.Services.Configure<HubOptions>(o => o.MaximumReceiveMessageSize = 1024 * 1024);
        }

        var app = builder.Build();

        app.UseAntiforgery();
        app.MapStaticAssets(); //使用blazor的系统文件（同时为 @Assets 提供带指纹的资源路由）
        app.MapRazorComponents<Alife.Client.UI.App>()
            .AddInteractiveServerRenderMode(); //Alife前端

        app.Run();
    }

    static async Task OnElectronAppReady()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "alife-icon.ico");

        //创建窗口
        var browserOptions = new BrowserWindowOptions {
            Title = "Alife",
            Icon = iconPath,
            Width = 1300,
            Height = 800,
            IsRunningBlazor = true,
        };
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            browserOptions.AutoHideMenuBar = true;
        MainWindow = await Electron.WindowManager.CreateWindowAsync(browserOptions);

        //创建托盘
        var menuItems = new[] {
            new MenuItem { Label = "显示主窗口", Click = MainWindow.Show },
            new MenuItem { Label = "显示网页工具", Click = MainWindow.WebContents.ToggleDevTools },
            new MenuItem { Label = "退出", Click = () => Electron.App.Exit() }
        };
        await Electron.Tray.Show(iconPath, menuItems);
        await Electron.Tray.SetToolTip("Alife");
        Electron.Tray.OnClick += async (_, _) =>
        {
            if (await MainWindow.IsVisibleAsync()) MainWindow.Hide();
            else MainWindow.Show();
        };
    }
}