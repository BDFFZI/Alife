using Alife.Framework;
using NUnit.Framework;

namespace Alife.Test.Framework;

/// <summary>
/// 链状重载回归测试：验证模块依赖链在 Awake / 重载 / 卸载时的生命周期顺序。
/// 依赖链 A→B→C（A 依赖 B，B 依赖 C）中：
/// - OnAwake 应按"依赖先于依赖方"（C,B,A）
/// - OnDestroy 应按"依赖方先于依赖"（A,B,C），即卸载整条链时先销毁最上层使用者
/// - 重载时共享依赖应被保留，不被误卸载
/// </summary>
[TestFixture]
public class ChainReloadTests
{
    static readonly List<string> Lifecycle = new();

    [SetUp]
    public void Setup() => Lifecycle.Clear();

    // ---- 插件模块（模拟若干插件形成的依赖链 + 共享依赖）----

    /// <summary>链尾：无依赖。</summary>
    class ChainC : ChatBehaviour
    {
        protected override Task OnAwake() { Lifecycle.Add("C:Awake"); return Task.CompletedTask; }
        protected override Task OnDestroy() { Lifecycle.Add("C:Destroy"); return Task.CompletedTask; }
    }

    /// <summary>依赖 ChainC。</summary>
    class ChainB : ChatBehaviour
    {
        public ChainB(ChainC c) { }
        protected override Task OnAwake() { Lifecycle.Add("B:Awake"); return Task.CompletedTask; }
        protected override Task OnDestroy() { Lifecycle.Add("B:Destroy"); return Task.CompletedTask; }
    }

    /// <summary>依赖 ChainB，构成链 A→B→C。</summary>
    class ChainA : ChatBehaviour
    {
        public ChainA(ChainB b) { }
        protected override Task OnAwake() { Lifecycle.Add("A:Awake"); return Task.CompletedTask; }
        protected override Task OnDestroy() { Lifecycle.Add("A:Destroy"); return Task.CompletedTask; }
    }

    /// <summary>也依赖 ChainC，与链共享该依赖。</summary>
    class SharedUser : ChatBehaviour
    {
        public SharedUser(ChainC c) { }
        protected override Task OnAwake() { Lifecycle.Add("S:Awake"); return Task.CompletedTask; }
        protected override Task OnDestroy() { Lifecycle.Add("S:Destroy"); return Task.CompletedTask; }
    }

    /// <summary>与链无关的独立模块。</summary>
    class Standalone : ChatBehaviour
    {
        protected override Task OnAwake() { Lifecycle.Add("O:Awake"); return Task.CompletedTask; }
        protected override Task OnDestroy() { Lifecycle.Add("O:Destroy"); return Task.CompletedTask; }
    }

    /// <summary>模拟 ChatActivity.Awake 中对全部实例依次 Awake（依赖先创建，故先 Awake）。</summary>
    static async Task AwakeAll(ConstructContainer container)
    {
        foreach (object instance in container.Instances)
        {
            if (instance is ChatBehaviour behaviour)
                await behaviour.AwakeAsync(new AwakeContext());
        }
    }

    /// <summary>
    /// 模拟 ChatActivity.OnCharacterChangedAsync 的冗余判定逻辑：
    /// 保留集 = 启用实例及其（递归）依赖，其余 ChatBehaviour 视为冗余并卸载。
    /// </summary>
    static async Task RemoveRedundant(ConstructContainer container, params object[] enabledInstances)
    {
        List<object> kept = container.CollectDependents(enabledInstances);
        List<object> redundantModules = container.Instances
            .Where(instance => instance is ChatBehaviour && kept.Contains(instance) == false)
            .ToList();
        foreach (object redundantModule in redundantModules)
            await container.RemoveInstance(redundantModule);
    }

    [Test]
    public async Task Chain_Awake_DependenciesFirst()
    {
        ConstructContainer container = new();
        container.RegisterBuilder(typeof(ChainC));
        container.RegisterBuilder(typeof(ChainB));
        container.RegisterBuilder(typeof(ChainA));

        await container.RequireInstance(typeof(ChainA)); // 依次构造 C,B,A
        await AwakeAll(container);

        Assert.That(Lifecycle, Is.EqualTo(new[] { "C:Awake", "B:Awake", "A:Awake" }));
    }

