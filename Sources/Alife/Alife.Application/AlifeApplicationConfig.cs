using Alife.Foundation;
using Newtonsoft.Json;

namespace Alife.Client;

public class AlifeApplicationConfig
{
    /// <summary>
    /// 从云端拉取客户端版本和插件清单信息。
    /// 部分老插件依赖云端清单来判断环境依赖，如果你已确保插件环境正常，且不关心云端版本，可以关闭。
    /// </summary>
    public bool FetchOnlineInfo { get; set; } = true;
    /// <summary>
    /// 是否加载插件文件夹中的插件。
    /// 如果你的所有功能都是内置在客户端程序中，那么你也可以不走插件加载。
    /// </summary>
    public bool LoadLocalPlugins { get; set; } = true;
    /// <summary>
    /// 是否每次都尝试安装插件环境。
    /// 此举可以确保每次运行时环境完整，但导致启动速度降低。如果你可以确保插件安装时不中断，且不会破坏性的修改环境，可以关闭此选项。
    /// 当出现环境问题时，你依然可以去插件环境页面手动重载。
    /// </summary>
    public bool EnsureEnvironment { get; set; } = true;
    /// <summary>
    /// 创建一个MCP服务器，让AI可以借此控制Alife。
    /// 如果你不需要此功能，也可关闭。
    /// </summary>
    public bool McpServer { get; set; } = true;
}