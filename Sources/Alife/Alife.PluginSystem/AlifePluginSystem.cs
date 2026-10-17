using System.Reflection;
using System.Runtime.Loader;
using Alife.Foundation;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Framework;

public static class AlifePluginSystem
{
    public static void AddAlifePluginSystem<T>(this IServiceCollection services) where T : class, IPluginRuntime
    {
        services.AddSingleton<IPluginRuntime, T>();
        //添加插件框架
        string pluginMarketAddress = "https://github.com/BDFFZI/Alife.PluginMarket/archive/refs/heads/main.zip";
        string pluginContextDirectory = Path.Combine(AlifePath.RuntimeFolderPath, "PluginContext");
        string pluginDirectory = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
        string pluginCompliedDirectory = Path.Combine(pluginContextDirectory, "CompiledPlugins");
        string pluginMarketDirectory = Path.Combine(AlifePath.RuntimeFolderPath, "PluginMarket");

#if DEBUG
        if (OperatingSystem.IsWindows())
            pluginDirectory = Path.GetFullPath(AppContext.BaseDirectory + "../../../../../Sources/Alife.Function");
#endif

        Directory.CreateDirectory(pluginContextDirectory);
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(pluginCompliedDirectory);
        Directory.CreateDirectory(pluginMarketDirectory);

        services.AddSingleton<CSharpCompiler>(_ => {
            CSharpCompiler compiler = new();
            compiler.SetBasicDllFiles(GetReferenceFiles());
            return compiler;
        });
        services.AddSingleton<NuGetEnvironmentInstaller>(provider => {
            NuGetEnvironmentInstaller nugetEnvironmentInstaller = new(
                Path.Combine(pluginContextDirectory, "NuGetPackagesResolver"), provider.GetRequiredService<IPluginRuntime>());
            nugetEnvironmentInstaller.PackagesUpdatedAsync += async () => {
                HashSet<string> defaultAssemblies = AssemblyLoadContext.Default.Assemblies
                    .Select(assembly => assembly.GetName().Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Cast<string>().ToHashSet();

                //重新设置编译环境
                string[] compilingDlls = GetReferenceFiles()
                    .Concat(nugetEnvironmentInstaller.CompilingDlls
                        .Where(dll => AlifeUtility.IsValidDll(dll, out var name) && !defaultAssemblies.Contains(name)))
                    .ToArray();
                provider.GetRequiredService<CSharpCompiler>().SetBasicDllFiles(compilingDlls);

                //重载程序集运行环境
                if (PluginLoadContext.RootPluginContext != null)
                    await PluginLoadContext.RootPluginContext.DisposeAsync();
                PluginLoadContext rootPluginContext =
                    new("NuGetPackages", nugetEnvironmentInstaller.UnmanagedDirectories.Append(AppContext.BaseDirectory).ToArray());
                IEnumerable<string> runtimeDllFiles = nugetEnvironmentInstaller.RuntimeDlls
                    .Where(file => AlifeUtility.IsValidDll(file, out string name) && !defaultAssemblies.Contains(name));
                foreach (string file in runtimeDllFiles)
                    rootPluginContext.LoadDll(file);
                PluginLoadContext.RootPluginContext = rootPluginContext;
            };
            return nugetEnvironmentInstaller;
        });
        services.AddSingleton<PipEnvironmentInstaller>(_ => new(
            Path.Combine(pluginContextDirectory, "PipPackages.txt")
        ));
        services.AddSingleton<PluginContext>(provider => new(
            pluginDirectory,
            pluginCompliedDirectory,
            new Dictionary<string, IEnvironmentInstaller>() {
                { "nuget", provider.GetRequiredService<NuGetEnvironmentInstaller>() },
                { "pip", provider.GetRequiredService<PipEnvironmentInstaller>() }
            },
            provider.GetRequiredService<CSharpCompiler>()
        ));
        services.AddSingleton<PluginMarket>(provider => new(
            provider.GetRequiredService<PluginContext>(),
            new ZipPluginProvider(pluginMarketAddress),
            new FileSystemPluginInstaller(pluginDirectory),
            pluginMarketDirectory
        ));
        services.AddSingleton<PluginSystem>();
    }

    static IEnumerable<string> GetReferenceFiles()
    {
        // Include transitive assemblies needed to compile plugin source.
        HashSet<string> loaded = new(StringComparer.OrdinalIgnoreCase);
        Queue<Assembly> queue = new(AssemblyLoadContext.Default.Assemblies);
        while (queue.TryDequeue(out var assembly))
        {
            if (!loaded.Add(assembly.FullName!)) continue;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch
                {
                    // 忽略加载失败的程序集（有些可能是环境相关的）
                }
            }
        }
        return AssemblyLoadContext.Default.Assemblies.Select(assembly => assembly.Location)
            .Where(location => !string.IsNullOrEmpty(location));
    }
}