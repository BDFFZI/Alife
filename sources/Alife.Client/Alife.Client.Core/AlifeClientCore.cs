using Alife.Framework;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Client.Core;

public static class AlifeClientCore
{
    public class InitConfig
    {
        /// <summary>
        /// 从云端拉取客户端版本和插件清单信息。
        /// 部分老插件依赖云端清单来判断环境依赖，如果你已确保插件环境正常，且不关心云端版本，可以关闭。
        /// </summary>
        public bool FetchOnlineInfo { get; init; }

        /// <summary>
        /// 是否加载插件文件夹中的插件。
        /// 如果你的所有功能都是内置在客户端程序中，那么你也可以不走插件加载。
        /// </summary>
        public bool LoadLocalPlugins { get; init; }

        /// <summary>
        /// 是否每次都尝试安装插件环境。
        /// 此举可以确保每次运行时环境完整，但导致启动速度降低。如果你可以确保插件安装时不中断，且不会破坏性的修改环境，可以关闭此选项。
        /// 当出现环境问题时，你依然可以去插件环境页面手动重载。
        /// </summary>
        public bool EnsureEnvironment { get; init; }

        /// <summary>
        /// 创建一个MCP服务器，让AI可以借此控制Alife。
        /// 如果你不需要此功能，也可关闭。
        /// </summary>
        public bool McpServer { get; init; }

        public int McpServerPort { get; init; } = 18765;
    }

    public static void AddAlifeClient<T>(this IServiceCollection services)
        where T : class, IAlifeClient
    {
        services.AddAlife();
        services.AddSingleton<IAlifeClient, T>();
    }
    public static async Task InitAlifeClient(this IServiceProvider provider, InitConfig? config = null, IProgress<string>? progress = null)
    {
        config ??= new InitConfig();

        //alife客户端会提供自己的运行环境（如python）
        ClientEnvironment.Initialize();

        await provider.InitAlife();

        if (config.FetchOnlineInfo) //自动拉取云端插件，防呆的同时确保兼容老插件编译
        {
            progress?.Report("正在拉取云端内容...");

            PluginSystem pluginSystem = provider.GetRequiredService<PluginSystem>();
            await ClientUpgrade.FetchNewVersion();
            await pluginSystem.SyncOnlinePluginPackages(); //老版本插件依赖云端包信息来确定编译环境，所以要放在加载插件之前
        }

        if (config.LoadLocalPlugins) //自动加载本地插件，客户端通常是全插件而非内置功能，所以默认加载
        {
            progress?.Report("正在加载本地插件...");

            PluginSystem pluginSystem = provider.GetRequiredService<PluginSystem>();
            await pluginSystem.SyncLocalPluginEnvironment();
        }

        if (config.McpServer) //客户端默认激活MCP，因为其一般都是图形化界面，无法基于CLI交互
        {
            progress?.Report("正在激活MCP服务...");

            //激活 MCP 服务，使外部程序可以通过 MCP 调用 Alife 系统能力
            await AlifeMcp.StartAsync(provider, $"http://127.0.0.1:{config.McpServerPort}");
        }

        //额外的客户端功能
        {
            progress?.Report("初始化即将完成...");

            ChatActivitySystem chatActivitySystem = provider.GetRequiredService<ChatActivitySystem>();
            StorageSystem storageSystem = provider.GetRequiredService<StorageSystem>();

            //初始化对话统计功能
            ClientChatStatistics.Initialize(chatActivitySystem, storageSystem);
            //增加额外的基于客户端的功能
            chatActivitySystem.InjectedObjects.Add(provider.GetRequiredService<IAlifeClient>());
        }
    }
}