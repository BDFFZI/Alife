using System.Text;
using Alife.Framework;
using Alife.Components.Services;
using ElectronNET.API;
using ElectronNET.API.Entities;
using MenuItem=ElectronNET.API.Entities.MenuItem;
using MessageBoxOptions=ElectronNET.API.Entities.MessageBoxOptions;

namespace Alife;

public static class Program
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    static void Main(string[] args)
    {
#if DEBUG
        Console.WriteLine(typeof(Function.Memory.MemoryService).Assembly.FullName);
        Console.WriteLine(typeof(Function.MessageFilter.MessageFilterService).Assembly);
        Console.WriteLine(typeof(Function.SystemEvent.SystemEventService).Assembly);
        Console.WriteLine(typeof(Function.VirtualWorld.VirtualWorldService).Assembly);
        Console.WriteLine(typeof(Function.Developer.DeveloperService).Assembly);

        Console.WriteLine(typeof(Function.FunctionCaller.XmlFunctionCaller).Assembly);
        Console.WriteLine(typeof(Function.Mcp.McpService).Assembly);
        Console.WriteLine(typeof(Function.Skill.SkillService).Assembly);

        Console.WriteLine(typeof(Function.Browser.BrowserService).Assembly);
        Console.WriteLine(typeof(Function.Python.PythonService).Assembly);
        Console.WriteLine(typeof(Function.Vision.VisionService).Assembly);
        Console.WriteLine(typeof(Function.FileService.FileService).Assembly);
        Console.WriteLine(typeof(Function.ProcessService.ProcessService).Assembly);

        Console.WriteLine(typeof(Function.Auditory.AuditoryService).Assembly);
        Console.WriteLine(typeof(Function.DeskPet.DeskPetService).Assembly);
        Console.WriteLine(typeof(Function.QChat.QChatService).Assembly);
        Console.WriteLine(typeof(Function.Speech.SpeechService).Assembly);
        
        Console.WriteLine(typeof(Function.Auditory.SenseVoice.SenseVoiceAuditoryModel).Assembly);
        Console.WriteLine(typeof(Function.Speech.EdgeTTS.EdgeSpeechModel).Assembly);
        Console.WriteLine(typeof(Function.Speech.Genie.GenieSpeechModel).Assembly);
        Console.WriteLine(typeof(Function.Speech.VITS.VitsSpeechModel).Assembly);
        Console.WriteLine(typeof(Function.Vision.MiniCPM.MiniCPMVisionModel).Assembly);
        Console.WriteLine(typeof(Function.Vision.OpenAI.OpenAIVisionModel).Assembly);
        Console.WriteLine(typeof(Function.Vision.Qwen.QwenVisionModel).Assembly);
#endif
        //TODO 为了支持基于 WebView2 的浏览器插件而加载，未来应当去除
        Console.WriteLine(typeof(Form).Assembly);

        //控制台编码设置
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        //业务功能注册
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
            //后端功能
            builder.Services.AddAlife();
            //前端功能
            builder.Services.AddSingleton<ChatMessageService>();
            builder.Services.AddSingleton<PluginMarketService>();
            builder.Services.AddSingleton<UpdateService>();
            builder.Services.AddSingleton<EnvironmentInstaller>();
        }

        var app = builder.Build();
        ServiceProvider = app.Services;

        app.UseAntiforgery();
        app.UseStaticFiles();
        app.MapRazorComponents<Components.App>()
            .AddInteractiveServerRenderMode();
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
        var browserWindow = await Electron.WindowManager.CreateWindowAsync(browserOptions);

        //创建托盘
        var menuItems = new[] {
            new MenuItem { Label = "显示主窗口", Click = () => browserWindow.Show() },
            new MenuItem { Label = "退出", Click = () => Electron.App.Exit() }
        };
        await Electron.Tray.Show(iconPath, menuItems);
        await Electron.Tray.SetToolTip("Alife");
        Electron.Tray.OnClick += async (_, _) => {
            if (await browserWindow.IsVisibleAsync()) browserWindow.Hide();
            else browserWindow.Show();
        };

        //自定义关闭事件
        await Electron.IpcMain.On("request-close", _ => {
            var options = new MessageBoxOptions("是否直接关闭应用？") {
                Title = "Alife",
                Buttons = ["取消", "是 - 直接关闭", "否 - 最小化到托盘"],
                Type = MessageBoxType.question,
            };
            var result = Electron.Dialog.ShowMessageBoxAsync(browserWindow, options).Result;
            if (result.Response == 1) Electron.App.Exit();
            else if (result.Response == 2) browserWindow.Hide();
        });
    }
}
