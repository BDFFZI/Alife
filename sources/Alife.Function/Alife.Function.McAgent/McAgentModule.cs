using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Alife.Function.McAgent;

public class McAgentConfig
{
    [Description("Numen MCP 服务器地址")]
    public string Endpoint { get; set; } = "http://127.0.0.1:8765/mcp";

    [Description("访问令牌（Bearer Token），用于鉴权访问 Numen MCP")]
    public string Token { get; set; } = "";

    [Description("对应的分身名称。留空则自动选择第一个在线的分身。")]
    public string CompanionName { get; set; } = "";
}

/// <summary>
/// MC 陪玩模块：接入 Numen MCP，将 Minecraft 世界中的同伴作为 AI 的分身。
/// 本模块自行连接 Numen MCP（连接工具来自 <see cref="McpUtility"/>，不再依赖 Alife.Function.Mcp 插件），
/// 将客户端注册到 <see cref="McpFunctionCaller"/>，AI 通过 &lt;JsonRpcMcp&gt; 调用工具，
/// 并自动轮询 get_events 拉取游戏内事件推送给 AI，AI 无需（也不应）手动轮询。
/// </summary>
[Module("MC陪玩",
    "接入 minecraft-numen 的外接大脑功能并进行框架融合，使 AI 可以控制 MC 中的分身：实现移动、挖掘、建造、合成、战斗等操作，同时自动化事件任务状态的拉取推送，以及部分提示词的修正，实现真正的一起玩 MC。"
    + "\nMod下载地址：https://github.com/Dwinovo/minecraft-numen",
    defaultCategory: "真央的小工具")]
