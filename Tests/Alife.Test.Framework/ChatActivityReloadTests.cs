using Alife.Foundation;
using Alife.Framework;
using Alife.PluginContext;
using NUnit.Framework;

namespace Alife.Test.Framework;

/// <summary>
/// 真实 ChatActivity 链状重载回归测试。
/// 使用真实的 StorageSystem/ConfigurationSystem/CharacterSystem/ModuleSystem 构造 ChatActivity，
/// 通过 CharacterSystem.SaveCharacter 触发 OnCharacterChangedAsync 重载流程，
/// 验证依赖链模块在 OnAwake/OnDestroy 的生命周期顺序与冗余卸载是否正确。
///
/// 注意：需以 Release 配置运行（客户端 Debug 进程独占 %TEMP%\Alife.ClientDebug\alife.log）。
/// </summary>
[TestFixture]
public class ChatActivityReloadTests
{
    static readonly List<string> Lifecycle = new();

    // ---- 插件模块（[Module] 会被 ModuleSystem 自动识别）----

    [Module("ChainC", "链式测试-链尾", defaultCategory: "链式测试")]
    class ChainC : ChatBehaviour
    {
        protected override Task OnAwake() { Lifecycle.Add("C:Awake"); return Task.CompletedTask; }
        protected override Task OnDestroy() { Lifecycle.Add("C:Destroy"); return Task.CompletedTask; }
    }

    [Module("ChainB", "链式测试-中段", defaultCategory: "链式测试")]
    class ChainB : ChatBehaviour
    {
        public ChainB(ChainC c) { }
        protected override Task OnAwake() { Lifecycle.Add("B:Awake"); return Task.CompletedTask; }
        protected override Task OnDestroy() { Lifecycle.Add("B:Destroy"); return Task.CompletedTask; }
    }

    [Module("ChainA", "链式测试-头", defaultCategory: "链式测试")]
    class ChainA : ChatBehaviour
    {
        public ChainA(ChainB b) { }
        protected override Task OnAwake() { Lifecycle.Add("A:Awake"); return Task.CompletedTask; }
        protected override Task OnDestroy() { Lifecycle.Add("A:Destroy"); return Task.CompletedTask; }
    }

    [Module("SharedUser", "链式测试-共享使用者", defaultCategory: "链式测试")]
    class SharedUser : ChatBehaviour
    {
        public SharedUser(ChainC c) { }
        protected override Task OnAwake() { Lifecycle.Add("S:Awake"); return Task.CompletedTask; }
        protected override Task OnDestroy() { Lifecycle.Add("S:Destroy"); return Task.CompletedTask; }
    }

    [Module("FakeLanguageModel", "链式测试-伪语言模型", defaultCategory: "链式测试")]
    class FakeLanguageModel : ChatBehaviour, ILanguageModel
    {
        protected override Task OnAwake() => Task.CompletedTask;
        public Task<string> ChatStreamingAsync(
            Microsoft.SemanticKernel.Agents.ChatHistoryAgentThread chatHistoryAgentThread,
            Action<string>? textReceived = null,
            Action<string>? thinkReceived = null,
            Action<TokenUsage>? tokenUsed = null,
            Action<Exception>? exceptionThrow = null,
            CancellationToken cancellationToken = default) => Task.FromResult("");
    }

    [SetUp]
    public void Setup() => Lifecycle.Clear();

    [TearDown]
    public async Task Teardown()
    {
        if (activity != null)
        {
            await activity.Destroy();
            activity = null;
        }
        if (character != null)
        {
            characterSystem!.DeleteCharacter(character);
            character = null;
        }
    }

    StorageSystem storageSystem = null!;
    ConfigurationSystem configurationSystem = null!;
    CharacterSystem characterSystem = null!;
    ModuleSystem moduleSystem = null!;
    ChatActivity? activity;
    Character? character;

    async Task<Character> ActivateCharacter(params Type[] enabledModuleTypes)
    {
        string name = $"ChainReload_{Guid.NewGuid():N}";
        character = characterSystem.CreateCharacter(name);
        character.Modules = enabledModuleTypes.Select(type => ModuleSystem.GetModuleID(type)).ToHashSet();

        activity = new ChatActivity(character, configurationSystem, moduleSystem, characterSystem,
            [storageSystem, configurationSystem, characterSystem, moduleSystem]);
        await activity.Awake();
        return character;
    }

