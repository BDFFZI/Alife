using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Alife.Function.Mcp;

/// <summary>
/// 业务无关的 MCP 适配器：负责建立 MCP 客户端连接，并把任意 MCP 服务器提供的工具转换为
/// <see cref="XmlHandler"/>，使 AI 可以通过 Xml 函数调用方式使用它们。
/// 支持 stdio（本地命令）与 HTTP（流式传输）两种连接方式。
/// </summary>
public static class McpXmlAdapter
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
    public static async Task<McpClient> ConnectHttpAsync(
        string name,
        Uri endpoint,
        ILoggerFactory? loggerFactory = null)
    {
        HttpClientTransportOptions options = new() {
            Name = name,
            Endpoint = endpoint
        };

        return await McpClient.CreateAsync(new HttpClientTransport(options), loggerFactory: loggerFactory);
    }

    /// <summary>
    /// 将已连接的 MCP 客户端中的工具转换为 XmlHandler，供 AI 通过 Xml 函数调用。
    /// </summary>
    /// <param name="client">已连接的 MCP 客户端。</param>
    /// <param name="name">XmlHandler 的名称。</param>
    /// <param name="description">XmlHandler 的描述。</param>
    /// <param name="resultCallback">函数执行完成后的回调（参数为小写工具名与结果文本）。</param>
    /// <param name="explanation">附加给 AI 的详细说明。</param>
    public static async Task<XmlHandler> McpClientToXmlHandler(
        McpClient client,
        string name,
        string? description = null,
        Action<string, string>? resultCallback = null,
        string? explanation = null)
    {
        IList<McpClientTool> tools = await client.ListToolsAsync();

        List<XmlFunction> functions = new();
        foreach (McpClientTool tool in tools)
        {
            XmlFunction function = McpClientToolToXmlFunction(tool, client, resultCallback);
            functions.Add(function);
        }

        return new XmlHandler(name) {
            Description = description,
            Explanation = explanation,
            Functions = functions,
        };
    }

    static XmlFunction McpClientToolToXmlFunction(McpClientTool tool, McpClient client, Action<string, string>? resultCallback)
    {
        string name = tool.Name.ToLower();
        string description = tool.Description;
        (List<XmlParameter> parameters, var typeMap) = ParseInputSchema(tool);

        async Task Invoker(XmlContext context, CancellationToken cancellationToken)
        {
            Dictionary<string, object?> arguments = new();
            foreach ((string key, string value) in context.Parameters)
            {
                if (typeMap.TryGetValue(key, out (string OriginalName, string Type) typeInfo))
                {
                    object? convertedValue = ConvertValue(value, typeInfo.Type);
                    arguments[typeInfo.OriginalName] = convertedValue;
                }
                else
                {
                    arguments[key] = value;
                }
            }

            CallToolResult result = await client.CallToolAsync(tool.Name, arguments, cancellationToken: cancellationToken);

            string resultText = string.Join("\n",
                result.Content
                    .Where(block => block is TextContentBlock)
                    .Select(block => ((TextContentBlock)block).Text));

            if (result.IsError == true)
                throw new Exception(resultText);

            resultCallback?.Invoke(name, resultText);
        }

        return new XmlFunction {
            Name = name,
            Description = description,
            Parameters = parameters,
            Invoker = Invoker,
        };
    }

    static object? ConvertValue(string value, string jsonType)
    {
        // 数组类型（integer[]、string[] 等）
        if (jsonType.EndsWith("[]"))
        {
            try
            {
                return JsonSerializer.Deserialize<object>(value);
            }
            catch
            {
                return value;
            }
        }

        switch (jsonType)
        {
            case "number":
                return double.TryParse(value, out double d) ? d : value;
            case "integer":
                return long.TryParse(value, out long l) ? l : value;
            case "boolean":
                return bool.TryParse(value, out bool b) ? b : value;
            case "object":
            case "array":
                try
                {
                    return JsonSerializer.Deserialize<object>(value);
                }
                catch
                {
                    return value;
                }
            default:
                return value;
        }
    }

    static (List<XmlParameter> Parameters, Dictionary<string, (string OriginalName, string Type)> TypeMap)
        ParseInputSchema(McpClientTool tool)
    {
        List<XmlParameter> parameters = new();
        Dictionary<string, (string, string)> typeMap = new();

        JsonElement schema = tool.JsonSchema;
        if (schema.TryGetProperty("properties", out JsonElement properties) == false)
            return (parameters, typeMap);

        JsonElement? requiredArray = schema.TryGetProperty("required", out JsonElement req) ? req : null;
        HashSet<string> requiredSet = new();
        if (requiredArray is { ValueKind: JsonValueKind.Array })
        {
            foreach (JsonElement item in requiredArray.Value.EnumerateArray())
            {
                string? reqName = item.GetString();
                if (reqName != null)
                    requiredSet.Add(reqName);
            }
        }

        foreach (JsonProperty prop in properties.EnumerateObject())
        {
            string paramName = prop.Name.ToLower();
            string jsonType = ResolveType(prop.Value);
            string? paramDescription = null;

            if (prop.Value.TryGetProperty("description", out JsonElement descElem))
                paramDescription = descElem.GetString();

            bool isRequired = requiredSet.Contains(prop.Name);
            bool isNullable = IsNullableType(prop.Value);
            string paramTypeLabel = jsonType;
            if (isRequired == false || isNullable)
                paramTypeLabel += "[可选]";

            parameters.Add(new XmlParameter {
                Name = paramName,
                Description = paramDescription,
                Type = paramTypeLabel,
            });

            typeMap[paramName] = (prop.Name, jsonType);
        }

        return (parameters, typeMap);
    }

    static string ResolveType(JsonElement schema)
    {
        // 1. 直接 enum
        if (schema.TryGetProperty("enum", out JsonElement enumElement))
            return "enum" + enumElement;

        // 2. 直接 type（含 array 处理 items），兼容 type 为数组（如 ["string","null"]）的写法
        if (schema.TryGetProperty("type", out JsonElement typeElem))
        {
            string? type = GetTypeString(typeElem);
            if (type != null)
            {
                if (type == "array" && schema.TryGetProperty("items", out JsonElement items))
                {
                    string itemType = ResolveType(items);
                    return itemType + "[]";
                }
                return type;
            }
        }

        // 3. anyOf 联合类型 — 过滤 null，取第一个非 null 类型
        if (schema.TryGetProperty("anyOf", out JsonElement anyOf) && anyOf.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement branch in anyOf.EnumerateArray())
            {
                if (branch.TryGetProperty("type", out JsonElement branchType) && branchType.GetString() != "null")
                    return ResolveType(branch);
            }
        }

        // 4. oneOf 联合类型 — 同理
        if (schema.TryGetProperty("oneOf", out JsonElement oneOf) && oneOf.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement branch in oneOf.EnumerateArray())
            {
                if (branch.TryGetProperty("type", out JsonElement branchType) && branchType.GetString() != "null")
                    return ResolveType(branch);
            }
        }

        return "string";
    }

    static string? GetTypeString(JsonElement typeElem)
    {
        if (typeElem.ValueKind == JsonValueKind.String)
            return typeElem.GetString();

        // 数组写法，如 ["string","null"]，取第一个非 null 类型
        if (typeElem.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in typeElem.EnumerateArray())
            {
                string? type = item.GetString();
                if (type != null && type != "null")
                    return type;
            }
        }
        return null;
    }

    static bool IsNullableType(JsonElement schema)
    {
        // type 为数组（如 ["string","null"]）时包含 null
        if (schema.TryGetProperty("type", out JsonElement typeElem) && typeElem.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in typeElem.EnumerateArray())
            {
                if (item.GetString() == "null")
                    return true;
            }
        }

        // anyOf/oneOf 中包含 null 类型
        if (schema.TryGetProperty("anyOf", out JsonElement anyOf) && anyOf.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement branch in anyOf.EnumerateArray())
            {
                if (branch.TryGetProperty("type", out JsonElement t) && t.GetString() == "null")
                    return true;
            }
        }
        if (schema.TryGetProperty("oneOf", out JsonElement oneOf) && oneOf.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement branch in oneOf.EnumerateArray())
            {
                if (branch.TryGetProperty("type", out JsonElement t) && t.GetString() == "null")
                    return true;
            }
        }
        return false;
    }
}
