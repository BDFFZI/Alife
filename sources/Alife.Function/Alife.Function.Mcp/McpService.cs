using System.Collections.Generic;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Alife.Function.Mcp;

public class McpServerItem
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Unnamed MCP Server";
    public string Description { get; set; } = "";
    public string Command { get; set; } = "";
    public string[] Arguments { get; set; } = [];
    public bool IsImplicit { get; set; } = true;
}

public class McpServerConfig
{
    public List<McpServerItem> Servers { get; set; } = new();
}

[Module("MCP服务",
    "让AI可以通过Model Context Protocol接入外部工具。",
    defaultCategory: "Alife 官方/功能底座",
    editorUI: typeof(McpServiceUI))]
public class McpService(
    XmlFunctionCaller functionService,
    ILoggerFactory loggerFactory,
    IInteractor<McpService> interactor) :
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

            (McpClient client, XmlHandler handler) = await McpXmlAdapter.CreateAsync(
                server,
                (name, result) => interactor.Poke($"{server.Name}.{name} 执行完成\n{result}"),
                loggerFactory
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