    [Test]
    public async Task Chain_Unload_DestroysDependentsFirst()
    {
        ConstructContainer container = new();
        container.RegisterBuilder(typeof(ChainC));
        container.RegisterBuilder(typeof(ChainB));
        container.RegisterBuilder(typeof(ChainA));

        await container.RequireInstance(typeof(ChainA));
        await AwakeAll(container);
        Lifecycle.Clear();

        // 重载后启用集为空 → 整条链冗余卸载
        await RemoveRedundant(container, []);

        // 卸载顺序：依赖方先于依赖（A,B,C），与 Awake 顺序相反
        Assert.That(Lifecycle, Is.EqualTo(new[] { "A:Destroy", "B:Destroy", "C:Destroy" }));
        Assert.That(container.Instances, Is.Empty);
    }

    [Test]
    public async Task Chain_Reload_KeepsSharedDependencies()
    {
        ConstructContainer container = new();
        container.RegisterBuilder(typeof(ChainC));
        container.RegisterBuilder(typeof(ChainB));
        container.RegisterBuilder(typeof(ChainA));
        container.RegisterBuilder(typeof(SharedUser));

        await container.RequireInstance(typeof(ChainA));
        await container.RequireInstance(typeof(SharedUser));
        await AwakeAll(container);
        Lifecycle.Clear();

        object sharedUser = container.Instances.Single(instance => instance is SharedUser);
        // 重载后仅保留 SharedUser → 共享的 ChainC 应保留，A/B 冗余卸载
        await RemoveRedundant(container, sharedUser);

        Assert.That(Lifecycle, Is.EqualTo(new[] { "A:Destroy", "B:Destroy" }));
        Assert.That(container.Instances, Has.Member(sharedUser));
        Assert.That(container.Instances.Any(instance => instance is ChainC), Is.True);
        Assert.That(container.Instances.Any(instance => instance is ChainA), Is.False);
        Assert.That(container.Instances.Any(instance => instance is ChainB), Is.False);
    }

    [Test]
    public async Task Chain_Unload_RemovesUnrelatedStandalone()
    {
        ConstructContainer container = new();
        container.RegisterBuilder(typeof(ChainC));
        container.RegisterBuilder(typeof(ChainB));
        container.RegisterBuilder(typeof(ChainA));
        container.RegisterBuilder(typeof(Standalone));

        await container.RequireInstance(typeof(ChainA));
        await container.RequireInstance(typeof(Standalone));
        await AwakeAll(container);
        Lifecycle.Clear();

        // 重载后仅保留 ChainA → Standalone 与其无依赖关系，应被卸载
        object chainA = container.Instances.Single(instance => instance is ChainA);
        await RemoveRedundant(container, chainA);

        Assert.That(Lifecycle, Is.EqualTo(new[] { "O:Destroy" }));
        Assert.That(container.Instances.Any(instance => instance is Standalone), Is.False);
        Assert.That(container.Instances.Any(instance => instance is ChainA), Is.True);
        Assert.That(container.Instances.Any(instance => instance is ChainC), Is.True);
    }

    [Test]
    public async Task Chain_ReEnable_RecreatesAndAwakes()
    {
        ConstructContainer container = new();
        container.RegisterBuilder(typeof(ChainC));
        container.RegisterBuilder(typeof(ChainB));
        container.RegisterBuilder(typeof(ChainA));

        await container.RequireInstance(typeof(ChainA));
        await AwakeAll(container);

        // 全部卸载
        await RemoveRedundant(container, []);
        Assert.That(container.Instances, Is.Empty);
        Assert.That(Lifecycle, Is.EqualTo(new[] { "C:Awake", "B:Awake", "A:Awake", "A:Destroy", "B:Destroy", "C:Destroy" }));

        Lifecycle.Clear();

        // 重新启用 ChainA → 重建依赖链并重新 Awake（模拟插件热重载）
        await container.RequireInstance(typeof(ChainA));
        await AwakeAll(container);

        Assert.That(Lifecycle, Is.EqualTo(new[] { "C:Awake", "B:Awake", "A:Awake" }));
        Assert.That(container.Instances, Has.Count.EqualTo(3));
    }
}