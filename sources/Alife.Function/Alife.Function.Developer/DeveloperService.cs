using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Mcp;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Alife.Function.Developer;

[Module("开发者模式",
    "桥接 Alife 内置 MCP 服务，向 AI 暴露项目和系统信息，并提供工具使其可以自制插件和活动管理。",
    defaultCategory: "Alife 官方/生活环境")]
public class DeveloperService(
    XmlFunctionCaller functionCaller,
    ILoggerFactory loggerFactory,
    IInteractor<DeveloperService> interactor) :
    ChatBehaviour
{
    McpClient? mcpClient;

    protected override async Task OnAwake()
    {
        mcpClient = await McpXmlAdapter.ConnectHttpAsync("AlifeMcp", AlifeMcp.Endpoint, loggerFactory);
        XmlHandler xmlHandler = await McpXmlAdapter.McpClientToXmlHandler(
            mcpClient,
            "AlifeMcp",
            "当用户需要你帮忙解决当前软件问题或想进行插件开发时使用此工具，它会带你全面了解你所有在的 Alife 框架，并让你拥有全量控制的能力。",
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