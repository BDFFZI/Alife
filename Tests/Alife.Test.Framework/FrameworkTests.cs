using Alife.Framework;
using Alife.PluginContext;
using Alife.PluginMarket;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public class FrameworkTests
{
    [Test]
    public async Task TestFramework()
    {
        ServiceCollection serviceCollection = new();
        serviceCollection.AddAlife();
        ServiceProvider provider = serviceCollection.BuildServiceProvider();
        provider.InitAlife();

        PluginSystem pluginSystem = provider.GetRequiredService<PluginSystem>();
        await pluginSystem.SyncOnlinePluginPackages();

        LogOnlinePlugins(pluginSystem);

        LogLocalPlugins(pluginSystem);

        TestContext.WriteLine("安装插件:");
        await pluginSystem.InstallPlugins([
            new KeyValuePair<PluginPackage, string>(pluginSystem.GetAllOnlinePlugins()["Alife.Function.Python"], "1.0.0")
        ]);

        LogLocalPlugins(pluginSystem);

        TestContext.WriteLine("卸载插件:");
        await pluginSystem.UninstallPlugins([pluginSystem.GetAllOnlinePlugins()["Alife.Function.Python"]]);

        LogLocalPlugins(pluginSystem);
    }

    void LogOnlinePlugins(PluginSystem pluginSystem)
    {
        TestContext.WriteLine("在线插件:");
        foreach (KeyValuePair<string, PluginPackage> plugin in pluginSystem.GetAllOnlinePlugins())
            TestContext.WriteLine($"{plugin.Key}:{plugin.Value.Name}");
    }

    void LogLocalPlugins(PluginSystem pluginSystem)
    {
        TestContext.WriteLine("本地插件:");
        foreach (KeyValuePair<string, PluginManifest> plugin in pluginSystem.GetAllLocalPlugins())
            TestContext.WriteLine($"{plugin.Key}:{plugin.Value.Version}");
    }
}