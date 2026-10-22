using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Client;
using Alife.Framework;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 陪玩配置编辑器控制器。
/// 通过 <see cref="IAlifeApplication"/> 创建编辑器主窗口、全屏透明覆盖层窗口与数据浮窗，
/// 并使用 <see cref="IAlifeWindow"/> 的通用消息通道与渲染进程通信（不再依赖 Electron）。
/// 编辑器 UI 位于插件目录 Resources/ 下。
/// </summary>
public sealed class GameCompanionEditorController : IDisposable
{
    const string PluginId = "BDFFZI.VibeCode.GameCompanion";

    enum WindowKind
    {
        Editor,
        Float,
        Overlay,
        ViewOverlay,
        TestScene
    }

    readonly ILogger<GameCompanionModule> logger;
    readonly IAlifeApplication alifeApplication;
    readonly string editorDirectory;
    readonly string configPath;
    /// <summary>浮窗当前选中的游戏名（打开编辑器时由此传入，持久化到文件）。</summary>
    public string LastGameName { get; set; } = "";

    readonly Func<string> readConfigJson;
    readonly Action<string> writeConfigJson;

    /// <summary>暂停/继续请求回调（由模块注册，切换陪玩监控的暂停状态）。</summary>
    public Action? TogglePaused;

    /// <summary>获取所有已配置游戏（由模块注册）。</summary>
    public Func<List<GameConfig>>? GetGamesList;

    /// <summary>启动指定游戏陪玩（由模块注册）。</summary>
    public Action<string>? StartGame;

    /// <summary>停止当前陪玩（由模块注册）。</summary>
    public Action? StopGame;

    /// <summary>开关指定游戏的某个采样项（由模块注册，持久化到配置）。</summary>
    public Action<string, string, bool>? ToggleCollector;

    IAlifeWindow? editorWindow;
    IAlifeWindow? overlayWindow;
    IAlifeWindow? testSceneWindow;
    IAlifeWindow? viewOverlayWindow; // 只读区域显示覆盖层（不挡鼠标）
    IAlifeWindow? floatWindow;

    string overlayItemsJson = "[]";
    bool overlayPickMode;
    bool overlayEditing;
    // 浮窗为每个角色独立（多角色各自一个浮窗，标题区分角色名）
    string floatDataJson = "{\"State\":0,\"GameName\":\"\",\"Values\":{}}";
    bool floatCollapsed = true; // 初始收起（小球）
    string characterName = "";
    Rectangle floatBounds = new(30, 30, FloatBallSize, FloatBallSize);
    Rectangle testSceneBounds = new(0, 0, 640, 400);
    const int FloatExpandedW = 420;
    const int FloatExpandedH = 440;
    const int FloatBallSize = 44;

    public GameCompanionEditorController(
        ILogger<GameCompanionModule> logger,
        IAlifeApplication alifeApplication,
        PluginSystem pluginSystem,
        string configPath,
        string characterName,
        Func<string> readConfigJson,
        Action<string> writeConfigJson)
    {
        this.logger = logger;
        this.alifeApplication = alifeApplication;
        this.configPath = configPath;
        this.characterName = characterName;
        this.readConfigJson = readConfigJson;
        this.writeConfigJson = writeConfigJson;
        editorDirectory = ResolveEditorDirectory(pluginSystem);
    }

    /// <summary>
    /// 解析编辑器网页资源目录（Resources）：
    /// 插件部署模式 → {插件目录}/Resources（随插件分发）；
    /// 内嵌模式（本项目直接编译进客户端）→ 客户端输出目录 Resources（csproj Content 复制）。
    /// </summary>
    static string ResolveEditorDirectory(PluginSystem pluginSystem)
    {
        string pluginResources = Path.Combine(pluginSystem.PluginContext.GetPluginDirectoryPath(PluginId), "Resources");
        if (Directory.Exists(pluginResources))
            return pluginResources;

        string embeddedResources = Path.Combine(AppContext.BaseDirectory, "Resources");
        if (Directory.Exists(embeddedResources))
            return embeddedResources;

        return pluginResources;
    }

    // ============ 窗口创建与消息通道 ============

    async Task<IAlifeWindow> CreateWindowAsync(
        WindowKind kind,
        string title,
        string htmlFile,
        Rectangle bounds,
        bool transparent,
        bool resizable,
        bool topMost,
        string? query = null)
    {
        string url = new Uri(Path.Combine(editorDirectory, htmlFile)).AbsoluteUri;
        if (!string.IsNullOrEmpty(query))
            url += query;

        IAlifeWindow window = await alifeApplication.CreateWindowAsync(new AlifeWindowOptions {
            Title = title,
            Url = url,
            Bounds = bounds,
            Transparent = transparent,
            Resizable = resizable,
            TopMost = topMost
        });
        window.MessageReceived += payload => OnWindowMessage(kind, window, payload);
        window.BoundsChanged += actual => UpdateTrackedBounds(kind, actual);
        await window.Launch();
        return window;
    }

