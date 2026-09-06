using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Alife.Function.FunctionCaller;

public partial class McpFunctionCaller
{
    /// <summary>
    /// JsonRpc 工具文档的文档头：既能标识是 JsonRpc 服务的文档，也能识别具体是哪个服务。
    /// 通过该头在对话历史中查找并回收已注入的文档。
    /// </summary>
    public static string GetDocumentTag(string serverName)
        => $"[JsonRpc文档({serverName})]";

    static string GetServerName(McpClient client)
    {
        string name = client.ServerInfo.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new Exception("MCP 客户端尚未初始化 ServerInfo.Name，无法作为路由名称。");
        return name;
    }

    /// <summary>
    /// 计算新 System 消息应插入的位置：紧随最后一个 System 消息之后，保持提示词位于对话前部。
    /// </summary>
    static int GetSystemInsertIndex(ChatHistoryAgentThread thread)
    {
        for (int i = thread.ChatHistory.Count - 1; i >= 0; i--)
        {
            if (thread.ChatHistory[i].Role == AuthorRole.System)
                return i + 1;
        }
        return 0;
    }
}

/// <summary>
/// 集中管理 AI 通过 JSON 调用外部 MCP 工具的能力：
/// 其他模块可调用 <see cref="RegisterMcpClientAsync"/> 将各自的 <see cref="McpClient"/> 注册于此，
/// AI 通过 &lt;JsonRpcMcp&gt; 标签以 JSON 直接描述工具调用，参数类型不会丢失。
/// 与 <see cref="XmlFunctionCaller"/> 一样支持 <see cref="DocumentMode"/> 文档注入模式。
/// 服务器名称取自 <see cref="McpClient.ServerInfo"/> 的 Name；如需自定义名称，可直接修改该字段。
/// </summary>
[Module("MCP函数调用器",
    "类似 XmlFunctionCaller，但使用 McpClient 注册。同时让 AI 通过 <JsonRpcMcp> 直接传递 JSON 来调用工具，具有更高的准确性和兼容性。",
    defaultCategory: "Alife 官方/功能底座")]
