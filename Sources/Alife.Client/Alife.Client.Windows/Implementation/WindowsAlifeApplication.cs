using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Alife.Foundation;
using Alife.Framework;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Alife.Client.Windows;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial class WindowsAlifeApplication
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
public partial class WindowsAlifeApplication(AlifeClientConfig config) : IAlifeApplication
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
        return Task.Run(() => {
            using FolderBrowserDialog dialog = new();
            dialog.Description = "请选择文件夹";
            dialog.ShowNewFolderButton = true;

            if (dialog.ShowDialog() == DialogResult.OK)
                return dialog.SelectedPath;
            return null;
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

    public Uri? AlifeMcpServer { get; private set; }
    public async Task StartAlifeMcpAsync(AlifeMcpTools alifeMcpTools)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(alifeMcpTools);
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