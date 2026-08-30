using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alife.Function.DeskPet;

/// <summary>聊天输入上行事件回调（由桌宠实现方注册，转发给 AI）。</summary>
public delegate void InputEventCallback(string text);

/// <summary>交互上行事件回调（由桌宠实现方注册，转发给 AI）。</summary>
public delegate void InteractedEventCallback(string text);

public partial class Live2DDeskPet
{
    public static string ModelRoot { get; } = Path.Combine(AlifePath.StorageFolderPath, "DeskPet", "Live2D", "model");
    public static async Task EnsureDefaultModelsAsync()
    {
        if (Directory.Exists(ModelRoot) && Directory.GetDirectories(ModelRoot).Length > 0)
            return;

        const string ModelsZipUrl =
            "https://github.com/BDFFZI/Alife.OfficialPluginStorage/raw/refs/heads/main/Alife.Function.DeskPet/Live2DModel/1.0.0.zip";
        Directory.CreateDirectory(ModelRoot);
        try
        {
            await AlifeUtility.DownloadZipFileAsync(ModelsZipUrl, ModelRoot);
        }
        catch (Exception ex)
        {
            AlifeLog.LogWarning("下载默认 Live2D 桌宠模型失败\n" + ex);
        }
    }
}

/// <summary>
/// Live2D 桌宠显示模块：作为 <see cref="IDeskPet"/> 的一种实现，
/// 自我管理生命周期（在 Electron 进程内创建透明窗口渲染 Live2D 模型）。
/// 模型为其内部数据，通过 <see cref="Live2DDeskPetConfig"/> 配置。
/// </summary>
[Module("Live2D 桌宠",
    """
    提供一个简易 Live2D 桌宠身体（仅支持 Cubism 3 及以上版本）。
    可选模型下载地址：
    https://github.com/imuncle/live2d
    """,
    defaultCategory: "Alife 官方/模型接入/桌宠模型",
    EditorUI = typeof(Live2DDeskPetUI))]
