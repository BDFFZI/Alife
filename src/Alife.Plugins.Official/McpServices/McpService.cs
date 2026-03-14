using Alife.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using ModelContextProtocol.Client;
using Alife.Plugins.Official.Components;

namespace Alife.Plugins.Official.Implement;

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
[Plugin("MCP插件", "让AI可以通过Model Context Protocol接入外部工具。",
    configurationUIType: typeof(McpServiceUI))]
public class McpService : IPlugin, IConfigurable<McpPluginConfig>
{
    public void Configure(McpPluginConfig configuration)
    {
        this.configuration = configuration;
    }

    public async Task AwakeAsync(IKernelBuilder kernelBuilder, ChatHistoryAgentThread agentThread)
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
                    kernelBuilder.Plugins.AddFromFunctions(
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