    void UpdateTrackedBounds(WindowKind kind, Rectangle bounds)
    {
        switch (kind)
        {
            case WindowKind.Float:
                floatBounds = bounds;
                break;
            case WindowKind.TestScene:
                testSceneBounds = bounds;
                break;
        }
    }

    void OnWindowMessage(WindowKind kind, IAlifeWindow window, string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        JObject message;
        try
        {
            message = JObject.Parse(payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "解析陪玩窗口消息失败");
            return;
        }

        string? messageKind = message["kind"]?.ToString();
        switch (messageKind)
        {
            case "ready":
                _ = OnWindowReadyAsync(kind, window);
                break;
            case "request":
            {
                long id = message["id"]?.Value<long>() ?? 0;
                string? channel = message["channel"]?.ToString();
                JToken? body = message["payload"];
                _ = HandleRequestAsync(kind, window, id, channel, body);
                break;
            }
            case "event":
            {
                string? channel = message["channel"]?.ToString();
                if (!string.IsNullOrEmpty(channel))
                    HandlePush(channel, message["payload"]);
                break;
            }
        }
    }

    async Task OnWindowReadyAsync(WindowKind kind, IAlifeWindow window)
    {
        try
        {
            switch (kind)
            {
                case WindowKind.Editor:
                    await SendEventAsync(window, "companion:config-path", configPath);
                    break;
                case WindowKind.Float:
                    await SendEventAsync(window, "companion:float-character", characterName);
                    await SendEventAsync(window, "companion:float-data", JToken.Parse(floatDataJson));
                    await SendEventAsync(window, "companion:float-reset-collapsed", null);
                    break;
                case WindowKind.Overlay:
                    await SendEventAsync(window, "companion:overlay-items", JToken.Parse(overlayItemsJson));
                    if (overlayPickMode)
                        await SendEventAsync(window, "companion:pick-mode", true);
                    break;
                case WindowKind.ViewOverlay:
                    await SendEventAsync(window, "companion:overlay-items", JToken.Parse(BuildOverlayViewItems()));
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "陪玩窗口初始化消息发送失败");
        }
    }

