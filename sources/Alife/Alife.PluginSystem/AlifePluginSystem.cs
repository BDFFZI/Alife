using System.Reflection;
using System.Runtime.Loader;
using Alife.Foundation;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Framework;

public static class AlifePluginSystem
{
    public static void AddAlifePluginSystem(this IServiceCollection services)
    {
        //添加插件框架
        string pluginMarketAddress = "https://github.com/BDFFZI/Alife.PluginMarket/archive/refs/heads/main.zip";

        string pluginContextDirectory = Path.Combine(AlifePath.RuntimeFolderPath, "PluginContext");
#if DEBUG
        //开发模式下，将插件根目录指向 Alife.Function 源码目录（Alife.Client.Core 与 Alife.Function 的相对目录），
        //以便直接以插件项目目录作为热更新源码加载。
        string pluginDirectory = Path.GetFullPath(@"..\..\Alife.Function");
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
            compiler.SetBasicDllFiles(AssemblyLoadContext.Default.Assemblies
                .Select(assembly => assembly.Location)
                .Where(location => !string.IsNullOrEmpty(location)));
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
        services.AddSingleton<NuGetEnvironmentInstaller>(provider =>
        {
            NuGetEnvironmentInstaller nugetEnvironmentInstaller = new(
                Path.Combine(pluginContextDirectory, "NuGetPackagesResolver")
            );
            nugetEnvironmentInstaller.PackagesUpdatedAsync += async () =>
            {
                HashSet<string> defaultAssemblies = AssemblyLoadContext.Default.Assemblies
                    .Select(assembly => assembly.GetName().Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Cast<string>().ToHashSet();

                //重新设置编译环境
                string[] compilingDlls = AssemblyLoadContext.Default.Assemblies
                    .Select(assembly => assembly.Location)
                    .Where(location => !string.IsNullOrEmpty(location))
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
}