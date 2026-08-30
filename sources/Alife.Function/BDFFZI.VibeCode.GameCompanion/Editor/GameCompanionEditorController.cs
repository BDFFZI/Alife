using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 陪玩配置编辑器控制器。
/// 直接利用 Alife.Client 的 Electron 环境（ElectronNET）创建编辑器主窗口与全屏透明覆盖层窗口，
/// 通过 IpcMain 与渲染进程通信，读写陪玩配置文件。
/// 编辑器 UI 位于插件目录 Editor/ 下（nodeIntegration 渲染进程）。
/// </summary>
public sealed class GameCompanionEditorController : IDisposable
{
    const string PluginId = "BDFFZI.VibeCode.GameCompanion";

    readonly ILogger<GameCompanionModule> logger;
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

    BrowserWindow? editorWindow;
    BrowserWindow? overlayWindow;
    BrowserWindow? testSceneWindow;
    BrowserWindow? viewOverlayWindow; // 只读区域显示覆盖层（不挡鼠标）
    string overlayItemsJson = "[]";
    // 浮窗为每个角色独立（多角色各自一个浮窗，标题区分角色名）
    BrowserWindow? floatWindow;
    string floatDataJson = "{\"State\":0,\"GameName\":\"\",\"Values\":{}}";
    bool floatCollapsed = true; // 初始收起（小球）
    string characterName = "";
    const int FloatExpandedW = 420;
    const int FloatExpandedH = 440;
    const int FloatBallSize = 44;

    public GameCompanionEditorController(
        ILogger<GameCompanionModule> logger,
        PluginSystem pluginSystem,
        string configPath,
        string characterName,
        Func<string> readConfigJson,
        Action<string> writeConfigJson)
    {
        this.logger = logger;
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

    /// <summary>打开编辑器主窗口。已打开时聚焦。</summary>
    public async Task OpenAsync()
    {
        if (editorWindow != null && !await editorWindow.IsDestroyedAsync())
        {
            // 已存在时激活到前台（覆盖层编辑时可能被 Hide，需恢复显示）
            try { editorWindow.Restore(); } catch { }
            editorWindow.Show();
            editorWindow.Focus();
            return;
        }
        if (!Directory.Exists(editorDirectory))
        {
            logger.LogError("编辑器目录不存在: {Dir}", editorDirectory);
            return;
        }

        // IPC 由全局桥管理，每个频道全局只注册一次（多角色共享）
        GameCompanionIpcBridge.Register(this);

        string url = new Uri(Path.Combine(editorDirectory, "editor.html")).AbsoluteUri;
        Display primaryDisplay = await Electron.Screen.GetPrimaryDisplayAsync();
        // 编辑器限制在工作区内（任务栏若出现也不会遮挡编辑器底部）
        int editorHeight = Math.Min(760, Math.Max(560, primaryDisplay.WorkArea.Height - 40));
        editorWindow = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions {
            Title = "陪玩配置编辑器",
            Width = 1080,
            Height = editorHeight,
            MinWidth = 860,
            MinHeight = 600,
            AutoHideMenuBar = true,
            AlwaysOnTop = true,
            WebPreferences = new WebPreferences {
                NodeIntegration = true,
                ContextIsolation = false,
                Sandbox = false,
                DevTools = true
            }
        }, url);
        editorWindow.OnReadyToShow += () => {
            PinTopMost(editorWindow);
            editorWindow!.Show();
        };
        editorWindow.OnClosed += () => {
            editorWindow = null;
            SetTaskbarVisible(true);
        };
        editorWindow.WebContents.OnDidFinishLoad += () => {
            if (editorWindow != null)
                Electron.IpcMain.Send(editorWindow, "companion:config-path", configPath);
        };
        logger.LogInformation("已打开陪玩配置编辑器");
    }

    // ============ 数据浮窗 ============

