using System.Diagnostics;
using System.Text;
using Alife.Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alife.Client.Windows;

static class Program
{
    [STAThread]
    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        TaskScheduler.UnobservedTaskException += UnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += UnhandledException;
        Application.ThreadException += (_, e) => HandleException(e.Exception, nameof(Application.ThreadException), false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ServiceCollection services = new();
        services.AddLogging(builder => {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        //Alife客户端
        services.AddAlifeClient<WindowsAlifeFoundation, WindowsCSharpCompiler,
            WindowsNuGetEnvironmentInstaller, WindowsPipEnvironmentInstaller,
            WindowsAlifeApplication, WindowsAlifeClient>();
        //呈现框架
        services.AddWindowsFormsBlazorWebView();
        services.AddBlazorWebViewDeveloperTools();
        services.AddSingleton<MainForm>();
        services.AddSingleton<IAlifeClientInitialize, WindowsAlifeClientInitialize>();
        ServiceProvider provider = services.BuildServiceProvider();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); //使Form使用物理像素渲染，从而解决画面模糊问题，但因此需要手动转换Dpi坐标。（webView不受影响，其始终使用转换后的坐标）
        Application.EnableVisualStyles(); //现代化 Form 预设组件样式
        Application.Run(provider.GetRequiredService<MainForm>());
        Environment.Exit(0);
    }

    static void UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleException(e.Exception, nameof(UnobservedTaskException), false);
        e.SetObserved();
    }
    static void UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            HandleException(ex, nameof(UnhandledException), true);
    }
    static void HandleException(Exception exception, string source, bool termination)
    {
        try
        {
            string logDir = Path.Combine(AlifePath.TempFolderPath, "Logs");
            Directory.CreateDirectory(logDir);
            string exceptionFilePath = Path.Combine(logDir, $"error-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            string content = $"""
                              发生时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}
                              异常来源: {source}

                              === 详细报错 ===
                              {exception}
                              """;
            File.WriteAllText(exceptionFilePath, content);

            string message = $"""
                              程序发生未处理的异常：
                              {exception.Message}

                              详情信息已保存至:
                              {exceptionFilePath}
                              """;

            MessageBox.Show(message, "Alife - 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{exceptionFilePath}\"", UseShellExecute = true });

            if (termination)
                Application.Exit();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}

class WindowsAlifeClientInitialize : IAlifeClientInitialize
{
    public async Task OnInitializeAsync(IProgress<string>? progress)
    {
        //重定向环境变量，例如修正python路径
        progress?.Report("正在初始化系统环境...");
        await Task.Run(WindowsEnvironment.SyncEnvironment);
    }
}