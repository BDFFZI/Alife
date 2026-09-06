using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Alife.Function.FunctionCaller;

public static class McpUtility
{
    /// <summary>
    /// 通过 stdio（本地命令）连接 MCP 服务器。
    /// </summary>
    public static async Task<McpClient> ConnectStdioAsync(
        string name,
        string command,
        string[]? arguments = null,
        ILoggerFactory? loggerFactory = null)
    {
        StdioClientTransportOptions options = new() {
            Name = name,
            Command = command,
            Arguments = arguments ?? []
        };

        return await McpClient.CreateAsync(new StdioClientTransport(options), loggerFactory: loggerFactory);
    }

    /// <summary>
    /// 通过 HTTP（流式传输）连接 MCP 服务器。
    /// </summary>
    /// <param name="name">连接名称。</param>
    /// <param name="endpoint">服务器端点。</param>
    /// <param name="loggerFactory">日志工厂。</param>
    /// <param name="additionalHeaders">附加请求头（如 Bearer Token 鉴权）。</param>
    public static async Task<McpClient> ConnectHttpAsync(
        string name,
        Uri endpoint,
        ILoggerFactory? loggerFactory = null,
        IDictionary<string, string>? additionalHeaders = null)
    {
        HttpClientTransportOptions options = new() {
            Name = name,
            Endpoint = endpoint
        };
        if (additionalHeaders is { Count: > 0 })
            options.AdditionalHeaders = additionalHeaders;

        return await McpClient.CreateAsync(new HttpClientTransport(options), loggerFactory: loggerFactory);
    }

    /// <summary>
    /// 生成单个 MCP 客户端的 JSON 工具文档（供使用方在激活时注入提示词），保留原始 JSON Schema。
    /// 服务器名称与描述取自 <see cref="McpClient.ServerInfo"/>。
    /// </summary>
    public static async Task<string> BuildToolsJsonDocumentAsync(McpClient client)
    {
        var tools = await client.ListToolsAsync();
        string name = client.ServerInfo.Name;
        string? description = client.ServerInfo.Description;

        // UnsafeRelaxedJsonEscaping：保留引号/反斜杠等为可读形式，避免 \u0022、\\ 之类转义影响 AI 阅读
        return JsonSerializer.Serialize(new Dictionary<string, object?> {
            ["server"] = name,
            ["description"] = description,
            ["instructions"] = client.ServerInstructions,
            ["tools"] = tools.Select(t => new Dictionary<string, object?> {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = t.JsonSchema,
            }).ToList(),
        }, new JsonSerializerOptions {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}