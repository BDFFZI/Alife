using Alife.Foundation;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Framework;

/// <summary>
/// 用于构建无头Alife，例如实现通过自编译、CLI、MCP来使用。
/// </summary>
public static class AlifeApplication
{
    public static void AddAlife<TAlifeFoundation, TCSharpCompiler, TNuGetEnvironmentInstaller, TPipEnvironmentInstaller>(
        this IServiceCollection services)
        where TAlifeFoundation : class, IAlifeFoundation
        where TCSharpCompiler : CSharpCompiler
        where TNuGetEnvironmentInstaller : NuGetEnvironmentInstaller
        where TPipEnvironmentInstaller : PipEnvironmentInstaller
    {
        //添加AI框架
        services.AddAlifeFoundation<TAlifeFoundation>();
        services.AddAlifeFramework();
        services.AddAlifePluginSystem<TCSharpCompiler>();

        //添加默认的插件环境
        services.AddSingletonChain<IEnvironmentInstaller, NuGetEnvironmentInstaller, TNuGetEnvironmentInstaller>();
        services.AddSingletonChain<IEnvironmentInstaller, PipEnvironmentInstaller, TPipEnvironmentInstaller>();
    }

    public static async Task InitAlife(this IServiceProvider provider, IProgress<string>? progress = null)
    {
        {
            progress?.Report("正在初始化基础环境...");
            await provider.InitAlifeFoundation();
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

            //将扩展的插件系统注入到基础框架中
            {
                //重载插件时自动更新模块
                ModuleSystem moduleSystem = provider.GetRequiredService<ModuleSystem>();
                pluginContext.PluginLoadedAsync += (_, context) => moduleSystem.LoadAssemblyModulesAsync(context);
                pluginContext.PluginUnloadedAsync += (_, context) => moduleSystem.UnloadAssemblyModulesAsync(context);
                //额外提供插件系统的依赖注入
                ChatActivitySystem chatActivitySystem = provider.GetRequiredService<ChatActivitySystem>();
                chatActivitySystem.InjectedObjects.Add(provider.GetRequiredService<PluginSystem>());
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
        }
    }
}