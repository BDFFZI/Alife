using Alife.Foundation;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Framework;

public static class AlifeApp
{
    public static void AddAlife(this IServiceCollection services)
    {
        //添加AI框架
        services.AddAlifeFramework();
        //添加插件框架
        services.AddAlifePluginSystem();
    }

    public static async Task InitAlife(this IServiceProvider provider, IProgress<string>? progress = null)
    {
        {
            progress?.Report("正在初始化基础功能...");
            AlifeLog.Initialize(); //激活静态日志功能
            AlifeMirror.Initialize(); //激活网络镜像功能
        }

        {
            progress?.Report("正在初始化插件环境...");

            PluginContext pluginContext = provider.GetRequiredService<PluginContext>();
            PluginMarket pluginMarket = provider.GetRequiredService<PluginMarket>();

            //老版本插件信息兼容
            {
                //生成老版版本文件
                pluginMarket.PluginInstalled += (pluginPackage, version) =>
                {
                    if (File.Exists(pluginContext.GetPluginManifestPath(pluginPackage.Id)) == false)
                    {
                        string versionFile = Path.Combine(pluginContext.GetPluginDirectoryPath(pluginPackage.Id), "VERSION.txt");
                        File.WriteAllText(versionFile, version);
                    }
                };
                //从版本文件或插件市场中获取插件环境信息
                pluginContext.PluginManifestFallback = pluginId =>
                {
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
                string lastClientVersion = AlifeConfig.GetString("ClientVersion");
                if (lastClientVersion != AlifeContext.AppVersion)
                {
                    await pluginContext.ClearAllPluginDll();
                    AlifeConfig.SetString("ClientVersion", AlifeContext.AppVersion);
                }
            }
        }
    }
}