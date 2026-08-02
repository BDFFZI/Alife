using Alife.Framework;
using Alife.Function.Mcp;
using Microsoft.Extensions.DependencyInjection;

var character = new Character {
    Name = "开发测试助手",
    Modules = [
        typeof(McpService).FullName!
    ]
};
await DemoSuite.Run(character, provider => {
    ConfigurationSystem configurationSystem = provider.GetRequiredService<ConfigurationSystem>();
    configurationSystem.SetConfiguration(typeof(McpService), new McpServerConfig {
        Servers = [
            new() {
                Name = "EnhancedBing",
                Description = "一个基于 MCP (Model Context Protocol) 的中文必应搜索工具。",
                Command = "npx",
                Arguments = ["-y", "bing-cn-mcp-enhanced"]
            }
        ]
    },character.StorageKey);
});
