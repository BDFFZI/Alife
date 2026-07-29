using Alife.Framework;
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
        await provider.InitAlife();

        MarketSystem marketSystem = provider.GetRequiredService<MarketSystem>();

        await marketSystem.SyncPluginMarket();
        foreach (KeyValuePair<string, PluginPackage> plugin in marketSystem.GetAllOnlinePlugins())
            TestContext.WriteLine($"{plugin.Key}:{plugin.Value.Name}");
    }
}
