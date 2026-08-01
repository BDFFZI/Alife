using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Alife.Function.DeskPet;
using Alife.Foundation;

namespace Alife.DeskPet;

public class PetEngine : IAsyncDisposable
{
    public MainWindow MainWindow => window;
    readonly MainWindow window;
    readonly PetBridge bridge;
    readonly PetProcess process;
    readonly PetModelMetadata metadata;
    readonly IServiceProvider services;
    readonly List<IPetModule> modules;
    readonly MotionDetector detector;
    readonly MouseTracker tracker;
    readonly CancellationTokenSource cancellationTokenSource;
    long lastMouseMoveTime;

    PetEngine(MainWindow window, PetBridge bridge, PetProcess process,
        PetModelMetadata metadata, IServiceProvider services,
        List<IPetModule> modules, CancellationTokenSource cancellationTokenSource)
    {
        this.window = window;
        this.bridge = bridge;
        this.process = process;
        this.metadata = metadata;
        this.services = services;
        this.modules = modules;
        this.cancellationTokenSource = cancellationTokenSource;
        detector = new MotionDetector();
        tracker = new MouseTracker();
    }

    public static async Task<PetEngine> Create(string[] args)
    {
        string modelRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot/model");
        string defaultModel = PetModelMetadata.ResolveModelJsonPath(modelRoot, "Mao");
        string modelPath = args.Length > 1 ? args[1] : defaultModel;

        PetModelMetadata metadata = PetModelMetadata.Load(modelPath);
        MainWindow window = await MainWindow.Create();

        // 等待页面加载完成
        await WaitForNavigationAsync(window.WebView);

        PetBridge bridge = new(window.WebView);
        PetProcess process = new(Console.Out, Console.In);
        CancellationTokenSource cancellationTokenSource = new();

        // 前端日志转发
        bridge.OnMessage += (type, data) =>
        {
            if (type != "log") return;
            string level = data.TryGetProperty("level", out JsonElement l) ? l.GetString() ?? "log" : "log";
            string text = data.TryGetProperty("text", out JsonElement t) ? t.GetString() ?? "" : "";
            _ = File.AppendAllTextAsync("pet.log", $"[WebView {level}] {text}{Environment.NewLine}", cancellationTokenSource.Token);
        };

        // 构建 DI 容器
        ServiceCollection services = new();
        services.AddSingleton(bridge);
        services.AddSingleton(process);
        services.AddSingleton(window);
        services.AddSingleton(metadata);

        // 注册模块
        services.AddSingleton<BubbleModule>();
        services.AddSingleton<ExpressionModule>();
        services.AddSingleton<GazeModule>();
        services.AddSingleton<StatusModule>();
        services.AddSingleton<DragModule>();
        services.AddSingleton<ResizeModule>();
        services.AddSingleton<PokeModule>();
        services.AddSingleton<InputModule>();

        // 注册 IPetModule 接口
        services.AddSingleton<IPetModule>(sp => sp.GetRequiredService<BubbleModule>());
        services.AddSingleton<IPetModule>(sp => sp.GetRequiredService<ExpressionModule>());
        services.AddSingleton<IPetModule>(sp => sp.GetRequiredService<GazeModule>());
        services.AddSingleton<IPetModule>(sp => sp.GetRequiredService<StatusModule>());
        services.AddSingleton<IPetModule>(sp => sp.GetRequiredService<DragModule>());
        services.AddSingleton<IPetModule>(sp => sp.GetRequiredService<ResizeModule>());
        services.AddSingleton<IPetModule>(sp => sp.GetRequiredService<PokeModule>());
        services.AddSingleton<IPetModule>(sp => sp.GetRequiredService<InputModule>());

        IServiceProvider provider = services.BuildServiceProvider();
        List<IPetModule> modules = provider.GetRequiredService<IEnumerable<IPetModule>>().ToList();

        // 注入前端资源（CSS + HTML + JS）
        await InjectModuleResourcesAsync(window.WebView, modules);

        // 等待 JS 就绪
        await WaitForReadyAsync(bridge);

        // 加载 Live2D 模型
        await bridge.LoadModel(metadata.ModelPath);

        // 实例化引擎
        PetEngine engine = new(window, bridge, process, metadata, provider, modules, cancellationTokenSource);

        // 启动交互（模块在构造函数中已自动初始化）
        engine.HandleInteraction("startup");

        // 启动 IPC 监听
        process.InputReceived += cmd => OnCommandReceived(cmd, window, process, modules);
        process.ListenInput();

        // 启动鼠标跟踪 + 视线自动回中
        engine.StartMouseTracking();
        _ = engine.FocusResetLoop(cancellationTokenSource.Token);

        // 通知服务器就绪
        process.SendOutput(new ReadyEvent());

        return engine;
    }

