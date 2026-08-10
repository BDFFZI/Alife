using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Newtonsoft.Json;

namespace Alife.Function.Mcp;

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
    public bool IsImplicit { get; set; } = true;
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
                ? await McpXmlAdapter.ConnectHttpAsync(server.Name, new Uri(server.Endpoint), loggerFactory)
                : await McpXmlAdapter.ConnectStdioAsync(server.Name, server.Command, server.Arguments, loggerFactory);
            XmlHandler handler = await McpXmlAdapter.McpClientToXmlHandler(
                client,
                server.Name,
                server.Description,
                (name, result) => interactor.Poke($"{server.Name}.{name} 执行完成\n{result}")
            );

            mcpClients.Add(client);
            functionService.RegisterHandler(
                handler,
                server.IsImplicit ? DocumentMode.Implicit : DocumentMode.Explicit,
                DestroyCancellationToken
            );
        }
    }

    protected override async Task OnDestroy()
    {
        foreach (McpClient client in mcpClients)
            await client.DisposeAsync();
    }
}