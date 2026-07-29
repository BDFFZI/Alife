using Alife.Framework;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public class FrameworkTests
{
    [Test]
    public async Task TestFramework()
    {
        ServiceCollection serviceCollection = new ServiceCollection();
        serviceCollection.AddAlife();
        ServiceProvider provider = serviceCollection.BuildServiceProvider();
        await provider.InitAlife();
    }
}
