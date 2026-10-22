using System;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Alife.Function.Developer;

[Module("开发者工具",
    "桥接 Alife 内置 MCP 服务，向 AI 完全暴露项目信息和软件控制能力，借此轻松实现自制插件、角色管理、错误处理等需求。",
    defaultCategory: "Alife 官方/生活环境")]
public class DeveloperService(
    IAlifeApplication alifeApplication,
    XmlFunctionCaller functionCaller,
    ILoggerFactory loggerFactory,
    Interactor<DeveloperService> interactor) :
    ChatBehaviour
{
    McpClient? mcpClient;

    protected override async Task OnAwake()
    {
        if (alifeApplication.AlifeMcpServer == null)
            throw new Exception("必须确保开启了 AlifeMcp 才可以使用开发者工具");

        mcpClient = await McpUtility.ConnectHttpAsync("AlifeMcp", alifeApplication.AlifeMcpServer, loggerFactory);
        XmlHandler xmlHandler = await McpXmlAdapter.McpClientToXmlHandler(
            mcpClient,
            "DeveloperTools",
            "（重要！）你是搭载在Alife框架上的一名AI，此服务是你必须要了解的东西。他让你获得完全的开发者能力。你可借此获取Alife中的所有信息并控制软件，从而轻松实现自制插件、角色管理、错误修复等需求。当用户遇到问题或期望时，请立即使用此工具。",
            (name, result) => interactor.Poke($"AlifeMcp.{name} 执行完成\n{result}")
        );

        functionCaller.RegisterHandler(xmlHandler, DocumentMode.Implicit, DestroyCancellationToken);
    }

    protected override async Task OnDestroy()
    {
        if (mcpClient != null)
            await mcpClient.DisposeAsync();
    }
}