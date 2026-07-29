using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.PluginMarket;
using Alife.PluginSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Framework;

public static class AlifeFramework
{
    public static void AddAlife(this IServiceCollection services)
    {
        string pluginMarketAddress = "https://github.com/BDFFZI/Alife.PluginMarket/archive/refs/heads/main.zip";

        string pluginSystemDirectory = Path.Combine(AlifePath.RuntimeFolderPath, "PluginSystem");
#if DEBUG
        string pluginDirectory = Path.Combine(AlifePath.StorageFolderPath, "PluginsDebug");
#else
        string pluginDirectory = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
#endif
        string pluginCompliedDirectory = Path.Combine(pluginSystemDirectory, "CompiledPlugins");
        string pluginMarketDirectory = Path.Combine(AlifePath.RuntimeFolderPath, "PluginMarket");

        Directory.CreateDirectory(pluginSystemDirectory);
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(pluginCompliedDirectory);
        Directory.CreateDirectory(pluginMarketDirectory);

        services.AddSingleton<CSharpCompiler>();
        services.AddSingleton<NuGetEnvironmentInstaller>(_ => new(
            Path.Combine(pluginSystemDirectory, "NuGetPackagesResolver")
        ));
        services.AddSingleton<PipEnvironmentInstaller>(_ => new(
            Path.Combine(pluginSystemDirectory, "PipPackages.txt")
        ));
        services.AddSingleton<Alife.PluginSystem.PluginSystem>(provider => new(
            pluginDirectory,
            pluginCompliedDirectory,
            new Dictionary<string, IEnvironmentInstaller>() {
                { "nuget", provider.GetRequiredService<NuGetEnvironmentInstaller>() },
                { "pip", provider.GetRequiredService<PipEnvironmentInstaller>() }
            },
            provider.GetRequiredService<CSharpCompiler>()
        ));
        services.AddSingleton<Alife.PluginMarket.PluginMarket>(provider => new(
            provider.GetRequiredService<PluginSystem.PluginSystem>(),
            new ZipPluginProvider(pluginMarketAddress),
            new FileSystemPluginInstaller(pluginDirectory),
            pluginMarketDirectory
        ));
        services.AddSingleton<StorageSystem>();
        services.AddSingleton<ConfigurationSystem>();
        services.AddSingleton<ModuleSystem>();
        services.AddSingleton<MarketSystem>();
        services.AddSingleton<CharacterSystem>();
        services.AddSingleton<ChatActivitySystem>();
    }
    public static async Task InitAlife(this IServiceProvider provider)
    {
        //插件基础设施组装
        {
            PluginSystem.PluginSystem pluginSystem = provider.GetRequiredService<PluginSystem.PluginSystem>();
            NuGetEnvironmentInstaller nugetEnvironmentInstaller = provider.GetRequiredService<NuGetEnvironmentInstaller>();
            CSharpCompiler cSharpCompiler = provider.GetRequiredService<CSharpCompiler>();
            PluginMarket.PluginMarket pluginMarket = provider.GetRequiredService<PluginMarket.PluginMarket>();

            //nuget环境变动时需要同步程序集环境
            nugetEnvironmentInstaller.PackagesUpdatedAsync += async () => {
                //重新设置编译环境
                cSharpCompiler.SetBasicDllFiles(nugetEnvironmentInstaller.Managed.Concat(AssemblyLoadContext.Default.Assemblies.Select(assembly => assembly.Location)));

                //重载nuget程序集环境
                if (PluginLoadContext.RootPluginContext != null)
                    await PluginLoadContext.RootPluginContext.DisposeAsync();
                PluginLoadContext rootPluginContext = new("NuGetPackages", nugetEnvironmentInstaller.Unmanaged.Append(AppContext.BaseDirectory).ToArray());
                foreach (string managedDirectory in nugetEnvironmentInstaller.Managed)
                {
                    foreach (string file in Directory.GetFiles(managedDirectory, "*.dll"))
                    {
                        try
                        {
                            if (AssemblyName.GetAssemblyName(file).Name != null)
                                rootPluginContext.LoadFromAssemblyPath(file);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                }
                PluginLoadContext.RootPluginContext = rootPluginContext;
            };

            //兼容老版本的插件信息
            pluginSystem.PluginManifestFallback = pluginId => {
                string versionPath = Path.Combine(pluginSystem.PluginRootDirectory, pluginId, "VERSION.txt");
                PluginPackage? pluginPackage = pluginMarket.AllPluginPackages.GetValueOrDefault(pluginId);
                return new PluginManifest() {
                    Version = File.Exists(versionPath) ? File.ReadAllText(versionPath) : "0.0.0",
                    Dependencies = pluginPackage?.GetDependencies(pluginId),
                    Environments = pluginPackage?.GetEnvironments(pluginId)
                };
            };

            //立即将插件环境加载到程序
            await pluginSystem.SyncPluginEnvironment();
        }

        //网络镜像功能
        AlifeMirror.SetupEnvironment();
    }
}