public class McAgentModule(
    XmlFunctionCaller functionCaller,
    McpFunctionCaller mcpFunctionCaller,
    ILogger<McAgentModule> logger,
    ILoggerFactory loggerFactory,
    Interactor<McAgentModule> interactor) :
    ChatBehaviour,
    IConfigurable<McAgentConfig>
{
    public McAgentConfig Configuration { get; set; } = null!;

    #region 激活与关闭

    [XmlFunction(FunctionMode.OneShot)]
    [Description("开启MC陪玩，和用户一起玩Minecraft。")]
    public async Task ActivateMcAgent()
    {
        if (mcpClient != null)
            throw new Exception("MC 陪玩已经连接，请先关闭。");

        // 连接 Numen MCP
        {
            if (string.IsNullOrWhiteSpace(Configuration.Endpoint))
                throw new Exception("MC 陪玩尚未配置服务器地址。");
            Dictionary<string, string>? headers = string.IsNullOrWhiteSpace(Configuration.Token)
                ? null
                : new Dictionary<string, string> { ["Authorization"] = "Bearer " + Configuration.Token };
            mcpClient = await McpUtility.ConnectHttpAsync(
                "NumenMcp",
                new Uri(Configuration.Endpoint),
                loggerFactory,
                headers);
        }

        // 注册 MC 陪玩的函数和文档
        {
            mcpClient.ServerInfo.Name = "McAgent";
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
                 1. 如果游戏过程中遇到问题，请及时向用户反馈，因为有时候任务会因各种问题被静默失败，你需要让用户配合你找到原因。
                 2. 建造功能使用绝对坐标，且会自动替换遮挡的方块。但如果材料不足或所需物品没有，以及过于的远离建造位置，则会静默失败。
                 3. 进行建造前，请务必先核算计划清单和背包中物品的数量关系，确保背包中的材料足够，并且每个方块家具都已合成。
                 4. 采集资源任务完成后需要手动收集周围的掉落物，否则可能收集不全。部分任务还需要对应的工具，比如挖石头需要稿子，否则会静默失败。
                 5. 任务已受理只是代表尝试执行，不代表执行成功，需要检查分身状态和任务结果。
                 6. 手持方块右键放置的落点不可控，优先使用build功能放置方块。例如放门，使用set_door+facing。
                 7. 分身采用半自动控制，其会根据情况自动寻找采集物、逃避敌人或战斗，因此任务中位置可能发生移动。
                 8. 目前系统已会自动轮询 get_events/task_status，并按需推送结果给你，所以一般不再需要你手动轮询。
                 """);
        }

        string? companion = await ResolveCompanionAsync(DestroyCancellationToken);
        if (string.IsNullOrWhiteSpace(companion))
        {
            string want = string.IsNullOrWhiteSpace(Configuration.CompanionName)
                ? "自动解析"
                : $"「{Configuration.CompanionName.Trim()}」";
            interactor.Poke($"未检测到在线的分身 {want}，get_events/task_status 无法进行自动轮询。请确认 CompanionName 与在线分身一致（或先召唤分身）后重新开启 MC 陪玩。");
            logger.LogWarning("未解析到在线分身，无法进行自动轮询。CompanionName: {Name}", Configuration.CompanionName);
        }
        else
        {
            pollLoop = new CancellationTokenSource();
            StartEventPollLoop(companion, pollLoop.Token);
            ChatBot.ChatSent += OnChatSent;
            interactor.Poke($"已激活 MC 陪玩。当前分身「{companion}」。你可以使用 <JsonRpcMcp> 标签调用 MCP 工具操作 MC。" +
                            "注意：目前系统已会自动轮询 get_events/task_status，并按需推送结果给你，所以你一般不需要手动轮询。");
        }
        logger.LogInformation("MC 陪玩已激活，分身: {Companion}", companion ?? "未设置");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("关闭MC陪玩，清理思绪回到正常状态。")]
    public async Task DeactivateMcAgent()
    {
        if (mcpClient == null)
            throw new Exception("MC 陪玩当前未激活。");

        ChatBot.ChatSent -= OnChatSent;
        await pollLoop!.CancelAsync();

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
    CancellationTokenSource? pollLoop;
    long taskProcess; //0-无任务；1-发起任务；2-监听任务


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
        ChatBot.ChatSent -= OnChatSent;
        if (mcpClient != null)
        {
            await mcpFunctionCaller.UnregisterMcpClientAsync(mcpClient);
            await mcpClient.DisposeAsync();
            mcpClient = null;
        }
    }

    /// <summary>
    /// 解析一个在线的同伴名称：校验配置名（或自动取第一个在线分身）确实在 list_companions 中存在，
    /// 不在线返回 null——由调用方决定不开启轮询并仅提示一次。
    /// </summary>
    async Task<string?> ResolveCompanionAsync(CancellationToken ct)
    {
        if (mcpClient == null)
            return null;

        CallToolResult result = await mcpClient.CallToolAsync(
            "list_companions",
            new Dictionary<string, object?>(),
            cancellationToken: ct);

        string text = GetResultText(result);
        if (result.IsError == true)
            throw new Exception(text);

        var live = new List<(string name, string id)>();
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("- "))
                continue;
            string rest = trimmed[2..].Trim();
            string name = rest;
            string id = "";
            int idIdx = rest.IndexOf("(id:", StringComparison.Ordinal);
            if (idIdx >= 0)
            {
                name = rest[..idIdx].Trim();
                int s = idIdx + 4;
                int e = rest.IndexOf(')', s);
                if (e > s)
                    id = rest[s..e].Trim();
            }
            if (!string.IsNullOrEmpty(name))
                live.Add((name, id));
        }

        if (!string.IsNullOrWhiteSpace(Configuration.CompanionName))
        {
            string want = Configuration.CompanionName.Trim();
            return live.FirstOrDefault(c =>
                c.name.Equals(want, StringComparison.OrdinalIgnoreCase) ||
                c.id.Equals(want, StringComparison.OrdinalIgnoreCase)).name;
        }

        return live.Count == 0 ? null : live[0].name;
    }

    void OnChatSent(string message)
    {
        try
        {
            if (message.Contains("已受理,后台执行中"))
            {
                logger.LogInformation("发起任务");
                taskProcess = 1;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "解析后台任务派发回执失败");
        }
    }

    /// <summary>
    /// 启动 get_events 自动轮询：后台循环约每2秒拉取一次游戏内事件，
    /// 非空结果自动推送到 AI 上下文，AI 无需（也不应）手动轮询 get_events。
    /// </summary>
    async void StartEventPollLoop(string companion, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                //轮询事件
                {
                    string eventResult = GetResultText(await mcpClient!.CallToolAsync(
                        "get_events",
                        new Dictionary<string, object?> {
                            ["companion"] = companion,
                            ["wait_seconds"] = 2,
                        },
                        cancellationToken: cancellationToken));
                    bool isNullEvent = eventResult.Contains("no new events");
                    if (isNullEvent == false)
                        interactor.Poke(eventResult);
                }

                if (taskProcess == 1)
                {
                    taskProcess = 2; //切换到处理状态
                    logger.LogInformation("执行任务");
                }

                if (taskProcess == 2)
                {
                    string taskResult = GetResultText(await mcpClient!.CallToolAsync(
                        "task_status",
                        new Dictionary<string, object?> {
                            ["companion"] = companion
                        },
                        cancellationToken: cancellationToken));
                    bool isNullTask = taskResult.Contains("没有后台任务");
                    if (taskProcess == 2 && isNullTask) //数据有效，还在处理状态
                    {
                        interactor.Poke("上个任务结束。" + taskResult);
                        taskProcess = 0;
                        logger.LogInformation("任务结束");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }


    static string GetResultText(CallToolResult result)
    {
        return string.Join("\n",
            result.Content
                .Where(block => block is TextContentBlock)
                .Select(block => ((TextContentBlock)block).Text));
    }
}