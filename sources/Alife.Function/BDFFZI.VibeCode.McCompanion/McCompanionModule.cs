using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace BDFFZI.VibeCode.McCompanion;

public class McCompanionConfig
{
    [Description("Numen MCP 服务器地址")]
    public string Endpoint { get; set; } = "http://127.0.0.1:8765/mcp";

    [Description("访问令牌（Bearer Token），用于鉴权访问 Numen MCP")]
    public string Token { get; set; } = "";

    [Description("对应的分身名称。留空则自动选择第一个在线的分身。")]
    public string CompanionName { get; set; } = "";

    [Description("注册到 MCP 函数调用器的客户端名称")]
    public string McpServerName { get; set; } = "Numen";
}

/// <summary>
/// MC 陪玩模块：接入 Numen MCP，将 Minecraft 世界中的同伴作为 AI 的分身。
/// 本模块自行连接 Numen MCP（连接工具来自 <see cref="McpUtility"/>，不再依赖 Alife.Function.Mcp 插件），
/// 将客户端注册到 <see cref="McpFunctionCaller"/>，AI 通过 &lt;JsonRpcMcp&gt; 调用工具，
/// 并自行使用 get_events 主动拉取游戏内事件。
/// </summary>
[Module("MC陪玩",
    "接入 minecraft-numen 的外接大脑功能，使 AI 可以控制 MC 中的分身：实现移动、挖掘、建造、合成、战斗等操作，同时提供 get_events 工具让 AI 主动拉取游戏内事件，实现真正的一起玩 MC。",
    defaultCategory: "真央的小工具")]
public class McCompanionModule(
    XmlFunctionCaller functionCaller,
    McpFunctionCaller mcpFunctionCaller,
    ILogger<McCompanionModule> logger,
    ILoggerFactory loggerFactory,
    Interactor<McCompanionModule> interactor) :
    ChatBehaviour,
    IConfigurable<McCompanionConfig>
{
    public McCompanionConfig Configuration { get; set; } = null!;

    #region 激活与关闭

    [XmlFunction(FunctionMode.OneShot)]
    [Description("开启MC陪玩，和用户一起玩Minecraft。")]
    public async Task ActivateMcCompanion()
    {
        if (mcpClient != null)
            throw new Exception("MC 陪玩已经连接，请先关闭。");

        if (string.IsNullOrWhiteSpace(Configuration.Endpoint))
            throw new Exception("MC 陪玩尚未配置服务器地址。");

        // 连接 Numen MCP
        Dictionary<string, string>? headers = string.IsNullOrWhiteSpace(Configuration.Token)
            ? null
            : new Dictionary<string, string> { ["Authorization"] = "Bearer " + Configuration.Token };
        mcpClient = await McpUtility.ConnectHttpAsync(
            "NumenMcp",
            new Uri(Configuration.Endpoint),
            loggerFactory,
            headers);

        // 注册 MC 陪玩的函数和文档
        mcpClient.ServerInfo.Name = Configuration.McpServerName;
        await mcpFunctionCaller.RegisterMcpClientAsync(
            mcpClient,
            DocumentMode.None,
            DestroyCancellationToken);
        interactor.Prompt(
            $"""
             {McpFunctionCaller.GetDocumentTag(mcpClient.ServerInfo.Name)}
             {mcpClient.ServerInstructions}

             {await McpUtility.BuildToolsJsonDocumentAsync(mcpClient)}

             追加说明：
             1. 如果游戏过程中遇到问题，请即使向用户反馈，因为有时候任务会因各种问题被静默失败，你需要让用户配合你找到原因。
             2. 建造功能使用绝对坐标，且会自动替换遮挡的方块，但如果材料不足则会静默失败。建造前请确保材料足够，并且每个物品都已经合成。
             3. 采集资源任务完成后需要手动收集周围的掉落物，否则可能收集不全。部分任务还需要对应的工具，比如挖石头需要稿子，否则会静默失败。
             4. 任务已受理只是代表尝试执行，不代表执行成功，需要检查分身状态和任务结果。
             5. 手持方块右键放置的落点不可控，优先使用build功能放置方块。例如放门，使用set_door+facing。 
             6. 分身采用半自动控制，其会根据情况自动寻找采集物、逃避敌人或战斗，因此任务中位置可能发生移动。
             """);

        string companion = await ResolveCompanionAsync(DestroyCancellationToken) ?? "未设置";
        interactor.Poke($"已激活 MC 陪玩。当前分身「{companion}」。你可以使用 <JsonRpcMcp> 标签调用 MCP 工具操作 MC，并使用 get_events 主动拉取游戏内事件。");
        logger.LogInformation("MC 陪玩已激活，分身: {Companion}", companion);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("关闭MC陪玩，清理思绪回到正常状态。")]
    public async Task DeactivateMcCompanion()
    {
        if (mcpClient == null)
            throw new Exception("MC 陪玩当前未激活。");

        // 清空注入的提示词
        interactor.Prompt("");

        // 注销并释放 MCP 客户端
        await mcpFunctionCaller.UnregisterMcpClientAsync(mcpClient);
        await mcpClient.DisposeAsync();
        mcpClient = null;

        interactor.Poke("已关闭 MC 陪玩。");
        logger.LogInformation("MC 陪玩已关闭");
    }

    #endregion

    McpClient? mcpClient;

    protected override Task OnAwake()
    {
        // 模块自身注册为显式文档：AI 常驻可见激活/关闭函数
        XmlHandler selfHandler = new(this) {
            Description = "MC陪玩功能",
        };
        functionCaller.RegisterHandler(selfHandler, DocumentMode.Explicit, DestroyCancellationToken);
        return Task.CompletedTask;
    }
    protected override async Task OnDestroy()
    {
        if (mcpClient != null)
        {
            await mcpFunctionCaller.UnregisterMcpClientAsync(mcpClient);
            await mcpClient.DisposeAsync();
            mcpClient = null;
        }
    }

    /// <summary>
    /// 解析一个在线的同伴名称：优先使用配置，否则调用 list_companions 取第一个。
    /// </summary>
    async Task<string?> ResolveCompanionAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(Configuration.CompanionName))
            return Configuration.CompanionName.Trim();

        if (mcpClient == null)
            return null;

        CallToolResult result = await mcpClient.CallToolAsync(
            "list_companions",
            new Dictionary<string, object?>(),
            cancellationToken: ct);

        string text = GetResultText(result);
        if (result.IsError == true)
            throw new Exception(text);

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("- "))
            {
                string name = trimmed[2..].Trim();
                int paren = name.IndexOf('(');
                if (paren > 0)
                    name = name[..paren].Trim();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
        }
        return null;
    }

    static string GetResultText(CallToolResult result)
    {
        return string.Join("\n",
            result.Content
                .Where(block => block is TextContentBlock)
                .Select(block => ((TextContentBlock)block).Text));
    }
}