public partial class Live2DDeskPet(
    PluginSystem pluginSystem,
    ILogger<Live2DDeskPet> logger) :
    ChatBehaviour,
    IConfigurable<Live2DDeskPetConfig>,
    IDeskPet
{
    public Live2DDeskPetConfig Configuration { get; set; } = null!;

    public event Action<string>? OnInput;
    public event Action<string>? OnInteracted;
    public string[] SupportedExpressions => metadata.Expressions.ToArray();
    public string[] SupportedMotions => metadata.Motions.Keys.ToArray();
    public Task ShowUsing(bool isUsing)
    {
        usingModule.Send(isUsing);
        return Task.CompletedTask;
    }
    public Task ShowSubtitle(string? subtitle)
    {
        if (string.IsNullOrEmpty(subtitle)) subtitleModule.Hide();
        else subtitleModule.Show(subtitle);
        return Task.CompletedTask;
    }
    public Task ShowExpression(string? expression)
    {
        expressionModule.PlayExpression(expression);
        return Task.CompletedTask;
    }
    public Task ShowMotion(string? motion)
    {
        if (string.IsNullOrWhiteSpace(motion)) return Task.CompletedTask;
        if (metadata.Motions.TryGetValue(motion, out (string Group, int Index) target) == false) return Task.CompletedTask;
        expressionModule.PlayMotion(target.Group, target.Index);
        return Task.CompletedTask;
    }
    public Task<Vector2> GetPosition()
    {
        (double x, double y) = window.GetCenterPosition();
        return Task.FromResult(new Vector2((float)x, (float)y));
    }
    public Task Move(Vector2 offset, float time)
    {
        return window.ProgrammaticMoveAsync(offset.X, offset.Y, (int)(time * 1000));
    }

    PetModelMetadata metadata = null!;
    ServiceProvider provider = null!;
    PetWindow window = null!;
    SubtitleModule subtitleModule = null!;
    ExpressionModule expressionModule = null!;
    UsingModule usingModule = null!;


    protected override async Task OnAwake()
    {
        // 解析模型元数据（无模型时自动下载默认模型）
        await EnsureDefaultModelsAsync();
        string modelName = Configuration.ModelName;
        if (string.IsNullOrWhiteSpace(modelName))
            modelName = "Mao";
        metadata = PetModelMetadata.Load(Path.Combine(ModelRoot, modelName));

        // 构建依赖注入容器
        provider = BuildProvider();
        try
        {
            //创建窗口
            window = provider.GetRequiredService<PetWindow>();
            string wwwRoot = Path.Combine(pluginSystem.PluginContext.GetPluginDirectoryPath("Alife.Function.DeskPet"), "Resources", "Live2DDeskPet");
            await window.CreateAsync(wwwRoot);

            //加载模型
            await LoadModelAsync();

            //加载模块
            await LoadModuleAsync();

            // subtitleModule = provider.GetRequiredService<SubtitleModule>();
            // expressionModule = provider.GetRequiredService<ExpressionModule>();
            // usingModule = provider.GetRequiredService<UsingModule>();
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }
    protected override async Task OnDestroy()
    {
        await provider.DisposeAsync();
    }

    /// <summary>
    /// 构建桌宠组件依赖注入容器：注册窗口、桥、事件回调与全部 IPetModule。
    /// 模块通过反射从程序集自动发现，新增模块类即自动注册，无需手工维护。
    /// </summary>
    ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddSingleton(logger);
        services.AddSingleton(metadata);
        services.AddSingleton<PetWindow>();
        services.AddSingleton<PetBridge>();
        services.AddSingleton<InputEventCallback>(text => OnInput?.Invoke(text));
        services.AddSingleton<InteractedEventCallback>(text => OnInteracted?.Invoke(text));
        services.AddSingleton<MouseTracker>();
        services.AddSingleton<MotionDetector>();
        services.AddSingleton<WindowInteractionModule>();

        Type[] moduleTypes = typeof(IPetModule).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IPetModule).IsAssignableFrom(type))
            .ToArray();
        foreach (Type moduleType in moduleTypes)
        {
            services.AddSingleton(moduleType);
            services.AddSingleton(typeof(IPetModule), sp => sp.GetRequiredService(moduleType));
        }

        return services.BuildServiceProvider();
    }
    async Task LoadModelAsync()
    {
        PetBridge bridge = provider.GetRequiredService<PetBridge>();

        TaskCompletionSource loadedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(string type, JsonElement _)
        {
            if (type == "loaded")
            {
                bridge.OnMessage -= Handler;
                loadedTcs.TrySetResult();
            }
        }
        bridge.OnMessage += Handler;
        bridge.SendMessage("load", new { url = new Uri(metadata.ModelPath).AbsoluteUri });
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(DestroyCancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await loadedTcs.Task.WaitAsync(timeout.Token);
    }
    async Task LoadModuleAsync()
    {
        IPetModule[] modules = provider.GetServices<IPetModule>().ToArray();
        PetBridge bridge = provider.GetRequiredService<PetBridge>();

        List<IPetModule> cssModules = modules.Where(m => m.CssCode != null).ToList();
        if (cssModules.Count > 0)
        {
            string css = string.Join("\n", cssModules.Select(m => m.CssCode));
            string script = $"injectCSS({JsonSerializer.Serialize(css)})";
            await bridge.ExecuteJavaScriptAsync(script);
        }
        foreach (IPetModule module in modules.Where(m => m.HtmlCode != null))
        {
            string script = $"injectHTML({JsonSerializer.Serialize(module.HtmlCode)})";
            await bridge.ExecuteJavaScriptAsync(script);
        }
        foreach (IPetModule module in modules.Where(m => m.JsCode != null))
        {
            await bridge.ExecuteJavaScriptAsync(module.JsCode!);
        }
    }
}