    /// <summary>显示数据浮窗（陪玩启动时调用）。已创建时直接显示。</summary>
    public async Task ShowFloatAsync()
    {
        // IPC 由全局桥统一管理：浮窗交互（收起/展开/编辑/移动）也要注册频道
        GameCompanionIpcBridge.Register(this);

        if (floatWindow != null && !await floatWindow.IsDestroyedAsync())
        {
            floatWindow.Show();
            return;
        }

        try
        {
            Display primary = await Electron.Screen.GetPrimaryDisplayAsync();
            string url = new Uri(Path.Combine(editorDirectory, "float.html")).AbsoluteUri;
            floatWindow = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions {
                // 初始即为收起的小球尺寸，避免首次 280×380 透明区挡鼠标
                Width = FloatBallSize,
                Height = FloatBallSize,
                X = primary.Bounds.X + 30,
                Y = primary.Bounds.Y + 30,
                Frame = false,
                Transparent = true,
                Resizable = false,
                Movable = true,
                AlwaysOnTop = true,
                SkipTaskbar = true,
                HasShadow = false,
                Focusable = true,
                Fullscreenable = false,
                BackgroundColor = "#00000000",
                Show = false,
                WebPreferences = new WebPreferences {
                    NodeIntegration = true,
                    ContextIsolation = false,
                    Sandbox = false,
                    DevTools = true
                }
            }, url);
            floatWindow.OnReadyToShow += () => {
                PinTopMost(floatWindow);
                floatWindow!.Show();
            };
            floatWindow.OnClosed += () => { floatWindow = null; };
            floatWindow.WebContents.OnDidFinishLoad += () => {
                if (floatWindow != null)
                {
                    Electron.IpcMain.Send(floatWindow, "companion:float-character", characterName);
                    Electron.IpcMain.Send(floatWindow, "companion:float-data", floatDataJson);
                }
            };
            logger.LogInformation("已打开陪玩数据浮窗");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开陪玩数据浮窗失败");
        }
    }

    /// <summary>
    /// 提升窗口置顶层级到最高（screen-saver），确保在全屏/无边框游戏上仍保持置顶，
    /// 并防止拖动/失焦后被游戏盖住。
    /// </summary>
    static void PinTopMost(BrowserWindow window)
    {
        try
        {
            // screen-saver 层级 = 最高置顶（仅次于系统），确保盖过任务栏与无边框全屏游戏
            window.SetAlwaysOnTop(true, (OnTopLevel)7, 1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companion] 提升窗口置顶层级失败: {ex.Message}");
        }
    }
    // ============ 系统任务栏控制 ============

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    const int SW_HIDE = 0;
    const int SW_SHOW = 5;

    /// <summary>隐藏/显示系统任务栏（进入覆盖层编辑时隐藏，避免隐形任务栏热区遮挡覆盖层底部）。</summary>
    static void SetTaskbarVisible(bool visible)
    {
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

    /// <summary>隐藏数据浮窗（陪玩停止时调用）。</summary>
    public Task HideFloatAsync()
    {
        if (floatWindow != null)
        {
            try { floatWindow.Close(); } catch { }
            floatWindow = null;
        }
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
            Electron.IpcMain.Send(floatWindow, "companion:float-data", floatDataJson);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "推送陪玩数据到浮窗失败");
        }
    }

    // ============ 覆盖层 ============

    internal async Task ShowOverlayAsync(object? payload)
    {
        overlayItemsJson = PayloadToString(payload) ?? "[]";

        // 覆盖层调试：渲染进程上报坐标信息
        try
        {
            Electron.IpcMain.On("companion:overlay-debug", (msg) => {
                logger.LogInformation("覆盖层调试: {Msg}", msg?.ToString());
            });
        }
        catch { }

        // 编辑区域时隐藏系统任务栏：覆盖层需满屏且不被隐形任务栏热区遮挡
        SetTaskbarVisible(false);

        // 编辑区域时隐藏主窗口，避免遮挡屏幕
        if (editorWindow != null && !await editorWindow.IsDestroyedAsync())
            editorWindow.Hide();

        if (overlayWindow != null && !await overlayWindow.IsDestroyedAsync())
        {
            Electron.IpcMain.Send(overlayWindow, "companion:overlay-items", overlayItemsJson);
            overlayWindow.Show();
            return;
        }

        try
        {
            Display primary = await Electron.Screen.GetPrimaryDisplayAsync();
            logger.LogInformation("覆盖层: Bounds=({0},{1},{2},{3}) WorkArea=({4},{5},{6},{7})",
                primary.Bounds.X, primary.Bounds.Y, primary.Bounds.Width, primary.Bounds.Height,
                primary.WorkArea.X, primary.WorkArea.Y, primary.WorkArea.Width, primary.WorkArea.Height);
            // 加时间戳强制绕过 file:// 的 JS/CSS 缓存，确保每次打开加载最新资源
            string url = new Uri(Path.Combine(editorDirectory, "overlay.html")).AbsoluteUri + "?v=" + DateTime.Now.Ticks;
            overlayWindow = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions {
                X = primary.Bounds.X,
                Y = primary.Bounds.Y,
                Width = primary.Bounds.Width,
                Height = primary.Bounds.Height,
                Transparent = true,
                Frame = false,
                Resizable = false,
                Movable = false,
                AlwaysOnTop = true,
                SkipTaskbar = true,
                HasShadow = false,
                Focusable = true,
                Fullscreenable = false,
                BackgroundColor = "#00000000",
                Show = false,
                WebPreferences = new WebPreferences {
                    NodeIntegration = true,
                    ContextIsolation = false,
                    Sandbox = false,
                    DevTools = true
                }
            }, url);
            overlayWindow.OnReadyToShow += () => {
                PinTopMost(overlayWindow);
                // 透明窗口创建时可能被系统压缩到工作区高度，显示前强制撑满全屏
                try
                {
                    Display pd = Electron.Screen.GetPrimaryDisplayAsync().GetAwaiter().GetResult();
                    overlayWindow.SetBounds(new Rectangle {
                        X = pd.Bounds.X, Y = pd.Bounds.Y,
                        Width = pd.Bounds.Width, Height = pd.Bounds.Height
                    });
                }
                catch { }
                overlayWindow!.Show();
                // 显示后 Electron 可能给透明窗口加隐形左边框导致 X 偏移，再次强制归位到 0
                try
                {
                    Display pd2 = Electron.Screen.GetPrimaryDisplayAsync().GetAwaiter().GetResult();
                    overlayWindow.SetBounds(new Rectangle {
                        X = pd2.Bounds.X, Y = pd2.Bounds.Y,
                        Width = pd2.Bounds.Width, Height = pd2.Bounds.Height
                    });
                }
                catch { }
            };
            overlayWindow.OnClosed += () => { overlayWindow = null; };
            overlayWindow.WebContents.OnDidFinishLoad += () => {
                if (overlayWindow != null)
                {
                    Rectangle ob = overlayWindow.GetBoundsAsync().GetAwaiter().GetResult();
                    IntPtr tray = FindWindow("Shell_TrayWnd", null);
                    string? inner = null;
                    try
                    {
                        inner = overlayWindow.WebContents
                            .ExecuteJavaScriptAsync<string>("JSON.stringify({iw:window.innerWidth,ih:window.innerHeight,sdpr:devicePixelRatio})", true)
                            .GetAwaiter().GetResult();
                    }
                    catch { }
                    logger.LogInformation("覆盖层就绪: bounds=({0},{1},{2},{3}) trayHandle={4} inner={5}",
                        ob.X, ob.Y, ob.Width, ob.Height, tray, inner);
                    Electron.IpcMain.Send(overlayWindow, "companion:overlay-items", overlayItemsJson);
                }
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开覆盖层窗口失败");
        }
    }

    internal void HideOverlay()
    {
        if (overlayWindow != null)
        {
            try { overlayWindow.Close(); } catch { }
            overlayWindow = null;
        }
        // 覆盖层关闭后恢复系统任务栏
        SetTaskbarVisible(true);
        // 覆盖层关闭后恢复显示编辑主窗口
        if (editorWindow != null)
        {
            try { editorWindow.Show(); } catch { }
        }
    }

    /// <summary>采样前隐藏「只读预览覆盖层」（悬浮窗勾选），编辑拖拽覆盖层保持长显不隐藏。返回是否曾隐藏。</summary>
    public bool HideOverlayForSample()
    {
        bool any = false;
        if (viewOverlayWindow != null && !viewOverlayWindow.IsDestroyedAsync().GetAwaiter().GetResult())
        {
            try { viewOverlayWindow.WebContents.ExecuteJavaScriptAsync<object>("document.body.style.opacity='0';").GetAwaiter().GetResult(); any = true; } catch { }
        }
        return any;
    }

    /// <summary>采样后恢复只读预览覆盖层可见。</summary>
    public void RestoreOverlayForSample()
    {
        if (viewOverlayWindow != null && !viewOverlayWindow.IsDestroyedAsync().GetAwaiter().GetResult())
        {
            try { viewOverlayWindow.WebContents.ExecuteJavaScriptAsync<object>("document.body.style.opacity='1';").GetAwaiter().GetResult(); } catch { }
        }
    }

    /// <summary>是否处于编辑拖拽覆盖层状态（该覆盖层打开可见）。此时采样跳过隐藏只读预览，避免闪烁。</summary>
    public bool IsOverlayEditing()
    {
        if (overlayWindow == null || overlayWindow.IsDestroyedAsync().GetAwaiter().GetResult())
            return false;
        try
        {
            return overlayWindow.IsVisibleAsync().GetAwaiter().GetResult();
        }
        catch { return false; }
    }

    // ============ 只读区域显示覆盖层（浮窗勾选，不挡鼠标） ============

    /// <summary>浮窗「显示覆盖层」开关：payload 为 {show, game}。</summary>
    internal Task<object> FloatOverlayViewAsync(object? payload)
    {
        try
        {
            bool show = false;
            string? game = null;
            string? raw = PayloadToString(payload);
            if (!string.IsNullOrEmpty(raw) && raw.StartsWith("{"))
            {
                var jo = JObject.Parse(raw);
                show = jo["show"]?.Value<bool>() ?? false;
                game = jo["game"]?.ToString();
            }
            else
            {
                show = raw == "true";
            }
            if (!string.IsNullOrEmpty(game))
                LastGameName = game;
            if (show) ShowOverlayView();
            else HideOverlayView();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "切换只读覆盖层失败");
        }
        return Task.FromResult<object>(true);
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
    async void ShowOverlayView()
    {
        if (viewOverlayWindow != null && !await viewOverlayWindow.IsDestroyedAsync())
        {
            // 已存在则刷新区域并显示
            Electron.IpcMain.Send(viewOverlayWindow, "companion:overlay-items", BuildOverlayViewItems());
            viewOverlayWindow.Show();
            return;
        }
        try
        {
            Display primary = await Electron.Screen.GetPrimaryDisplayAsync();
            // 复用编辑覆盖层页面，?view=1 进入只读模式
            string url = new Uri(Path.Combine(editorDirectory, "overlay.html")).AbsoluteUri + "?view=1&v=" + DateTime.Now.Ticks;
            viewOverlayWindow = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions {
                X = primary.Bounds.X,
                Y = primary.Bounds.Y,
                Width = primary.Bounds.Width,
                Height = primary.Bounds.Height,
                Transparent = true,
                Frame = false,
                Resizable = false,
                Movable = false,
                AlwaysOnTop = true,
                SkipTaskbar = true,
                HasShadow = false,
                Focusable = false,
                Fullscreenable = false,
                BackgroundColor = "#00000000",
                Show = false,
                WebPreferences = new WebPreferences {
                    NodeIntegration = true,
                    ContextIsolation = false,
                    Sandbox = false,
                    DevTools = false
                }
            }, url);
            viewOverlayWindow.OnReadyToShow += () => {
                PinTopMost(viewOverlayWindow);
                // 不抢焦点、鼠标穿透
                try { viewOverlayWindow.SetIgnoreMouseEvents(true); } catch { }
                // 强制撑满全屏并归位到 0,0，避免透明窗口被系统加隐形左边框导致 X 偏移
                try
                {
                    Display pd = Electron.Screen.GetPrimaryDisplayAsync().GetAwaiter().GetResult();
                    viewOverlayWindow.SetBounds(new Rectangle {
                        X = pd.Bounds.X, Y = pd.Bounds.Y,
                        Width = pd.Bounds.Width, Height = pd.Bounds.Height
                    });
                }
                catch { }
                viewOverlayWindow.ShowInactive();
            };
            viewOverlayWindow.OnClosed += () => { viewOverlayWindow = null; };
            viewOverlayWindow.WebContents.OnDidFinishLoad += () => {
                if (viewOverlayWindow != null)
                    Electron.IpcMain.Send(viewOverlayWindow, "companion:overlay-items", BuildOverlayViewItems());
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开只读覆盖层失败");
        }
    }

    /// <summary>隐藏只读覆盖层。</summary>
    void HideOverlayView()
    {
        if (viewOverlayWindow != null)
        {
            try { viewOverlayWindow.Close(); } catch { }
            viewOverlayWindow = null;
        }
    }

    // ============ 屏幕取色 ============

    /// <summary>进入屏幕取色模式：隐藏编辑窗口，显示全屏覆盖层供点击取色。</summary>
    internal async Task<object> PickColorAsync()
    {
        try
        {
            Display primary = await Electron.Screen.GetPrimaryDisplayAsync();
            string url = new Uri(Path.Combine(editorDirectory, "overlay.html")).AbsoluteUri + "?v=" + DateTime.Now.Ticks;
            if (overlayWindow != null && !await overlayWindow.IsDestroyedAsync())
            {
                Electron.IpcMain.Send(overlayWindow, "companion:pick-mode", true);
                overlayWindow.Show();
            }
            else
            {
                overlayWindow = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions {
                    X = primary.Bounds.X, Y = primary.Bounds.Y,
                    Width = primary.Bounds.Width, Height = primary.Bounds.Height,
                    Transparent = true, Frame = false, Resizable = false, Movable = false,
                    AlwaysOnTop = true, SkipTaskbar = true, HasShadow = false,
                    Focusable = true, Fullscreenable = false, BackgroundColor = "#00000000",
                    Show = false,
                    WebPreferences = new WebPreferences {
                        NodeIntegration = true, ContextIsolation = false, Sandbox = false, DevTools = true
                    }
                }, url);
                overlayWindow.OnReadyToShow += () => {
                    PinTopMost(overlayWindow);
                    // 强制铺满全屏（同拖拽覆盖层），避免透明窗口被压缩导致取色区域不全
                    try
                    {
                        Display pd = Electron.Screen.GetPrimaryDisplayAsync().GetAwaiter().GetResult();
                        overlayWindow.SetBounds(new Rectangle {
                            X = pd.Bounds.X, Y = pd.Bounds.Y,
                            Width = pd.Bounds.Width, Height = pd.Bounds.Height
                        });
                    }
                    catch { }
                    overlayWindow!.Show();
                    Electron.IpcMain.Send(overlayWindow, "companion:pick-mode", true);
                };
                overlayWindow.OnClosed += () => { overlayWindow = null; };
            }
            // 取色期间隐藏编辑主窗口，避免遮挡
            if (editorWindow != null && !await editorWindow.IsDestroyedAsync())
                editorWindow.Hide();
            SetTaskbarVisible(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "进入取色模式失败");
        }
        return true;
    }

    /// <summary>取色覆盖层点击：截图取该点颜色，返回十六进制色值。</summary>
    internal async Task<object> PickAtAsync(object? payload)
    {
        try
        {
            string? json = PayloadToString(payload);
            JObject? obj = string.IsNullOrEmpty(json) ? null : JObject.Parse(json);
            int x = obj?["x"]?.Value<int>() ?? 0;
            int y = obj?["y"]?.Value<int>() ?? 0;
            // 截图前隐藏覆盖层窗口，避免把半透明覆盖层自身截入画面导致取到灰色
            if (overlayWindow != null)
            {
                try { overlayWindow.Hide(); } catch { }
                await Task.Delay(50);
            }
            // 物理像素坐标截图取色（覆盖层以物理像素工作）
            using ScreenFrame frame = await ScreenFrame.CaptureFullscreenAsync();
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
    internal Task<object> PickCancelAsync()
    {
        HideOverlay();
        return Task.FromResult<object>(true);
    }

    /// <summary>取色完成：把取到的颜色转发给编辑窗口，并恢复界面。</summary>
    internal Task<object> PickResultAsync(object? payload)
    {
        string? hex = PayloadToString(payload);
        if (editorWindow != null && !string.IsNullOrEmpty(hex))
        {
            try { Electron.IpcMain.Send(editorWindow, "companion:pick-result", hex); } catch { }
        }
        HideOverlay();
        return Task.FromResult<object>(true);
    }

    // ============ IPC 处理（由全局桥调用） ============

    internal Task<object> FloatExpandAsync()
    {
        SwitchFloatState(false);
        return Task.FromResult<object>(true);
    }

    internal Task<object> FloatCollapseAsync()
    {
        SwitchFloatState(true);
        return Task.FromResult<object>(true);
    }

    internal Task<object> FloatEditAsync(object? payload)
    {
        if (payload is string gameName && !string.IsNullOrEmpty(gameName))
            LastGameName = gameName;
        _ = OpenAsync();
        return Task.FromResult<object>(true);
    }

    internal Task<object> FloatMoveAsync(object? payload)
    {
        try
        {
            if (floatWindow == null)
                return Task.FromResult<object>(true);
            JObject obj = JObject.Parse(PayloadToString(payload) ?? "{}");
            int dx = obj["dx"]?.Value<int>() ?? 0;
            int dy = obj["dy"]?.Value<int>() ?? 0;
            Rectangle bounds = floatWindow.GetBoundsAsync().GetAwaiter().GetResult();
            floatWindow.SetBounds(new Rectangle {
                X = bounds.X + dx,
                Y = bounds.Y + dy,
                Width = bounds.Width,
                Height = bounds.Height
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "移动陪玩数据浮窗失败");
        }
        return Task.FromResult<object>(true);
    }

    internal Task<object> FloatPauseToggleAsync()
    {
        try
        {
            TogglePaused?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "切换陪玩暂停状态失败");
        }
        return Task.FromResult<object>(true);
    }

    /// <summary>浮窗内快速开关采样项（持久化到配置，下一周期监控自动生效）。</summary>
    internal Task<object> FloatToggleAsync(object? payload)
    {
        try
        {
            JObject obj = JObject.Parse(PayloadToString(payload) ?? "{}");
            string game = obj["game"]?.ToString() ?? "";
            string name = obj["name"]?.ToString() ?? "";
            bool enable = obj["enable"]?.Value<bool>() ?? true;
            if (!string.IsNullOrEmpty(game) && !string.IsNullOrEmpty(name))
                ToggleCollector?.Invoke(game, name, enable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "切换采样项启用状态失败");
        }
        return Task.FromResult<object>(true);
    }

    /// <summary>返回游戏列表给浮窗选择（含各游戏采样项明细，供未运行时可展示/开关）。</summary>
    internal Task<object> FloatGamesAsync()
    {
        try
        {
            if (GetGamesList == null)
                return Task.FromResult<object>("[]");
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
            return Task.FromResult<object>(JsonConvert.SerializeObject(games));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取游戏列表失败");
            return Task.FromResult<object>("[]");
        }
    }

    /// <summary>启动指定游戏陪玩。</summary>
    internal Task<object> FloatStartAsync(object? payload)
    {
        try
        {
            string? name = PayloadToString(payload);
            if (!string.IsNullOrWhiteSpace(name))
                StartGame?.Invoke(name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "浮窗启动陪玩失败");
        }
        return Task.FromResult<object>(true);
    }

    /// <summary>停止当前陪玩。</summary>
    internal Task<object> FloatStopAsync()
    {
        try
        {
            StopGame?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "浮窗停止陪玩失败");
        }
        return Task.FromResult<object>(true);
    }

    /// <summary>浮窗当前窗口位置（供渲染进程作拖拽基准，DIP 与 SetPosition 一致）。</summary>
    internal Task<object> FloatGetPosAsync()
    {
        try
        {
            if (floatWindow == null)
                return Task.FromResult<object>("{ \"x\": 0, \"y\": 0 }");
            int[] pos = floatWindow.GetPositionAsync().GetAwaiter().GetResult();
            return Task.FromResult<object>(JsonConvert.SerializeObject(new { x = pos[0], y = pos[1] }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取陪玩数据浮窗位置失败");
            return Task.FromResult<object>("{ \"x\": 0, \"y\": 0 }");
        }
    }

    /// <summary>浮窗绝对定位（JS 拖动时按屏幕坐标直接设置窗口位置，避免累加抖动）。</summary>
    internal Task<object> FloatMoveToAsync(object? payload)
    {
        try
        {
            if (floatWindow == null)
                return Task.FromResult<object>(true);
            JObject obj = JObject.Parse(PayloadToString(payload) ?? "{}");
            int x = obj["x"]?.Value<int>() ?? 0;
            int y = obj["y"]?.Value<int>() ?? 0;
            // 仅移动位置，不做 SetBounds，避免污染窗口宽高
            floatWindow.SetPosition(x, y);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "定位陪玩数据浮窗失败");
        }
        return Task.FromResult<object>(true);
    }

    void SwitchFloatState(bool collapsed)
    {
        floatCollapsed = collapsed;
        _ = ResizeFloatAsync();
    }

    async Task ResizeFloatAsync()
    {
        if (floatWindow == null || await floatWindow.IsDestroyedAsync())
            return;

        // 始终保持左上角锚点不变：收起为小球（左上角即小球位置），
        // 展开为面板，面板标题也在左上角 → 球与标题位置重合，点击一处即可收展
        var bounds = await floatWindow.GetBoundsAsync();
        if (floatCollapsed)
        {
            floatWindow.SetBounds(new Rectangle {
                X = bounds.X,
                Y = bounds.Y,
                Width = FloatBallSize,
                Height = FloatBallSize
            });
            var after = await floatWindow.GetBoundsAsync();
            logger.LogInformation("浮窗收起后 bounds=({0},{1},{2},{3})", after.X, after.Y, after.Width, after.Height);
        }
        else
        {
            floatWindow.SetBounds(new Rectangle {
                X = bounds.X,
                Y = bounds.Y,
                Width = FloatExpandedW,
                Height = FloatExpandedH
            });
        }
    }

    internal Task<object> SaveConfigAsync(object? payload)
    {
        return Task.FromResult(SaveConfig(payload));
    }

    /// <summary>返回主显示器物理分辨率（供分辨率转换比对基准分辨率）。
    /// 注意：不能用 Electron 的 Bounds（那是逻辑像素/DIP），须用一次全屏捕获的位图物理尺寸，与采样坐标保持一致。</summary>
    internal async Task<object> GetScreenSizeAsync()
    {
        try
        {
            using var frame = await ScreenFrame.CaptureFullscreenAsync();
            if (frame != null)
                return JsonConvert.SerializeObject(new { width = frame.Width, height = frame.Height });
            // 捕获失败时回退到物理像素：按 Electron 逻辑分辨率 × 缩放比例估算
            Display primary = await Electron.Screen.GetPrimaryDisplayAsync();
            double scale = primary.ScaleFactor > 0 ? primary.ScaleFactor : 1.0;
            return JsonConvert.SerializeObject(new { width = (int)Math.Round(primary.Bounds.Width * scale), height = (int)Math.Round(primary.Bounds.Height * scale) });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取屏幕分辨率失败");
            return "{\"width\":0,\"height\":0}";
        }
    }

    /// <summary>返回已注册采样器类型列表（含采样器自带的配置 UI 片段）。</summary>
    internal Task<object> CollectorTypesAsync()
    {
        try
        {
            var types = CollectorRegistry.All.Select(t => new {
                type = t.TypeName,
                name = t.DisplayName,
                ui = CollectorRegistry.GetUi(t.TypeName)
            });
            return Task.FromResult<object>(JsonConvert.SerializeObject(types));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取采样器类型列表失败");
            return Task.FromResult<object>("[]");
        }
    }

    /// <summary>同步保存（供渲染进程关闭窗口时 sendSync 落盘）。</summary>
    internal object SaveConfig(object? payload)
    {
        try
        {
            string? json = PayloadToString(payload);
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
        try
        {
            if (floatWindow != null)
                Electron.IpcMain.Send(floatWindow, "companion:config-saved");
        }
        catch { }
    }

    internal void ForwardRegionChange(object? payload)
    {
        string? json = PayloadToString(payload);
        if (string.IsNullOrEmpty(json) || editorWindow == null)
            return;
        try
        {
            JObject obj = JObject.Parse(json);
            string? id = obj["id"]?.ToString();
            JToken? region = obj["region"];
            if (id == null || region == null)
                return;
            Electron.IpcMain.Send(editorWindow, "companion:region-changed", id, region.ToString(Formatting.None));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "转发区域变更失败");
        }
    }

    internal void OpenConfigFolder()
    {
        try
        {
            if (string.IsNullOrEmpty(configPath))
                return;
            // 配置目录不存在时先创建，再打开（避免"定位配置文件"打不开）
            if (!Directory.Exists(configPath))
                Directory.CreateDirectory(configPath);
            if (Directory.Exists(configPath))
                _ = Electron.Shell.OpenPathAsync(configPath);
            else if (File.Exists(configPath))
                _ = Electron.Shell.ShowItemInFolderAsync(configPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开陪玩配置目录失败");
        }
    }

    internal void OpenTestScene()
    {
        if (testSceneWindow != null)
            return;
        try
        {
            string url = new Uri(Path.Combine(editorDirectory, "test-scene.html")).AbsoluteUri;
            testSceneWindow = Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions {
                Title = "测试场景",
                Width = 640,
                Height = 400,
                AutoHideMenuBar = true,
                WebPreferences = new WebPreferences {
                    NodeIntegration = true,
                    ContextIsolation = false,
                    Sandbox = false,
                    DevTools = true,
                    // 后台持续渲染，避免被遮挡时 WGC 捕获到陈旧帧
                    BackgroundThrottling = false
                }
            }, url).GetAwaiter().GetResult();
            testSceneWindow.OnClosed += () => { testSceneWindow = null; };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "打开测试场景失败");
        }
    }

    internal Task<object> GetTestSceneBoundsAsync()
    {
        try
        {
            if (testSceneWindow == null)
                return Task.FromResult<object>("null");
            Rectangle content = testSceneWindow.GetContentBoundsAsync().GetAwaiter().GetResult();
            double scale = Electron.Screen.GetPrimaryDisplayAsync().GetAwaiter().GetResult().ScaleFactor;
            return Task.FromResult<object>(JsonConvert.SerializeObject(new {
                contentBounds = new { x = content.X, y = content.Y, width = content.Width, height = content.Height },
                scaleFactor = scale
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取测试场景边界失败");
            return Task.FromResult<object>("null");
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

    static string? PayloadToString(object? payload) => payload switch
    {
        string s => s,
        null => null,
        _ => payload.ToString()
    };

public void Dispose()
    {
        try { editorWindow?.Close(); } catch { }
        try { overlayWindow?.Close(); } catch { }
        try { testSceneWindow?.Close(); } catch { }
        try { floatWindow?.Close(); } catch { }
        try { viewOverlayWindow?.Close(); } catch { }
        editorWindow = null;
        overlayWindow = null;
        testSceneWindow = null;
        floatWindow = null;
        viewOverlayWindow = null;
        // 每个角色的控制器(含其浮窗窗口)由所属角色销毁时独立回收

        // 从桥上摘除本控制器，全局 IPC 频道保留（其他角色仍在用）
        GameCompanionIpcBridge.Unregister(this);
    }
}
