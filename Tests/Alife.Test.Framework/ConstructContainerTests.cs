using Alife.Framework;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
#pragma warning disable CS9113 // 参数未读。

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

    [Test]
    public async Task GetInstanceDependents_ReturnsWhatInstanceDependsOn()
    {
        ConstructContainer container = new();
        container.RegisterBuilder(typeof(EnabledModuleA));
        container.RegisterBuilder(typeof(HelperDependency));

        object enabledA = await container.RequireInstance(typeof(EnabledModuleA));
        object helper = container.Instances.Single(instance => instance is HelperDependency);

        //启用模块依赖 helper，因此应返回 helper；而非"谁依赖该模块"
        Assert.That(container.GetInstanceDependents(enabledA), Has.Member(helper));
        //helper 不依赖任何实例
        Assert.That(container.GetInstanceDependents(helper), Is.Empty);

        //反向查找：helper 被启用模块依赖，启用模块不被任何实例依赖
        Assert.That(container.GetDependentsOf(helper), Has.Member(enabledA));
        Assert.That(container.GetDependentsOf(enabledA), Is.Empty);
    }

    [Test]
    public async Task CollectDependents_KeepsEnabledChain_ExcludesRedundant()
    {
        ConstructContainer container = new();
        container.RegisterBuilder(typeof(EnabledModuleA));
        container.RegisterBuilder(typeof(HelperDependency));
        container.RegisterBuilder(typeof(DisabledModuleB));
        container.RegisterBuilder(typeof(DisabledModuleC));
        container.RegisterBuilder(typeof(ExclHelper));

        await container.RequireInstance(typeof(EnabledModuleA));
        await container.RequireInstance(typeof(DisabledModuleB));
        await container.RequireInstance(typeof(DisabledModuleC));

        object enabledA = container.Instances.Single(instance => instance is EnabledModuleA);
        List<object> kept = container.CollectDependents([enabledA]);

        //保留：启用模块 + 其（递归）依赖
        Assert.That(kept, Has.Member(enabledA));
        Assert.That(kept, Has.Member(container.Instances.Single(instance => instance is HelperDependency)));
        //移除：未启用且不在依赖链中的模块（即使它依赖启用模块）
        Assert.That(kept, Has.None.Matches<object>(instance => instance is DisabledModuleB));
        Assert.That(kept, Has.None.Matches<object>(instance => instance is DisabledModuleC));
        Assert.That(kept, Has.None.Matches<object>(instance => instance is ExclHelper));
    }

    [Test]
    public async Task RedundantModules_AreRemoved_FromContainer()
    {
        ConstructContainer container = new();
        container.RegisterBuilder(typeof(EnabledModuleA));
        container.RegisterBuilder(typeof(HelperDependency));
        container.RegisterBuilder(typeof(DisabledModuleB));
        container.RegisterBuilder(typeof(DisabledModuleC));
        container.RegisterBuilder(typeof(ExclHelper));

        await container.RequireInstance(typeof(EnabledModuleA));
        await container.RequireInstance(typeof(DisabledModuleB));
        await container.RequireInstance(typeof(DisabledModuleC));

        object enabledA = container.Instances.Single(instance => instance is EnabledModuleA);
        //与 ChatActivity.OnCharacterChangedAsync 相同的冗余判定逻辑
        List<object> kept = container.CollectDependents([enabledA]);
        List<object> redundantModules = container.Instances
            .Where(instance => instance is ChatBehaviour && kept.Contains(instance) == false)
            .ToList();
        foreach (object redundantModule in redundantModules)
            await container.RemoveInstance(redundantModule);

        //保留：启用模块 + 其依赖
        Assert.That(container.Instances, Has.Member(enabledA));
        Assert.That(container.Instances, Has.Member(container.Instances.Single(instance => instance is HelperDependency)));
        //冗余模块及其专属依赖被卸载
        Assert.That(container.Instances, Has.None.Matches<object>(instance => instance is DisabledModuleB));
        Assert.That(container.Instances, Has.None.Matches<object>(instance => instance is DisabledModuleC));
        Assert.That(container.Instances, Has.None.Matches<object>(instance => instance is ExclHelper));
    }

    class EnabledModuleA : ChatBehaviour
    {
        public EnabledModuleA(HelperDependency helper) { }
    }

    class HelperDependency : ChatBehaviour;

    class DisabledModuleB : ChatBehaviour
    {
        public DisabledModuleB(EnabledModuleA enabledA) { }
    }

    class DisabledModuleC : ChatBehaviour
    {
        public DisabledModuleC(ExclHelper helper) { }
    }

    class ExclHelper : ChatBehaviour;
}
