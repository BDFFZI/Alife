using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Newtonsoft.Json;

namespace Alife.Function.Mcp;

/// <summary>
/// MCP 服务器的注册方式：以 XML 函数方式还是 JSON-RPC 方式暴露给 AI。
/// </summary>
public enum McpRegisterMode
{
    Xml,
    Json,
}

public class McpServerItem
{
    [JsonIgnore]
    public bool IsUrlServer { get => string.IsNullOrWhiteSpace(Endpoint) == false; }

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Unnamed MCP Server";
    public string Description { get; set; } = "";
    public string Command { get; set; } = "";
    public string[] Arguments { get; set; } = [];
    public string Endpoint { get; set; } = "";
    /// <summary>
    /// Bearer Token（可选）。配置后将以 `Authorization: Bearer xxx` 请求头发送，
    /// 用于访问需要鉴权的 HTTP MCP 服务器。
    /// </summary>
    public string Token { get; set; } = "";
    public bool IsImplicit { get; set; } = true;
    /// <summary>
    /// 注册方式：Xml 以 Xml 函数暴露，Json 以 JSON-RPC 方式暴露。默认 Json。
    /// </summary>
    public McpRegisterMode RegisterMode { get; set; } = McpRegisterMode.Json;
}

public class McpServerConfig
{
    public List<McpServerItem> Servers { get; init; } = [];
}

[Module("MCP服务",
    "让AI可以通过Model Context Protocol接入外部工具。",
    defaultCategory: "Alife 官方/功能底座",
    editorUI: typeof(McpServiceUI))]
public class McpService(
    XmlFunctionCaller functionService,
    McpFunctionCaller mcpFunctionCaller,
    ILoggerFactory loggerFactory,
    Interactor<McpService> interactor) :
    ChatBehaviour,
    IConfigurable<McpServerConfig>
{
    public McpServerConfig Configuration { get; set; } = null!;

    readonly List<McpClient> mcpClients = new();

    protected override async Task OnAwake()
    {
        foreach (McpServerItem server in Configuration.Servers)
        {
            if (server.Enabled == false) continue;

            McpClient client = server.IsUrlServer
                ? await McpUtility.ConnectHttpAsync(
                    server.Name,
                    new Uri(server.Endpoint),
                    loggerFactory,
                    string.IsNullOrWhiteSpace(server.Token)
                        ? null
                        : new Dictionary<string, string> {
                            ["Authorization"] = "Bearer " + server.Token
                        })
                : await McpUtility.ConnectStdioAsync(server.Name, server.Command, server.Arguments, loggerFactory);

            mcpClients.Add(client);
            DocumentMode documentMode = server.IsImplicit ? DocumentMode.Implicit : DocumentMode.Explicit;

            if (server.RegisterMode == McpRegisterMode.Json)
            {
                // JSON-RPC 方式：以配置名作为路由名称，注册到 McpFunctionCaller，AI 通过 <JsonRpcMcp> 调用
                client.ServerInfo.Name = server.Name;
                client.ServerInfo.Description = server.Description;
                await mcpFunctionCaller.RegisterMcpClientAsync(client, documentMode, DestroyCancellationToken);
            }
            else
            {
                // Xml 方式：转换为 XmlHandler 注册到 XmlFunctionCaller
                XmlHandler handler = await McpXmlAdapter.McpClientToXmlHandler(
                    client,
                    server.Name,
                    server.Description,
                    (name, result) => interactor.Poke($"{server.Name}.{name} 执行完成\n{result}")
                );
                functionService.RegisterHandler(
                    handler,
                    documentMode,
                    DestroyCancellationToken
                );
            }
        }
    }

    protected override async Task OnDestroy()
    {
        foreach (McpClient client in mcpClients)
            await client.DisposeAsync();
    }
}