    void StartMouseTracking()
    {
        DragModule? drag = services.GetService<DragModule>();
        GazeModule? gaze = services.GetService<GazeModule>();

        tracker.MouseMoved += (x, y) =>
        {
            lastMouseMoveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            (double ScaleX, double ScaleY) dpi = window.GetDpi();
            double windowMouseX = x / dpi.ScaleX;
            double windowMouseY = y / dpi.ScaleY;
            (double Left, double Top, double Width, double Height) layout = window.GetLayout();
            double centerX = layout.Left + layout.Width / 2;
            double centerY = layout.Top + layout.Height / 2;

            detector.Update(windowMouseX, windowMouseY, centerX, centerY, layout.Left, layout.Top);

            if (gaze != null)
            {
                double clientX = windowMouseX - layout.Left;
                double clientY = windowMouseY - layout.Top;
                gaze.SetFocus(clientX, clientY);
            }

            drag?.OnMouseMove(windowMouseX, windowMouseY);
        };
        tracker.Start();

        detector.WindowShaken += () => HandleInteraction("window_shake");
        detector.WindowMoved += () => HandleInteraction("window_move");
        detector.MouseShaken += () => HandleInteraction("mouse_shake");
    }

    async Task FocusResetLoop(CancellationToken ct)
    {
        try
        {
            GazeModule? gaze = services.GetService<GazeModule>();
            while (true)
            {
                await Task.Delay(500, ct);
                if (gaze != null && Now() - lastMouseMoveTime > 3000)
                {
                    (double Left, double Top, double Width, double Height) layout = window.GetLayout();
                    gaze.SetFocus(layout.Width / 2, layout.Height / 2);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    static long Now() => DateTimeOffset.Now.ToUnixTimeMilliseconds();

    public async ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();
        foreach (IPetModule module in ((IEnumerable<IPetModule>)modules).Reverse())
            module.Dispose();
        process.Dispose();
        bridge.Dispose();
    }

    static async Task WaitForNavigationAsync(WebView2 webView)
    {
        TaskCompletionSource navigationTcs = new();
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs? e)
        {
            navigationTcs.TrySetResult();
            webView.CoreWebView2.NavigationCompleted -= Handler;
        }
        webView.CoreWebView2.NavigationCompleted += Handler;
        await navigationTcs.Task;
    }

    static async Task WaitForReadyAsync(PetBridge bridge)
    {
        TaskCompletionSource readyTcs = new();
        void Handler(string type, JsonElement _)
        {
            if (type == "ready")
            {
                bridge.OnMessage -= Handler;
                readyTcs.SetResult();
            }
        }
        bridge.OnMessage += Handler;
        bridge.SendMessage("_init");
        await readyTcs.Task;
    }

    static async Task InjectModuleResourcesAsync(WebView2 webView, List<IPetModule> modules)
    {
        // CSS
        List<IPetModule> cssModules = modules.Where(m => m.CssCode != null).ToList();
        if (cssModules.Count > 0)
        {
            string css = string.Join("\n", cssModules.Select(m => m.CssCode));
            string script = $"injectCSS({JsonSerializer.Serialize(css)})";
            await webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        // HTML
        foreach (IPetModule module in modules.Where(m => m.HtmlCode != null))
        {
            string script = $"injectHTML({JsonSerializer.Serialize(module.HtmlCode)})";
            await webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        // JS
        foreach (IPetModule module in modules.Where(m => m.JsCode != null))
        {
            await webView.CoreWebView2.ExecuteScriptAsync(module.JsCode);
        }
    }

    static void OnCommandReceived(IpcCommand cmd, MainWindow window, PetProcess process,
        List<IPetModule> modules)
    {
        // 内置命令
        if (cmd is WindowMoveCommand move)
        {
            window.ProgrammaticMove(move.X, move.Y, move.Duration);
            return;
        }
        if (cmd is GetPositionCommand)
        {
            (double X, double Y) center = window.GetCenterPosition();
            process.SendOutput(new PositionEvent(center.X, center.Y));
            return;
        }

        // 转发给模块
        foreach (IPetModule module in modules)
        {
            if (module.HandleIpc(cmd)) return;
        }
    }

    void HandleInteraction(string type)
    {
        process.SendOutput(new InteractionEvent($"桌宠被{type switch
        {
            "startup" => "启动",
            "window_shake" => "大幅晃动",
            "window_move" => "快速移动",
            "mouse_shake" => "鼠标快速转圈",
            _ => type
        }}"));

        if (!metadata.Interactions.TryGetValue(type, out List<InteractionItem>? pool) || pool == null || pool.Count == 0) return;
        InteractionItem item = pool[Random.Shared.Next(pool.Count)];
        if (!string.IsNullOrEmpty(item.Text))
            services.GetService<BubbleModule>()?.Show(item.Text);
        if (!string.IsNullOrEmpty(item.Exp))
            services.GetService<ExpressionModule>()?.Play(item.Exp);
        if (item.Mtn != null)
            services.GetService<ExpressionModule>()?.PlayMotion(item.Mtn.Group, item.Mtn.Index);
    }
}
