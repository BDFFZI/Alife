using Alife.Foundation;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Framework;

public static class AlifePluginSystem
{
    public static void AddAlifePluginSystem<TCSharpCompiler>(this IServiceCollection services)
        where TCSharpCompiler : CSharpCompiler
    {
        services.AddSingletonChain<CSharpCompiler, TCSharpCompiler>();
        services.AddSingleton<PluginContext>(provider => new(
            GetPluginDirectory(),
            GetPluginCompliedDirectory(),
            provider.GetServices<IEnvironmentInstaller>().ToDictionary(installer => installer.EnvironmentName),
            provider.GetRequiredService<CSharpCompiler>()
        ));
        services.AddSingleton<PluginMarket>(provider => new(
            provider.GetRequiredService<PluginContext>(),
            new ZipPluginProvider("https://github.com/BDFFZI/Alife.PluginMarket/archive/refs/heads/main.zip"),
            new FileSystemPluginInstaller(GetPluginDirectory()),
            GetPluginMarketDirectory()
        ));
        services.AddSingleton<PluginSystem>();
    }

    static string GetPluginDirectory()
    {
        string pluginDirectory = Path.Combine(AlifePath.StorageFolderPath, "Plugins");

#if DEBUG
        if (OperatingSystem.IsWindows())
            pluginDirectory = Path.GetFullPath(AppContext.BaseDirectory + "../../../../../Sources/Alife.Function");
#endif

        Directory.CreateDirectory(pluginDirectory);
        return pluginDirectory;
    }
    static string GetPluginCompliedDirectory()
    {
        string pluginCompliedDirectory = Path.Combine(AlifePath.RuntimeFolderPath, "CompiledPlugins");
        Directory.CreateDirectory(pluginCompliedDirectory);
        return pluginCompliedDirectory;
    }
    static string GetPluginMarketDirectory()
    {
        string pluginCompliedDirectory = Path.Combine(AlifePath.RuntimeFolderPath, "PluginMarket");
        Directory.CreateDirectory(pluginCompliedDirectory);
        return pluginCompliedDirectory;
    }
}