using Alife.Framework;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;

namespace Alife.Implement;

public class McpServerConfig
{
    public string Name { get; set; } = "Unnamed MCP Server";
    public string Description { get; set; } = "";
    public string Command { get; set; } = "";
    public string[] Arguments { get; set; } = [];
}
public class McpPluginConfig
{
    public List<McpServerConfig> Servers { get; set; } = new();
}
[Plugin("框架-MCP服务", "让AI可以通过Model Context Protocol接入外部工具。",
    configurationUIType: typeof(McpServiceUI))]
public class McpService : Plugin, IConfigurable<McpPluginConfig>
{
    public void Configure(McpPluginConfig configuration)
    {
        this.configuration = configuration;
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        foreach (McpServerConfig server in configuration.Servers)
        {
            try
            {
                StdioClientTransport clientTransport = new(new StdioClientTransportOptions {
                    Name = server.Name,
                    Command = server.Command,
                    Arguments = server.Arguments
                });
                McpClient client = await McpClient.CreateAsync(clientTransport);
                IList<McpClientTool> mcpTools = await client.ListToolsAsync();

                if (mcpTools.Count > 0)
                {
                    context.kernelBuilder.Plugins.AddFromFunctions(
                        server.Name,
                        server.Description,
                        mcpTools.Select(tool => tool.AsKernelFunction())
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load MCP server {server.Name}: {ex.Message}");
            }
        }
    }

    McpPluginConfig configuration = new();
}
