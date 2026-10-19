using Alife.Client;
using Alife.Foundation;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Framework;

/// <summary>
/// 用于构建无头Alife，例如实现通过自编译、CLI、MCP来使用。
/// </summary>
public static class AlifeApplication
{
    public static void AddAlife<TAlifeFoundation, TCSharpCompiler,
        TNuGetEnvironmentInstaller, TPipEnvironmentInstaller,
        TAlifeApplication>(
        this IServiceCollection services)
        where TAlifeFoundation : class, IAlifeFoundation
        where TCSharpCompiler : CSharpCompiler
        where TNuGetEnvironmentInstaller : NuGetEnvironmentInstaller
        where TPipEnvironmentInstaller : PipEnvironmentInstaller
        where TAlifeApplication : class, IAlifeApplication
    {
        //添加Alife框架
        services.AddAlifeFoundation<TAlifeFoundation>();
        services.AddAlifeFramework();
        services.AddAlifePluginSystem<TCSharpCompiler>();

        //添加默认插件环境
        services.AddSingletonChain<IEnvironmentInstaller, NuGetEnvironmentInstaller, TNuGetEnvironmentInstaller>();
        services.AddSingletonChain<IEnvironmentInstaller, PipEnvironmentInstaller, TPipEnvironmentInstaller>();

        //添加前端功能
        services.AddSingletonChain<IAlifeApplication, TAlifeApplication>();
        services.AddSingleton<AlifeMcpTools>();
        services.AddAntDesign(); //UI框架
    }

    public static async Task InitAlife(this IServiceProvider provider, AlifeApplicationConfig config, IProgress<string>? progress = null)
    {
        {
            progress?.Report("正在初始化基础环境...");
            provider.InitAlifeFoundation();
        }

        {
            progress?.Report("正在初始化插件环境...");

            PluginContext pluginContext = provider.GetRequiredService<PluginContext>();
            PluginMarket pluginMarket = provider.GetRequiredService<PluginMarket>();

            //老版本插件信息兼容
            {
                //生成老版版本文件
                pluginMarket.PluginInstalled += (pluginPackage, version) => {
                    if (File.Exists(pluginContext.GetPluginManifestPath(pluginPackage.Id)) == false)
                    {
                        string versionFile = Path.Combine(pluginContext.GetPluginDirectoryPath(pluginPackage.Id), "VERSION.txt");
                        File.WriteAllText(versionFile, version);
                    }
                };
                //从版本文件或插件市场中获取插件环境信息
                pluginContext.PluginManifestFallback = pluginId => {
                    string versionFile = Path.Combine(pluginContext.GetPluginDirectoryPath(pluginId), "VERSION.txt");
                    string? version = File.Exists(versionFile) ? File.ReadAllText(versionFile) : null;
                    PluginPackage? pluginPackage = pluginMarket.AllPluginPackages.GetValueOrDefault(pluginId);

                    return new PluginManifest() {
                        Version = string.IsNullOrEmpty(version) ? "0.0.0" : version,
                        Dependencies = pluginPackage?.GetDependencies(pluginId),
                        Environments = pluginPackage?.GetEnvironments(pluginId)
                    };
                };
            }

            //重载插件时自动更新模块
            {
                ModuleSystem moduleSystem = provider.GetRequiredService<ModuleSystem>();
                pluginContext.PluginLoadedAsync += (_, context) => moduleSystem.LoadAssemblyModulesAsync(context);
                pluginContext.PluginUnloadedAsync += (_, context) => moduleSystem.UnloadAssemblyModulesAsync(context);
            }

            //当客户端大版本更新时清除所有旧插件程序集
            {
                string lastClientVersion = AlifeConfig.GetString("LastClientVersion");
                if (lastClientVersion != AlifeContext.AppVersion)
                {
                    await pluginContext.ClearAllPluginDll();
                    AlifeConfig.SetString("LastClientVersion", AlifeContext.AppVersion);
                }
            }

            //额外提供的依赖注入
            ChatActivitySystem chatActivitySystem = provider.GetRequiredService<ChatActivitySystem>();
            chatActivitySystem.InjectedObjects.Add(provider.GetRequiredService<PluginSystem>());
            chatActivitySystem.InjectedObjects.Add(provider.GetRequiredService<IAlifeApplication>());
        }

        if (config.FetchOnlineInfo) //自动拉取云端插件，防呆的同时确保兼容老插件编译
        {
            progress?.Report("正在拉取插件信息...");
            try
            {
                PluginSystem pluginSystem = provider.GetRequiredService<PluginSystem>();
                await pluginSystem.SyncOnlinePluginPackages(); //老版本插件依赖云端包信息来确定编译环境，所以要放在加载插件之前
            }
            catch (Exception e)
            {
                AlifeLog.LogError("插件信息拉取失败：" + e);
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
                IAlifeApplication alifeApplication = provider.GetRequiredService<IAlifeApplication>();
                AlifeMcpTools alifeMcpTools = provider.GetRequiredService<AlifeMcpTools>();
                await alifeApplication.StartAlifeMcpAsync(alifeMcpTools);
            }
            catch (Exception e)
            {
                AlifeLog.LogError("激活MCP服务失败：" + e);
            }
        }
    }
}