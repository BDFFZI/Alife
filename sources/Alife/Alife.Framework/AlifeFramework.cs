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
            Path.Combine(pluginSystemDirectory, "NuGetPackages.txt"),
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
        //插件环境配置与自动加载
        {
            PluginSystem.PluginSystem pluginSystem = provider.GetRequiredService<PluginSystem.PluginSystem>();
            NuGetEnvironmentInstaller nugetEnvironmentInstaller = provider.GetRequiredService<NuGetEnvironmentInstaller>();
            CSharpCompiler cSharpCompiler = provider.GetRequiredService<CSharpCompiler>();

            //兼容老版本的插件信息
            PluginMarket.PluginMarket pluginMarket = provider.GetRequiredService<PluginMarket.PluginMarket>();
            provider.GetRequiredService<PluginSystem.PluginSystem>().PluginManifestFallback = pluginId => {
                string versionPath = Path.Combine(pluginSystem.PluginRootDirectory, pluginId, "VERSION.txt");
                PluginPackage? pluginPackage = pluginMarket.AllPluginPackages.GetValueOrDefault(pluginId);
                return new PluginManifest() {
                    Version = File.Exists(versionPath) ? File.ReadAllText(versionPath) : "0.0.0",
                    Dependencies = pluginPackage?.GetDependencies(pluginId),
                    Environments = pluginPackage?.GetEnvironments(pluginId)
                };
            };

            //同步插件环境（解算nuget环境）
            await pluginSystem.SyncPluginEnvironment();
            (HashSet<string> managed, HashSet<string> unmanaged) = nugetEnvironmentInstaller.GetPackageManifest();

            //加载插件
            await ReloadPluginContext(managed, unmanaged);

            //后续nuget变化时也需要重载插件环境
            nugetEnvironmentInstaller.PackageManifestUpdated += ReloadPluginContext;

            async Task ReloadPluginContext(HashSet<string> managed, HashSet<string> unmanaged)
            {
                //释放旧的nuget程序集
                if (PluginLoadContext.RootPluginContext != null)
                    await PluginLoadContext.RootPluginContext.DisposeAsync();

                //重新设置编译环境
                cSharpCompiler.SetBasicDllFiles(managed.Concat(AssemblyLoadContext.Default.Assemblies.Select(assembly => assembly.Location)));

                //重新设置nuget程序集环境
                PluginLoadContext rootPluginContext = new("NuGetPackages", unmanaged.Append(AppContext.BaseDirectory).ToArray());
                foreach (string managedDirectory in managed)
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

                //重新加载插件
                await pluginSystem.ReloadAllPluginDlls();
            }
        }

        //网络镜像功能
        AlifeMirror.SetupEnvironment();
    }
}
