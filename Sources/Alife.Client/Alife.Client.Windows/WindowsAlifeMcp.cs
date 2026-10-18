using Alife.Foundation;
using Alife.Framework;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Alife.Client.Windows;

/// <summary>桌面端 MCP 传输：ASP.NET 自托管 HTTP。</summary>
public sealed class WindowsAlifeMcp(IServiceProvider provider) : IAlifeMcp
{
    public Uri? Server { get; private set; }
    public async Task StartAsync(string address = "http://127.0.0.1:18765", string path = "/mcp")
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(provider.GetRequiredService<CharacterSystem>());
        builder.Services.AddSingleton(provider.GetRequiredService<ChatActivitySystem>());
        builder.Services.AddSingleton(provider.GetRequiredService<PluginSystem>());
        builder.Services.AddSingleton(provider.GetRequiredService<ModuleSystem>());
        builder.Services.AddSingleton(provider.GetRequiredService<ConfigurationSystem>());
        builder.Services.AddSingleton<AlifeMcpTools>();
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
        webApp.MapMcp(path);
        webApp.Urls.Add(address);
        await webApp.StartAsync();

        Server = new Uri(address + path);
    }
}