using Alife.Framework;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Alife.Test.Framework;

class ConstructContainerTests
{
    [Test]
    public async Task Test()
    {
        ConstructContainer container = new();
        container.RegisterBuilder(typeof(SystemA));
        container.RegisterBuilder(typeof(ModuleA));
        container.RegisterBuilder(typeof(LoggerFactory), _ => {
            return Task.FromResult<object>(LoggerFactory.Create(builder => builder.AddConsole()));
        });
        container.RegisterBuilder(typeof(Logger<>));
        container.RegisterBuilder(typeof(PluginA));
        container.RegisterBuilder(typeof(PluginB));
        container.RegisterBuilder(typeof(PluginC));
        container.RegisterBuilder(typeof(PluginD));

        await container.RequireInstance(typeof(PluginD));
        foreach (object obj in container.Instances)
            Console.WriteLine(obj.ToString());
    }


    class SystemA;

    interface IModel;

    class ModuleA : IModel;

    class PluginA(SystemA system);

    class PluginB(IModel model);

    class PluginC(ILogger<PluginC> logger);

    class PluginD(PluginA pluginA, PluginB pluginBm, PluginC pluginC);
}
