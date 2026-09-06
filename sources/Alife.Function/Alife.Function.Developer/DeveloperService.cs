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
    XmlFunctionCaller functionCaller,
    ILoggerFactory loggerFactory,
    Interactor<DeveloperService> interactor) :
    ChatBehaviour
{
    McpClient? mcpClient;

    protected override async Task OnAwake()
    {
        mcpClient = await McpUtility.ConnectHttpAsync("AlifeMcp", AlifeMcp.Endpoint, loggerFactory);
        XmlHandler xmlHandler = await McpXmlAdapter.McpClientToXmlHandler(
            mcpClient,
            "DeveloperTools",
            "你是搭载在Alife框架上的一名AI，你已被授予完全的开发者权限。你可以通过此工具获取框架的所有信息和软件控制能力，借此轻松实现自制插件、角色管理、错误处理等需求。当用户遇到技术问题或请求功能时，请使用此工具。",
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