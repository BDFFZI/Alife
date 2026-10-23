using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Alife.Foundation;
using Alife.Framework;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Alife.Client.Windows;

[SuppressMessage("ReSharper", "InconsistentNaming")]
partial class WindowsAlifeApplication
{
    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    const int SM_CXSCREEN = 0; // 主屏幕宽度
    const int SM_CYSCREEN = 1; // 主屏幕高度

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();
}

/// <summary>桌面端 MCP 传输：ASP.NET 自托管 HTTP。</summary>
partial class WindowsAlifeApplication(AlifeClientConfig config) : IAlifeApplication
{
    public Task RevealFolderAsync(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true });
        return Task.CompletedTask;
    }
    public Task RevealFileAsync(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = true });
        return Task.CompletedTask;
    }
    public Task<string?> SelectFolderAsync()
    {
        Form? owner = Application.OpenForms.OfType<MainForm>().FirstOrDefault()
                      ?? Application.OpenForms.Cast<Form>().FirstOrDefault();
        if (owner == null)
            return Task.FromResult<string?>(null);

        return owner.InvokeAsync(() => {
            using FolderBrowserDialog dialog = new() {
                Description = "请选择文件夹",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
        });
    }

    public Size GetScreenSize()
    {
        double width = GetSystemMetrics(SM_CXSCREEN);
        double height = GetSystemMetrics(SM_CYSCREEN);
        return new Size((int)width, (int)height);
    }
    public Point GetCursorPosition()
    {
        GetCursorPos(out POINT point);
        return new Point(point.X, point.Y);
    }
    public float GetDpiScale()
    {
        return (float)(GetDpiForSystem() / 96.0);
    }
    public async Task<IAlifeWindow> CreateWindowAsync(AlifeWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 每个 Html 二级窗口（桌宠等）一条专属 STA UI 线程，窗口之间、与主窗口互相隔离。
        TaskCompletionSource<IAlifeWindow> tcs = new();
        Thread thread = new(_ => {
            WindowsAlifeWindow alifeWindow = new(options);
            alifeWindow.FormClosed += (_, _) => {
                Application.ExitThread();
            };
            tcs.SetResult(alifeWindow);
            Application.Run(alifeWindow);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = options.Title;
        thread.Start();
        return await tcs.Task;
    }

    public IAlifeProcess StartPython(string scriptPath, string? pythonExe = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(scriptPath);
        ProcessStartInfo startInfo = new() {
            FileName = string.IsNullOrWhiteSpace(pythonExe) ? "python" : pythonExe,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
        };
        startInfo.ArgumentList.Add(scriptPath);

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return new WindowsPythonProcess(process);
    }

    public Uri? AlifeMcpServer { get; private set; }
    public async Task StartAlifeMcpAsync(IServiceProvider provider)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(ActivatorUtilities.CreateInstance<AlifeMcpTools>(provider));
        //将AlifeMcpTools依赖的框架服务从主容器映射到MCP容器，避免激活时无法解析
        foreach (Type t in typeof(AlifeMcpTools).GetConstructors().First().GetParameters().Select(p => p.ParameterType))
            builder.Services.AddSingleton(t, provider.GetRequiredService(t));
        builder.Services.AddMcpServer(options => {
            options.ServerInfo = new Implementation {
                Name = "Alife MCP Server",
                Version = AlifeContext.AppVersion,
                Title = "Alife MCP Server",
                Description = "Alife 是一款 AIAgent 软件，而此 MCP 可绕过图形化界面，通过 AI 友好的方式控制它，从而便于开发工作。",
                WebsiteUrl = "https://github.com/BDFFZI/Alife"
            };
            options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) => {
                try
                {
                    return await next(request, cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException && e is not ModelContextProtocol.McpProtocolException)
                {
                    return new CallToolResult {
                        IsError = true,
                        Content = [new TextContentBlock { Text = e.ToString() }],
                    };
                }
            });
        }).WithTools<AlifeMcpTools>().WithHttpTransport();

        WebApplication webApp = builder.Build();
        webApp.MapMcp(config.McpServerPath);
        webApp.Urls.Add(config.McpServerAddress);
        await webApp.StartAsync();

        AlifeMcpServer = new Uri(config.McpServerAddress + config.McpServerPath);
    }
}