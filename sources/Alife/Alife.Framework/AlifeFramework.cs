using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Alife.Platform;
using Alife.PluginMarket;
using Alife.PluginContext;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Framework;
public static class AlifeFramework
{
    public static void AddAlife(this IServiceCollection services)
    {
        string pluginMarketAddress = "https://github.com/BDFFZI/Alife.PluginMarket/archive/refs/heads/main.zip";

        string pluginContextDirectory = Path.Combine(AlifePath.RuntimeFolderPath, "PluginContext");
#if DEBUG
        string pluginDirectory = Path.Combine(AlifePath.StorageFolderPath, "PluginsDebug");
#else
        string pluginDirectory = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
#endif
        string pluginCompliedDirectory = Path.Combine(pluginContextDirectory, "CompiledPlugins");
        string pluginMarketDirectory = Path.Combine(AlifePath.RuntimeFolderPath, "PluginMarket");

        Directory.CreateDirectory(pluginContextDirectory);
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(pluginCompliedDirectory);
        Directory.CreateDirectory(pluginMarketDirectory);

        services.AddSingleton<CSharpCompiler>(_ =>
        {
            CSharpCompiler compiler = new();
            //立即加载软件依赖的所有程序集，这样就可以获取到dotnet运行时的dll，以便用于插件功能
            LoadAssemblyChain(Assembly.GetEntryAssembly()!);
            compiler.SetBasicDllFiles(AssemblyLoadContext.Default.Assemblies.Select(assembly => assembly.Location));
            return compiler;

            void LoadAssemblyChain(Assembly entryAssembly)
            {
                var loadedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<Assembly>();
                queue.Enqueue(entryAssembly);
                while (queue.Count > 0)
                {
                    var assembly = queue.Dequeue();
                    foreach (var reference in assembly.GetReferencedAssemblies())
                    {
                        // 如果这个程序集还没被加载过
                        if (!loadedAssemblies.Contains(reference.FullName))
                        {
                            try
                            {
                                // 强制加载它
                                var loaded = Assembly.Load(reference);
                                queue.Enqueue(loaded);
                                loadedAssemblies.Add(reference.FullName);
                            }
                            catch
                            {
                                // 忽略加载失败的程序集（有些可能是环境相关的）
                            }
                        }
                    }
                }
            }
        });
        services.AddSingleton<NuGetEnvironmentInstaller>(_ => new(
            Path.Combine(pluginContextDirectory, "NuGetPackagesResolver")
        ));
        services.AddSingleton<PipEnvironmentInstaller>(_ => new(
            Path.Combine(pluginContextDirectory, "PipPackages.txt")
        ));
        services.AddSingleton<Alife.PluginContext.PluginContext>(provider => new(
            pluginDirectory,
            pluginCompliedDirectory,
            new Dictionary<string, IEnvironmentInstaller>()
            {
                { "nuget", provider.GetRequiredService<NuGetEnvironmentInstaller>() },
                { "pip", provider.GetRequiredService<PipEnvironmentInstaller>() }
            },
            provider.GetRequiredService<CSharpCompiler>()
        ));
        services.AddSingleton<Alife.PluginMarket.PluginMarket>(provider => new(
            provider.GetRequiredService<PluginContext.PluginContext>(),
            new ZipPluginProvider(pluginMarketAddress),
            new FileSystemPluginInstaller(pluginDirectory),
            pluginMarketDirectory
        ));
        services.AddSingleton<StorageSystem>();
        services.AddSingleton<ConfigurationSystem>();
        services.AddSingleton<ModuleSystem>();
        services.AddSingleton<PluginSystem>();
        services.AddSingleton<CharacterSystem>();
        services.AddSingleton<ChatActivitySystem>();
    }
    public static void InitAlife(this IServiceProvider provider)
    {
        //网络镜像功能
        AlifeMirror.SetupEnvironment();

        //插件基础设施组装
        {
            PluginContext.PluginContext pluginContext = provider.GetRequiredService<PluginContext.PluginContext>();
            NuGetEnvironmentInstaller nugetEnvironmentInstaller = provider.GetRequiredService<NuGetEnvironmentInstaller>();
            CSharpCompiler cSharpCompiler = provider.GetRequiredService<CSharpCompiler>();
            PluginMarket.PluginMarket pluginMarket = provider.GetRequiredService<PluginMarket.PluginMarket>();


            //nuget环境变动时需要同步程序集环境
            nugetEnvironmentInstaller.PackagesUpdatedAsync += async () =>
            {
                //重新设置编译环境
                cSharpCompiler.SetBasicDllFiles(
                    nugetEnvironmentInstaller.Managed.Concat(AssemblyLoadContext.Default.Assemblies.Select(assembly => assembly.Location)));

                //重载nuget程序集环境
                if (PluginLoadContext.RootPluginContext != null)
                    await PluginLoadContext.RootPluginContext.DisposeAsync();
                PluginLoadContext rootPluginContext =
                    new("NuGetPackages", nugetEnvironmentInstaller.Unmanaged.Append(AppContext.BaseDirectory).ToArray());
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
            pluginContext.PluginManifestFallback = pluginId =>
            {
                string versionPath = Path.Combine(pluginContext.PluginRootDirectory, pluginId, "VERSION.txt");
                PluginPackage? pluginPackage = pluginMarket.AllPluginPackages.GetValueOrDefault(pluginId);
                return new PluginManifest()
                {
                    Version = File.Exists(versionPath) ? File.ReadAllText(versionPath) : "0.0.0",
                    Dependencies = pluginPackage?.GetDependencies(pluginId),
                    Environments = pluginPackage?.GetEnvironments(pluginId)
                };
            };
        }
    }
}