    async Task HandleRequestAsync(WindowKind kind, IAlifeWindow window, long id, string? channel, JToken? payload)
    {
        object? result = null;
        string? error = null;
        try
        {
            if (channel == "companion:close-window")
                result = await CloseWindowAsync(kind, window);
            else
                result = await DispatchAsync(channel, payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理陪玩窗口请求失败: {Channel}", channel);
            error = ex.Message;
        }

        // id == 0 表示前端只发送、不等待响应
        if (id == 0)
            return;

        try
        {
            string json = JsonConvert.SerializeObject(new { kind = "response", id, result, error }, Formatting.None);
            await window.PostMessageAsync(json);
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送陪玩窗口响应失败");
        }
    }

    Task<object?> DispatchAsync(string? channel, JToken? payload) => channel switch
    {
        "companion:get-config" => Task.FromResult<object?>(JToken.Parse(SafeReadConfig())),
        "companion:get-screen-size" => GetScreenSizeAsync(),
        "companion:collector-types" => CollectorTypesAsync(),
        "companion:save-config" => Task.FromResult<object?>(SaveConfig(payload)),
        "companion:save-config-sync" => Task.FromResult<object?>(SaveConfig(payload)),
        "companion:show-overlay" => ShowOverlayFromRequestAsync(payload),
        "companion:hide-overlay" => HideOverlayFromRequestAsync(),
        "companion:region-changed" => Task.FromResult<object?>(ForwardRegionChange(payload)),
        "companion:pick-color" => PickColorAsync(),
        "companion:pick-at" => PickAtAsync(payload),
        "companion:pick-cancel" => PickCancelAsync(),
        "companion:pick-result" => PickResultAsync(payload),
        "companion:open-config-folder" => Task.FromResult<object?>(OpenConfigFolder()),
        "companion:open-test-scene" => OpenTestSceneAsync(),
        "companion:get-test-scene-bounds" => Task.FromResult<object?>(GetTestSceneBounds()),
        "companion:float-collapse" => Task.FromResult<object?>(FloatCollapse()),
        "companion:float-expand" => Task.FromResult<object?>(FloatExpand()),
        "companion:float-edit" => Task.FromResult<object?>(FloatEdit(payload)),
        "companion:float-move" => Task.FromResult<object?>(FloatMove(payload)),
        "companion:float-set-pos" => Task.FromResult<object?>(FloatMoveTo(payload)),
        "companion:float-get-pos" => Task.FromResult<object?>(FloatGetPos()),
        "companion:float-pause" => Task.FromResult<object?>(FloatPauseToggle()),
        "companion:float-games" => Task.FromResult<object?>(FloatGames()),
        "companion:float-start" => Task.FromResult<object?>(FloatStart(payload)),
        "companion:float-stop" => Task.FromResult<object?>(FloatStop()),
        "companion:float-toggle" => Task.FromResult<object?>(FloatToggle(payload)),
        "companion:float-overlay-view" => FloatOverlayViewAsync(payload),
        "companion:overlay-debug" => Task.FromResult<object?>(LogDebug(payload)),
        "companion:debug" => Task.FromResult<object?>(LogDebug(payload)),
        _ => Task.FromResult<object?>(false)
    };

    void HandlePush(string channel, JToken? payload)
    {
        switch (channel)
        {
            case "companion:debug":
            case "companion:overlay-debug":
                logger.LogInformation("{Channel}: {Message}", channel, payload?.ToString());
                break;
        }
    }

    bool LogDebug(JToken? payload)
    {
        logger.LogInformation("[陪玩] {Message}", payload?.Type == JTokenType.String ? payload.Value<string>() : payload?.ToString(Formatting.None));
        return true;
    }

    async Task SendEventAsync(IAlifeWindow? window, string channel, object? payload)
    {
        if (window == null)
            return;
        try
        {
            string json = JsonConvert.SerializeObject(new { kind = "event", channel, payload }, Formatting.None);
            await window.PostMessageAsync(json);
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送陪玩窗口事件失败: {Channel}", channel);
        }
    }

    // ============ 编辑器窗口 ============

    /// <summary>打开编辑器主窗口。已打开时聚焦。</summary>
    public async Task OpenAsync()
    {
        if (editorWindow != null)
        {
            try
            {
                await editorWindow.ShowAsync();
                return;
            }
            catch (ObjectDisposedException)
            {
                editorWindow = null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "复用陪玩配置编辑器窗口失败，将重新创建");
                editorWindow = null;
            }
        }
        if (!Directory.Exists(editorDirectory))
        {
            logger.LogError("编辑器目录不存在: {Dir}", editorDirectory);
            return;
        }

        try
        {
            (int screenWidth, int screenHeight) = GetLogicalScreenSize();
            int editorHeight = Math.Min(760, Math.Max(560, screenHeight - 40));
            const int editorWidth = 1080;
            Rectangle bounds = new(
                Math.Max(0, (screenWidth - editorWidth) / 2),
                Math.Max(0, (screenHeight - editorHeight) / 2),
                editorWidth,
                editorHeight);

            editorWindow = await CreateWindowAsync(
                WindowKind.Editor,
                "陪玩配置编辑器",
                "editor.html",
                bounds,
                transparent: false,
                resizable: true,
                topMost: true);
            logger.LogInformation("已打开陪玩配置编辑器");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开陪玩配置编辑器失败");
        }
    }

    (int Width, int Height) GetLogicalScreenSize()
    {
        float dpi = alifeApplication.GetDpiScale();
        if (dpi <= 0)
            dpi = 1;
        Size physical = alifeApplication.GetScreenSize();
        return ((int)(physical.Width / dpi), (int)(physical.Height / dpi));
    }

    // ============ 数据浮窗 ============

    /// <summary>显示数据浮窗（陪玩启动时调用）。已创建时直接显示。</summary>
    public async Task ShowFloatAsync()
    {
        if (floatWindow != null)
        {
            try
            {
                await floatWindow.ShowAsync();
                return;
            }
            catch (ObjectDisposedException)
            {
                floatWindow = null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "复用陪玩数据浮窗失败，将重新创建");
                floatWindow = null;
            }
        }

