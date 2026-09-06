using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
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
                    AlifeLog.LogInformation(
                        $"[McpConvert] tool={tool.Name} key={key} raw={value} jsonType={typeInfo.Type} converted={System.Text.Json.JsonSerializer.Serialize(convertedValue)}");
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
            {
                AlifeLog.LogWarning($"[McpConvert] tool={tool.Name} 调用失败: {resultText}");
                throw new Exception(resultText);
            }

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
        // 数组类型（integer[]、string[]、object[] 等）
        if (jsonType.EndsWith("[]"))
        {
            string itemType = jsonType[..^2];

            // 尝试按 JSON 解析（数组或单值字符串）
            try
            {
                using JsonDocument doc = JsonDocument.Parse(value);
                return ParseJsonArrayElements(doc.RootElement, itemType);
            }
            catch
            {
                // 非合法 JSON，继续尝试宽容解析
            }

            // 宽容解析：把单引号替换为双引号（AI 常写 [{'a':'b'}] 而非 JSON 标准双引号）
            string normalized = value.Replace('\'', '"');
            if (normalized != value)
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(normalized);
                    return ParseJsonArrayElements(doc.RootElement, itemType);
                }
                catch
                {
                    // 仍失败，进入下方兜底
                }
            }

            // 兜底：剥括号 + 逗号拆分（仅适用于基础类型数组；对象/数组元素无法用逗号可靠拆分）
            if (itemType is "object" or "array")
                return new List<object?> { value };

            string trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
                trimmed = trimmed[1..^1];

            string[] parts = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            List<object?> items = new();
            foreach (string part in parts)
                items.Add(ConvertValue(part.Trim(), itemType));
            return items;
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

    /// <summary>
    /// 将 JSON 元素按目标类型转换为 .NET 值（供数组元素使用）。
    /// </summary>
    static object? ConvertJsonElement(JsonElement element, string itemType)
    {
        switch (itemType)
        {
            case "number":
                return element.ValueKind == JsonValueKind.Number ? element.GetDouble() : element.ToString();
            case "integer":
                return element.ValueKind == JsonValueKind.Number ? element.GetInt64() : element.ToString();
            case "boolean":
                return element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False
                    ? element.GetBoolean()
                    : element.ToString();
            case "object":
                return element.ValueKind == JsonValueKind.Object ? JsonElementToDictionary(element) : element.ToString();
            case "array":
                return element.ValueKind == JsonValueKind.Array ? JsonElementToArray(element) : element.ToString();
            default:
                return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        }
    }

    /// <summary>
    /// 将 JSON 根元素解析为数组元素列表（兼容数组或单值字符串）。
    /// </summary>
    static List<object?> ParseJsonArrayElements(JsonElement root, string itemType)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            List<object?> jsonItems = new();
            foreach (JsonElement element in root.EnumerateArray())
                jsonItems.Add(ConvertJsonElement(element, itemType));
            return jsonItems;
        }
        if (root.ValueKind == JsonValueKind.String)
            return new List<object?> { ConvertValue(root.GetString() ?? "", itemType) };
        return new List<object?> { ConvertJsonElement(root, itemType) };
    }

    /// <summary>
    /// 将 JSON 对象元素转为 Dictionary<string, object?>，保持嵌套结构（供 object[] 元素使用）。
    /// </summary>
    static Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        Dictionary<string, object?> dict = new();
        foreach (JsonProperty property in element.EnumerateObject())
            dict[property.Name] = ConvertJsonElement(property.Value, ResolveJsonValueKind(property.Value));
        return dict;
    }

    /// <summary>
    /// 将 JSON 数组元素转为 List<object?>，保持嵌套结构。
    /// </summary>
    static List<object?> JsonElementToArray(JsonElement element)
    {
        List<object?> list = new();
        foreach (JsonElement item in element.EnumerateArray())
            list.Add(ConvertJsonElement(item, ResolveJsonValueKind(item)));
        return list;
    }

    static string ResolveJsonValueKind(JsonElement element)
    {
        return element.ValueKind switch {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.Number => element.TryGetInt64(out _) ? "integer" : "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            _ => "string"
        };
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