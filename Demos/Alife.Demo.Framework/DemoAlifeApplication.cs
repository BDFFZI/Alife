using System.Drawing;
using Alife.Client;
using Alife.Foundation;
using Alife.Framework;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Alife.Demo.Framework;

public class DemoAlifeApplication : IAlifeApplication
{
    public Task RevealFolderAsync(string path)
    {
        throw new NotSupportedException();
    }
    public Task RevealFileAsync(string path)
    {
        throw new NotSupportedException();
    }
    public Task<string?> SelectFolderAsync()
    {
        throw new NotSupportedException();
    }
    public Size GetScreenSize()
    {
        throw new NotSupportedException();
    }
    public Point GetCursorPosition()
    {
        throw new NotSupportedException();
    }
    public float GetDpiScale()
    {
        throw new NotSupportedException();
    }
    public Task<IAlifeWindow> CreateWindowAsync(AlifeWindowOptions options)
    {
        throw new NotSupportedException();
    }
    public IAlifeProcess StartPython(string scriptPath, string? pythonExe = null)
    {
        throw new NotSupportedException();
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
        webApp.MapMcp("/mcp");
        webApp.Urls.Add("http://127.0.0.1:18765");
        await webApp.StartAsync();

        AlifeMcpServer = new Uri("http://127.0.0.1:18765/mcp");
    }
}