        try
        {
            // 初始即为收起的小球尺寸，避免首次展开尺寸的透明区挡鼠标
            floatBounds = new Rectangle(30, 30, FloatBallSize, FloatBallSize);
            floatCollapsed = true;
            floatWindow = await CreateWindowAsync(
                WindowKind.Float,
                "陪玩数据",
                "float.html",
                floatBounds,
                transparent: true,
                resizable: false,
                topMost: true);
            logger.LogInformation("已打开陪玩数据浮窗");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开陪玩数据浮窗失败");
        }
    }

    /// <summary>隐藏数据浮窗（陪玩停止时调用）。</summary>
    public Task HideFloatAsync()
    {
        IAlifeWindow? window = floatWindow;
        floatWindow = null;
        if (window != null)
            _ = DisposeWindowAsync(window);
        return Task.CompletedTask;
    }

    /// <summary>推送实时数据到浮窗。</summary>
    public void PushFloatData(object snapshot)
    {
        try
        {
            floatDataJson = JsonConvert.SerializeObject(snapshot);
            if (floatWindow == null)
                return;
            _ = SendEventAsync(floatWindow, "companion:float-data", JToken.Parse(floatDataJson));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "推送陪玩数据到浮窗失败");
        }
    }

    object FloatExpand()
    {
        SwitchFloatState(false);
        return true;
    }

    object FloatCollapse()
    {
        SwitchFloatState(true);
        return true;
    }

    object FloatEdit(JToken? payload)
    {
        string? gameName = PayloadString(payload);
        if (!string.IsNullOrEmpty(gameName))
            LastGameName = gameName;
        _ = OpenAsync();
        return true;
    }

    object FloatMove(JToken? payload)
    {
        try
        {
            if (floatWindow == null)
                return true;
            int dx = payload?["dx"]?.Value<int>() ?? 0;
            int dy = payload?["dy"]?.Value<int>() ?? 0;
            floatBounds.X += dx;
            floatBounds.Y += dy;
            _ = floatWindow.SetBoundsAsync(floatBounds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "移动陪玩数据浮窗失败");
        }
        return true;
    }

    object FloatPauseToggle()
    {
        try
        {
            TogglePaused?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "切换陪玩暂停状态失败");
        }
        return true;
    }

    /// <summary>浮窗内快速开关采样项（持久化到配置，下一周期监控自动生效）。</summary>
    object FloatToggle(JToken? payload)
    {
        try
        {
            string game = payload?["game"]?.ToString() ?? "";
            string name = payload?["name"]?.ToString() ?? "";
            bool enable = payload?["enable"]?.Value<bool>() ?? true;
            if (!string.IsNullOrEmpty(game) && !string.IsNullOrEmpty(name))
                ToggleCollector?.Invoke(game, name, enable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "切换采样项启用状态失败");
        }
        return true;
    }

    /// <summary>返回游戏列表给浮窗选择（含各游戏采样项明细，供未运行时可展示/开关）。</summary>
    object FloatGames()
    {
        try
        {
            if (GetGamesList == null)
                return new JArray();
            var games = GetGamesList().Select(g => new {
                name = g.GameName,
                targets = g.Collectors.Count,
                collectors = g.Collectors.Select(c => new {
                    name = c.Name,
                    sampler = CollectorRegistry.TypeName(c),
                    enabled = c.IsEnable,
                    validator = c.IsValidator
                })
            });
            return JToken.FromObject(games);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取游戏列表失败");
            return new JArray();
        }
    }

    /// <summary>启动指定游戏陪玩。</summary>
    object FloatStart(JToken? payload)
    {
        try
        {
            string? name = PayloadString(payload);
            if (!string.IsNullOrWhiteSpace(name))
                StartGame?.Invoke(name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "浮窗启动陪玩失败");
        }
        return true;
    }

    /// <summary>停止当前陪玩。</summary>
    object FloatStop()
    {
        try
        {
            StopGame?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "浮窗停止陪玩失败");
        }
        return true;
    }

    /// <summary>浮窗当前窗口位置（供渲染进程作拖拽基准，逻辑像素）。</summary>
    object FloatGetPos() => new { x = floatBounds.X, y = floatBounds.Y };

    /// <summary>浮窗绝对定位（JS 拖动时按屏幕坐标直接设置窗口位置，避免累加抖动）。</summary>
    object FloatMoveTo(JToken? payload)
    {
        try
        {
            if (floatWindow == null)
                return true;
            int x = payload?["x"]?.Value<int>() ?? 0;
            int y = payload?["y"]?.Value<int>() ?? 0;
            floatBounds.X = x;
            floatBounds.Y = y;
            _ = floatWindow.SetBoundsAsync(floatBounds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "定位陪玩数据浮窗失败");
        }
        return true;
    }

    void SwitchFloatState(bool collapsed)
    {
        floatCollapsed = collapsed;
        _ = ResizeFloatAsync();
    }

    async Task ResizeFloatAsync()
    {
        if (floatWindow == null)
            return;

        try
        {
            // 始终保持左上角锚点不变：收起为小球（左上角即小球位置），
            // 展开为面板，面板标题也在左上角 → 球与标题位置重合，点击一处即可收展
            floatBounds.Width = floatCollapsed ? FloatBallSize : FloatExpandedW;
            floatBounds.Height = floatCollapsed ? FloatBallSize : FloatExpandedH;
            await floatWindow.SetBoundsAsync(floatBounds);
            logger.LogInformation("浮窗{State}后 bounds=({X},{Y},{W},{H})",
                floatCollapsed ? "收起" : "展开",
                floatBounds.X, floatBounds.Y, floatBounds.Width, floatBounds.Height);
        }
        catch (ObjectDisposedException)
        {
            floatWindow = null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "调整陪玩数据浮窗大小失败");
        }
    }

    // ============ 系统任务栏控制 ============

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    const int SW_HIDE = 0;
    const int SW_SHOW = 5;

    /// <summary>隐藏/显示系统任务栏（进入覆盖层编辑时隐藏，避免隐形任务栏热区遮挡覆盖层底部）。</summary>
    static void SetTaskbarVisible(bool visible)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            IntPtr hWnd = FindWindow("Shell_TrayWnd", null);
            if (hWnd != IntPtr.Zero)
                ShowWindow(hWnd, visible ? SW_SHOW : SW_HIDE);
        }
        catch
        {
            // 任务栏控制失败时忽略，不影响核心功能
        }
    }

    // ============ 覆盖层 ============

    async Task<object?> ShowOverlayFromRequestAsync(JToken? payload)
    {
        await ShowOverlayAsync(payload);
        return true;
    }

    internal async Task ShowOverlayAsync(JToken? payload)
    {
        overlayItemsJson = payload?.Type == JTokenType.String
            ? payload.Value<string>() ?? "[]"
            : payload?.ToString(Formatting.None) ?? "[]";

        overlayPickMode = false;
        // 编辑区域时隐藏系统任务栏：覆盖层需满屏且不被隐形任务栏热区遮挡
        SetTaskbarVisible(false);
        overlayEditing = true;

        // 编辑区域时隐藏主窗口，避免遮挡屏幕
        if (editorWindow != null)
        {
            try { await editorWindow.HideAsync(); }
            catch (ObjectDisposedException) { editorWindow = null; }
        }

        if (overlayWindow != null)
        {
            try
            {
                await SendEventAsync(overlayWindow, "companion:overlay-items", JToken.Parse(overlayItemsJson));
                await overlayWindow.ShowAsync();
                return;
            }
            catch (ObjectDisposedException)
            {
                overlayWindow = null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "复用陪玩覆盖层失败，将重新创建");
                overlayWindow = null;
            }
        }

        try
        {
            (int width, int height) = GetLogicalScreenSize();
            overlayWindow = await CreateWindowAsync(
                WindowKind.Overlay,
                "陪玩区域编辑器",
                "overlay.html",
                new Rectangle(0, 0, width, height),
                transparent: true,
                resizable: false,
                topMost: true,
                query: "?v=" + DateTime.Now.Ticks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开覆盖层窗口失败");
        }
    }

    Task<object?> HideOverlayFromRequestAsync()
    {
        HideOverlay();
        return Task.FromResult<object?>(true);
    }

    internal void HideOverlay()
    {
        overlayPickMode = false;
        overlayEditing = false;
        IAlifeWindow? window = overlayWindow;
        overlayWindow = null;
        if (window != null)
            _ = DisposeWindowAsync(window);
        // 覆盖层关闭后恢复系统任务栏
        SetTaskbarVisible(true);
        // 覆盖层关闭后恢复显示编辑主窗口
        if (editorWindow != null)
        {
            try { _ = editorWindow.ShowAsync(); }
            catch (ObjectDisposedException) { editorWindow = null; }
        }
    }

    /// <summary>采样前隐藏「只读预览覆盖层」（悬浮窗勾选），编辑拖拽覆盖层保持长显不隐藏。返回是否曾隐藏。</summary>
    public bool HideOverlayForSample()
    {
        if (viewOverlayWindow == null)
            return false;
        try
        {
            _ = viewOverlayWindow.ExecuteScriptAsync("document.body.style.opacity='0';");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>采样后恢复只读预览覆盖层可见。</summary>
    public void RestoreOverlayForSample()
    {
        if (viewOverlayWindow == null)
            return;
        try { _ = viewOverlayWindow.ExecuteScriptAsync("document.body.style.opacity='1';"); }
        catch { }
    }

    /// <summary>是否处于编辑拖拽覆盖层状态（该覆盖层打开可见）。此时采样跳过隐藏只读预览，避免闪烁。</summary>
    public bool IsOverlayEditing() => overlayEditing;

    // ============ 只读区域显示覆盖层（浮窗勾选，不挡鼠标） ============

    /// <summary>浮窗「显示覆盖层」开关：payload 为 {show, game}。</summary>
    internal async Task<object?> FloatOverlayViewAsync(JToken? payload)
    {
        try
        {
            bool show;
            string? game;
            if (payload?.Type == JTokenType.String)
            {
                show = payload.Value<string>() == "true";
                game = null;
            }
            else
            {
                show = payload?["show"]?.Value<bool>() ?? false;
                game = payload?["game"]?.ToString();
            }
            if (!string.IsNullOrEmpty(game))
                LastGameName = game;
            if (show)
                await ShowOverlayViewAsync();
            else
                HideOverlayView();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "切换只读覆盖层失败");
        }
        return true;
    }

    /// <summary>根据当前选中游戏构建覆盖层区域项（供只读显示）。</summary>
    string BuildOverlayViewItems()
    {
        var items = new List<object>();
        if (GetGamesList == null)
            return "[]";
        GameConfig? game = GetGamesList().FirstOrDefault(g => string.Equals(g.GameName, LastGameName, StringComparison.OrdinalIgnoreCase))
            ?? GetGamesList().FirstOrDefault();
        if (game == null)
            return "[]";

        int i = 0;
        foreach (CollectConfigBase c in game.Collectors)
        {
            i++;
            if (!c.IsEnable)
                continue;
            // 读取采样器配置中的区域字段（多数用 Region）
            ScreenRegion? region = ReadRegion(c);
            if (region == null)
                continue;
            string kind = region.IsTriangle ? "triangle" : (region.IsSector ? "sector" : (region.IsPoint ? "point" : "rect"));
            string color = kind switch
            {
                "point" => "#52c41a",
                "triangle" => "#fa8c16",
                "sector" => "#722ed1",
                _ => "#1677ff"
            };
            items.Add(new
            {
                id = $"target:{i}:Region",
                label = c.Name,
                kind,
                color,
                region = (kind == "triangle" || kind == "sector") ? null : new { X = region.X, Y = region.Y, Width = region.Width, Height = region.Height },
                triangle = kind == "triangle" ? region.Triangle : null,
                sector = kind == "sector" ? new { X = region.X, Y = region.Y, Radius = region.Radius, StartAngle = region.StartAngle, SweepAngle = region.SweepAngle } : null
            });
        }
        return JsonConvert.SerializeObject(items);
    }

    /// <summary>从采样器配置读取区域字段。</summary>
    static ScreenRegion? ReadRegion(CollectConfigBase config)
    {
        foreach (var prop in config.GetType().GetProperties())
        {
            if (prop.PropertyType == typeof(ScreenRegion) && prop.GetValue(config) is ScreenRegion r)
                return r;
        }
        return null;
    }

    /// <summary>显示只读覆盖层：全屏、透明、不挡鼠标。</summary>
    async Task ShowOverlayViewAsync()
    {
        if (viewOverlayWindow != null)
        {
            try
            {
                // 已存在则刷新区域并显示
                await SendEventAsync(viewOverlayWindow, "companion:overlay-items", JToken.Parse(BuildOverlayViewItems()));
                await viewOverlayWindow.ShowAsync();
                return;
            }
            catch (ObjectDisposedException)
            {
                viewOverlayWindow = null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "复用只读覆盖层失败，将重新创建");
                viewOverlayWindow = null;
            }
        }
        try
        {
            (int width, int height) = GetLogicalScreenSize();
            // 复用编辑覆盖层页面，?view=1 进入只读模式
            viewOverlayWindow = await CreateWindowAsync(
                WindowKind.ViewOverlay,
                "陪玩区域预览",
                "overlay.html",
                new Rectangle(0, 0, width, height),
                transparent: true,
                resizable: false,
                topMost: true,
                query: "?view=1&v=" + DateTime.Now.Ticks);
            // 不挡鼠标：空输入区域表示整窗穿透
            await viewOverlayWindow.SetInputRegionsAsync(Array.Empty<Rectangle>());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开只读覆盖层失败");
        }
    }

    /// <summary>隐藏只读覆盖层。</summary>
    void HideOverlayView()
    {
        IAlifeWindow? window = viewOverlayWindow;
        viewOverlayWindow = null;
        if (window != null)
            _ = DisposeWindowAsync(window);
    }

    // ============ 屏幕取色 ============

    /// <summary>进入屏幕取色模式：隐藏编辑窗口，显示全屏覆盖层供点击取色。</summary>
    internal async Task<object?> PickColorAsync()
    {
        try
        {
            SetTaskbarVisible(false);
            overlayPickMode = true;
            overlayEditing = true;

            if (overlayWindow != null)
            {
                try
                {
                    await SendEventAsync(overlayWindow, "companion:pick-mode", true);
                    await overlayWindow.ShowAsync();
                }
                catch (ObjectDisposedException)
                {
                    overlayWindow = null;
                }
            }

            if (overlayWindow == null)
            {
                (int width, int height) = GetLogicalScreenSize();
                overlayWindow = await CreateWindowAsync(
                    WindowKind.Overlay,
                    "陪玩区域编辑器",
                    "overlay.html",
                    new Rectangle(0, 0, width, height),
                    transparent: true,
                    resizable: false,
                    topMost: true,
                    query: "?v=" + DateTime.Now.Ticks);
            }

            // 取色期间隐藏编辑主窗口，避免遮挡
            if (editorWindow != null)
            {
                try { await editorWindow.HideAsync(); }
                catch (ObjectDisposedException) { editorWindow = null; }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "进入取色模式失败");
        }
        return true;
    }

    /// <summary>取色覆盖层点击：截图取该点颜色，返回十六进制色值。</summary>
    internal async Task<object?> PickAtAsync(JToken? payload)
    {
        try
        {
            int x = payload?["x"]?.Value<int>() ?? 0;
            int y = payload?["y"]?.Value<int>() ?? 0;
            // 截图前隐藏覆盖层窗口，避免把半透明覆盖层自身截入画面导致取到灰色
            if (overlayWindow != null)
            {
                try { await overlayWindow.HideAsync(); }
                catch (ObjectDisposedException) { overlayWindow = null; }
                await Task.Delay(50);
            }
            // 物理像素坐标截图取色（覆盖层以物理像素工作）
            using ScreenFrame? frame = await ScreenFrame.CaptureFullscreenAsync();
            if (frame == null)
                return "#000000";
            var region = new ScreenRegion { X = x, Y = y, Width = 1, Height = 1 };
            System.Drawing.Color? c = frame.GetPixel(region);
            if (c is not System.Drawing.Color color)
                return "#000000";
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "屏幕取色失败");
            return "#000000";
        }
    }

    /// <summary>取消取色：关闭覆盖层并恢复编辑窗口。</summary>
    internal Task<object?> PickCancelAsync()
    {
        HideOverlay();
        return Task.FromResult<object?>(true);
    }

    /// <summary>取色完成：把取到的颜色转发给编辑窗口，并恢复界面。</summary>
    internal async Task<object?> PickResultAsync(JToken? payload)
    {
        string? hex = PayloadString(payload);
        if (editorWindow != null && !string.IsNullOrEmpty(hex))
            await SendEventAsync(editorWindow, "companion:pick-result", hex);
        HideOverlay();
        return true;
    }

    // ============ 配置读写 ============

    internal object SaveConfig(JToken? payload)
    {
        try
        {
            string? json = payload?.Type == JTokenType.String
                ? payload.Value<string>()
                : payload?.ToString(Formatting.None);
            if (!string.IsNullOrWhiteSpace(json))
            {
                writeConfigJson(json);
                NotifyFloatConfigChanged();
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "保存陪玩配置失败");
            return false;
        }
    }

    /// <summary>通知数据浮窗配置已变更（编辑器保存后），让其立即刷新采样项清单。</summary>
    void NotifyFloatConfigChanged()
    {
        if (floatWindow != null)
            _ = SendEventAsync(floatWindow, "companion:config-saved", null);
    }

    /// <summary>转发覆盖层上报的区域变更给编辑主窗口。</summary>
    internal object ForwardRegionChange(JToken? payload)
    {
        if (payload == null || editorWindow == null)
            return true;
        try
        {
            string? id = payload["id"]?.ToString();
            JToken? region = payload["region"];
            if (id == null || region == null)
                return true;
            _ = SendEventAsync(editorWindow, "companion:region-changed", new { id, region });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "转发区域变更失败");
        }
        return true;
    }

    internal object OpenConfigFolder()
    {
        try
        {
            if (string.IsNullOrEmpty(configPath))
                return true;
            // 配置目录不存在时先创建，再打开（避免"定位配置文件"打不开）
            if (!Directory.Exists(configPath))
                Directory.CreateDirectory(configPath);
            if (Directory.Exists(configPath))
                _ = alifeApplication.RevealFolderAsync(configPath);
            else if (File.Exists(configPath))
                _ = alifeApplication.RevealFileAsync(configPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开陪玩配置目录失败");
        }
        return true;
    }

    internal async Task<object?> OpenTestSceneAsync()
    {
        if (testSceneWindow != null)
            return true;
        try
        {
            (int screenWidth, int screenHeight) = GetLogicalScreenSize();
            testSceneBounds = new Rectangle(
                Math.Max(0, (screenWidth - 640) / 2),
                Math.Max(0, (screenHeight - 400) / 2),
                640,
                400);
            testSceneWindow = await CreateWindowAsync(
                WindowKind.TestScene,
                "测试场景",
                "test-scene.html",
                testSceneBounds,
                transparent: false,
                resizable: true,
                topMost: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开测试场景失败");
        }
        return true;
    }

    internal object GetTestSceneBounds()
    {
        try
        {
            if (testSceneWindow == null)
                return JValue.CreateNull();
            float dpi = alifeApplication.GetDpiScale();
            if (dpi <= 0)
                dpi = 1;
            return new
            {
                contentBounds = new
                {
                    x = testSceneBounds.X,
                    y = testSceneBounds.Y,
                    width = testSceneBounds.Width,
                    height = testSceneBounds.Height
                },
                scaleFactor = dpi
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取测试场景边界失败");
            return JValue.CreateNull();
        }
    }

    internal string SafeReadConfig()
    {
        try
        {
            string raw = readConfigJson();
            var jo = JObject.Parse(raw);
            if (!string.IsNullOrEmpty(LastGameName))
                jo["DefaultGame"] = LastGameName;
            return jo.ToString(Formatting.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取陪玩配置失败");
            return "{\"Games\":[]}";
        }
    }

    /// <summary>返回主显示器物理分辨率（供分辨率转换比对基准分辨率）。
    /// 注意：不能用窗口逻辑像素，须用一次全屏捕获的位图物理尺寸，与采样坐标保持一致。</summary>
    internal async Task<object?> GetScreenSizeAsync()
    {
        try
        {
            using var frame = await ScreenFrame.CaptureFullscreenAsync();
            if (frame != null)
                return new { width = frame.Width, height = frame.Height };
            // 捕获失败时回退到物理像素
            Size size = alifeApplication.GetScreenSize();
            return new { width = size.Width, height = size.Height };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取屏幕分辨率失败");
            return new { width = 0, height = 0 };
        }
    }

    /// <summary>返回已注册采样器类型列表（含采样器自带的配置 UI 片段）。</summary>
    internal Task<object?> CollectorTypesAsync()
    {
        try
        {
            var types = CollectorRegistry.All.Select(t => new {
                type = t.TypeName,
                name = t.DisplayName,
                ui = CollectorRegistry.GetUi(t.TypeName)
            });
            return Task.FromResult<object?>(JToken.FromObject(types));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取采样器类型列表失败");
            return Task.FromResult<object?>(new JArray());
        }
    }

    static string? PayloadString(JToken? payload)
    {
        if (payload == null || payload.Type == JTokenType.Null)
            return null;
        return payload.Type == JTokenType.String ? payload.Value<string>() : payload.ToString(Formatting.None);
    }

    async Task<object?> CloseWindowAsync(WindowKind kind, IAlifeWindow window)
    {
        switch (kind)
        {
            case WindowKind.Editor:
                if (ReferenceEquals(editorWindow, window))
                    editorWindow = null;
                break;
            case WindowKind.Float:
                if (ReferenceEquals(floatWindow, window))
                    floatWindow = null;
                break;
            case WindowKind.Overlay:
                if (ReferenceEquals(overlayWindow, window))
                {
                    overlayWindow = null;
                    overlayPickMode = false;
                    overlayEditing = false;
                    SetTaskbarVisible(true);
                    if (editorWindow != null)
                    {
                        try { _ = editorWindow.ShowAsync(); }
                        catch (ObjectDisposedException) { editorWindow = null; }
                    }
                }
                break;
            case WindowKind.ViewOverlay:
                if (ReferenceEquals(viewOverlayWindow, window))
                    viewOverlayWindow = null;
                break;
            case WindowKind.TestScene:
                if (ReferenceEquals(testSceneWindow, window))
                    testSceneWindow = null;
                break;
        }
        await DisposeWindowAsync(window);
        return true;
    }

    async Task DisposeWindowAsync(IAlifeWindow window)
    {
        try { await window.DisposeAsync(); }
        catch (Exception ex) { logger.LogDebug(ex, "释放陪玩窗口失败"); }
    }

    public void Dispose()
    {
        DisposeWindow(ref editorWindow);
        DisposeWindow(ref overlayWindow);
        DisposeWindow(ref testSceneWindow);
        DisposeWindow(ref viewOverlayWindow);
        DisposeWindow(ref floatWindow);
        overlayEditing = false;
        overlayPickMode = false;
        SetTaskbarVisible(true);
    }

    void DisposeWindow(ref IAlifeWindow? window)
    {
        IAlifeWindow? target = window;
        window = null;
        if (target != null)
            _ = DisposeWindowAsync(target);
    }
}