public partial class McpFunctionCaller(
    XmlFunctionCaller functionCaller,
    Interactor<McpFunctionCaller> interactor) :
    ChatBehaviour
{
    /// <summary>
    /// 注册一个 MCP 客户端。服务器名称取自 <see cref="McpClient.ServerInfo"/> 的 Name，
    /// 用于 <see cref="JsonRpcMcp"/> 的 server 参数路由；如需自定义名称，请先修改
    /// <c>client.ServerInfo.Name</c>。
    /// </summary>
    /// <param name="client">已连接的 MCP 客户端。</param>
    /// <param name="documentMode">文档注入模式，默认 <see cref="DocumentMode.Explicit"/>。</param>
    /// <param name="cancellationToken">取消时自动注销该客户端。</param>
    public async Task RegisterMcpClientAsync(McpClient client,
        DocumentMode documentMode = DocumentMode.Explicit,
        CancellationToken cancellationToken = default)
    {
        clients[client] = documentMode;

        switch (documentMode)
        {
            case DocumentMode.None:
                break;
            case DocumentMode.Implicit:
                implicitClients.Add(client);
                break;
            case DocumentMode.Explicit:
                explicitClients.Add(client);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(documentMode), documentMode, null);
        }

        if (cancellationToken != CancellationToken.None)
            cancellationToken.Register(() => {
                _ = UnregisterMcpClientAsync(client);
            });

        await UpdatePromptAsync();
    }

    public async Task UnregisterMcpClientAsync(McpClient client)
    {
        clients.Remove(client);
        explicitClients.Remove(client);
        if (implicitClients.Remove(client))
            RemoveJsonRpcDocument(GetServerName(client));
        await UpdatePromptAsync();
    }

    /// <summary>
    /// 按服务器名称获取已注册的 MCP 客户端。
    /// </summary>
    public bool TryGetClient(string name, out McpClient? client)
    {
        foreach ((McpClient key, _) in clients)
        {
            if (GetServerName(key) == name)
            {
                client = key;
                return true;
            }
        }
        client = null;
        return false;
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("用于调用使用McpFunctionCaller注册的工具。")]
    public async Task JsonRpcMcp(XmlExecutorContext context,
        [Description("调用工具所属的服务")] string server,
        [Description("直接提供`{\"name\":\"工具名\",\"arguments\":{...}}`即可")] [XmlContent]
        string json,
        CancellationToken cancellationToken)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string content = context.FullContent.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            interactor.Poke("JsonRpcMcp 内容体为空，请写入 JSON：{\"name\":\"工具名\",\"arguments\":{...}}");
            return;
        }

        using JsonDocument doc = JsonDocument.Parse(content);
        JsonElement root = doc.RootElement;
        string toolName = root.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(toolName))
            throw new Exception("JsonRpcMcp 需要 JSON 对象：{\"name\":\"工具名\",\"arguments\":{...}}");

        McpClient client = ResolveClient(server);

        // arguments 是 JSON 对象，直接反序列化成字典即可（保留嵌套结构，无需手写转换）
        Dictionary<string, object?>? arguments = root.TryGetProperty("arguments", out JsonElement argsEl) && argsEl.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(argsEl.GetRawText())
            : new Dictionary<string, object?>();

        CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
        string resultText = string.Join("\n", result.Content.Where(b => b is TextContentBlock).Select(b => ((TextContentBlock)b).Text));
        if (result.IsError == true)
            throw new Exception(resultText);
        interactor.Poke($"MCP 工具执行完成\n{resultText}");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("加载指定 JsonRpc 服务的工具文档，了解如何调用其工具")]
    public async Task LoadJsonRpcDocument(
        [Description("服务名称（即 server 参数）")] string server,
        CancellationToken cancellationToken)
    {
        McpClient client = ResolveClient(server);
        string document = await McpUtility.BuildToolsJsonDocumentAsync(client);

        string head = GetDocumentTag(server);
        ChatBot.EditChatHistory(thread => {
                // 已存在则先移除旧文档，避免重复注入
                ChatMessageContent? old = thread.ChatHistory.FirstOrDefault(c => c.Content?.StartsWith(head) ?? false);
                if (old != null)
                    thread.ChatHistory.Remove(old);

                var message = new ChatMessageContent(AuthorRole.System, head + "\n" + document);
                thread.ChatHistory.Insert(GetSystemInsertIndex(thread), message);
            }, $"加载 JsonRpc 文档({server})");

        interactor.Poke($"JsonRpc 服务「{server}」的工具文档已加载");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("卸载指定 JsonRpc 服务的工具文档，释放上下文空间")]
    public void UnloadJsonRpcDocument(
        [Description("服务名称（即 server 参数）")] string server)
    {
        RemoveJsonRpcDocument(server);
        interactor.Poke($"JsonRpc 服务「{server}」的工具文档已卸载");
    }

    readonly Dictionary<McpClient, DocumentMode> clients = new();
    readonly HashSet<McpClient> explicitClients = new();
    readonly HashSet<McpClient> implicitClients = new();
    XmlHandler selfHandler = null!;

    protected override Task OnAwake()
    {
        selfHandler = new(this);
        // 函数文档由 UpdatePrompt 手动注入，避免 XmlFunctionCaller 重复注入
        functionCaller.RegisterHandler(selfHandler, DocumentMode.None, DestroyCancellationToken);
        functionCaller.AddPlainAreas(nameof(JsonRpcMcp));
        return Task.CompletedTask;
    }

    async Task UpdatePromptAsync()
    {
        var explicitDocs = new List<string>();
        foreach (McpClient client in explicitClients)
            explicitDocs.Add(await McpUtility.BuildToolsJsonDocumentAsync(client));

        string explicitBlock = explicitDocs.Count == 0
            ? "（暂无）"
            : string.Join("\n\n", explicitDocs);

        string implicitList = implicitClients.Count == 0
            ? "（暂无）"
            : string.Join("\n",
                implicitClients.Select(c => $"- {GetServerName(c)} : {c.ServerInfo.Description}"));

        interactor.Prompt(
            $$"""
              此服务追加了一些额外特别的MCP服务，但这些服务内的函数必须使用JsonRpcMcp调用。

              {{selfHandler.FunctionDocument()}}

              ## 显式 MCP 服务器
              {{explicitBlock}}

              ## 隐式 MCP 服务器
              这些服务的文档默认隐藏，需要用上面的 JsonRpc 文档加载函数按需加载、卸载：

              {{implicitList}}
              """
        );
    }

    McpClient ResolveClient(string server)
    {
        foreach ((McpClient key, _) in clients)
        {
            if (GetServerName(key) == server)
                return key;
        }
        throw new Exception($"未找到 MCP 客户端 {server}，可用：{string.Join(", ", clients.Keys.Select(GetServerName))}");
    }

    /// <summary>
    /// 从对话历史中移除指定服务的 JsonRpc 文档（按文档头识别）。
    /// </summary>
    void RemoveJsonRpcDocument(string server)
    {
        string head = GetDocumentTag(server);
        ChatBot.EditChatHistory(thread => {
                ChatMessageContent? old = thread.ChatHistory.FirstOrDefault(c => c.Content?.StartsWith(head) ?? false);
                if (old != null)
                    thread.ChatHistory.Remove(old);
            }, $"卸载 JsonRpc 文档({server})");
    }
}