    async Task Reload(Character character, params Type[] enabledModuleTypes)
    {
        character.Modules = enabledModuleTypes.Select(type => ModuleSystem.GetModuleID(type)).ToHashSet();
        await characterSystem.SaveCharacter(character);
    }

    [Test]
    public async Task Awake_ConstructsChainAndAwakesDependenciesFirst()
    {
        await SetupEnvironment();
        await ActivateCharacter(typeof(ChainA));

        // 仅启用 ChainA，依赖链 C→B→A 被自动构造；Awake 顺序为依赖先于依赖方
        Assert.That(Lifecycle, Is.EqualTo(new[] { "C:Awake", "B:Awake", "A:Awake" }));
    }

    [Test]
    public async Task Reload_DisableAll_DestroysChainDependentsFirst()
    {
        await SetupEnvironment();
        Character character = await ActivateCharacter(typeof(ChainA));
        Lifecycle.Clear();

        // 重载：禁用全部模块 → 整条依赖链冗余卸载
        await Reload(character);

        Assert.That(Lifecycle, Is.EqualTo(new[] { "A:Destroy", "B:Destroy", "C:Destroy" }));
        // ChatBot 与语言模型保留
        Assert.That(activity!.Container.Instances.Any(instance => instance is ChatBot), Is.True);
        Assert.That(activity.Container.Instances.Any(instance => instance is FakeLanguageModel), Is.True);
        Assert.That(activity.Container.Instances.Any(instance => instance is ChainA), Is.False);
        Assert.That(activity.Container.Instances.Any(instance => instance is ChainB), Is.False);
        Assert.That(activity.Container.Instances.Any(instance => instance is ChainC), Is.False);
    }

    [Test]
    public async Task Reload_KeepsSharedDependency()
    {
        await SetupEnvironment();
        Character character = await ActivateCharacter(typeof(ChainA), typeof(SharedUser));
        Lifecycle.Clear();

        // 重载：仅保留 SharedUser → 共享依赖 ChainC 保留，A/B 卸载
        await Reload(character, typeof(SharedUser));

        Assert.That(Lifecycle, Is.EqualTo(new[] { "A:Destroy", "B:Destroy" }));
        Assert.That(activity!.Container.Instances.Any(instance => instance is SharedUser), Is.True);
        Assert.That(activity.Container.Instances.Any(instance => instance is ChainC), Is.True);
        Assert.That(activity.Container.Instances.Any(instance => instance is ChainA), Is.False);
        Assert.That(activity.Container.Instances.Any(instance => instance is ChainB), Is.False);
    }

    [Test]
    public async Task Reload_ReEnable_RecreatesAndAwakes()
    {
        await SetupEnvironment();
        Character character = await ActivateCharacter(typeof(ChainA));
        Lifecycle.Clear();

        // 禁用全部
        await Reload(character);
        Assert.That(Lifecycle, Is.EqualTo(new[] { "A:Destroy", "B:Destroy", "C:Destroy" }));
        Lifecycle.Clear();

        // 重新启用 ChainA → 重建依赖链并重新 Awake（模拟插件热重载）
        await Reload(character, typeof(ChainA));

        Assert.That(Lifecycle, Is.EqualTo(new[] { "C:Awake", "B:Awake", "A:Awake" }));
        Assert.That(activity!.Container.Instances.Any(instance => instance is ChainA), Is.True);
    }

    async Task SetupEnvironment()
    {
        storageSystem = new StorageSystem();
        configurationSystem = new ConfigurationSystem(storageSystem);
        characterSystem = new CharacterSystem(storageSystem);

        string tempRoot = Path.Combine(Path.GetTempPath(), $"Alife.Test.ChainReload_{Guid.NewGuid():N}");
        PluginContext.PluginContext pluginContext = new(tempRoot, Path.Combine(tempRoot, "CompiledPlugins"),
            new Dictionary<string, IEnvironmentInstaller>(), new CSharpCompiler());
        moduleSystem = new ModuleSystem(pluginContext, storageSystem);
    }
}