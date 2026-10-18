using Alife.Foundation;
using Alife.Framework;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Client;

public static class AlifeClient
{
    public static void AddAlifeClient<TAlifeFoundation, TCSharpCompiler,
        TNuGetEnvironmentInstaller, TPipEnvironmentInstaller,
        TAlifeClient, TAlifeMcp>(
        this IServiceCollection services, AlifeClientConfig? config = null)
        where TAlifeFoundation : class, IAlifeFoundation
        where TCSharpCompiler : CSharpCompiler
        where TNuGetEnvironmentInstaller : NuGetEnvironmentInstaller
        where TPipEnvironmentInstaller : PipEnvironmentInstaller
        where TAlifeClient : class, IAlifeClient
        where TAlifeMcp : class, IAlifeMcp
    {
        services.AddAlife<TAlifeFoundation, TCSharpCompiler, TNuGetEnvironmentInstaller, TPipEnvironmentInstaller>(); //alife框架
        services.AddAntDesign(); //前端框架
        services.AddSingleton(config ?? new AlifeClientConfig());
        services.AddSingletonChain<IAlifeClient, TAlifeClient>();
        services.AddSingletonChain<IAlifeMcp, TAlifeMcp>();

        //客户端功能
        services.AddSingleton<ClientDataStore>();
    }
    public static async Task InitAlifeClient(this IServiceProvider provider, IProgress<string>? progress = null)
    {
        await provider.InitAlife(progress);

        AlifeClientConfig config = provider.GetRequiredService<AlifeClientConfig>();

        if (config.FetchOnlineInfo) //自动拉取云端插件，防呆的同时确保兼容老插件编译
        {
            progress?.Report("正在拉取云端内容...");
            try
            {
                PluginSystem pluginSystem = provider.GetRequiredService<PluginSystem>();
                await ClientUpgrade.FetchNewVersion();
                await pluginSystem.SyncOnlinePluginPackages(); //老版本插件依赖云端包信息来确定编译环境，所以要放在加载插件之前
            }
            catch (Exception e)
            {
                AlifeLog.LogError("云端内容拉取失败：" + e);
            }
        }

        if (config.LoadLocalPlugins) //自动加载本地插件，客户端通常是全插件而非内置功能，所以默认加载
        {
            progress?.Report("正在加载本地插件...");

            if (config.EnsureEnvironment == false)
            {
                NuGetEnvironmentInstaller nuGetEnvironmentInstaller = provider.GetRequiredService<NuGetEnvironmentInstaller>();
                await nuGetEnvironmentInstaller.LoadResolvedEnvironment(); //nuget比较特殊，插件依赖他的dll运行时环境，所以必须加载
            }

            PluginSystem pluginSystem = provider.GetRequiredService<PluginSystem>();
            await pluginSystem.SyncLocalPluginEnvironment(config.EnsureEnvironment);
        }

        if (config.McpServer) //客户端默认激活MCP，因为其一般都是图形化界面，无法基于CLI交互
        {
            progress?.Report("正在激活MCP服务...");
            try
            {
                //激活 MCP 服务，使外部程序可以通过 MCP 调用 Alife 系统能力
                IAlifeMcp alifeMcp = provider.GetRequiredService<IAlifeMcp>();
                await alifeMcp.StartAsync($"http://127.0.0.1:{config.McpServerPort}");
            }
            catch (Exception e)
            {
                AlifeLog.LogError("激活MCP服务失败：" + e);
            }
        }

        //额外的客户端功能
        {
            progress?.Report("初始化客户端功能...");

            ChatActivitySystem chatActivitySystem = provider.GetRequiredService<ChatActivitySystem>();
            StorageSystem storageSystem = provider.GetRequiredService<StorageSystem>();

            //初始化对话统计功能
            ClientChatStatistics.Initialize(chatActivitySystem, storageSystem);
            //增加额外的基于客户端的功能
            chatActivitySystem.InjectedObjects.Add(provider.GetRequiredService<IAlifeClient>());
        }
    }
}