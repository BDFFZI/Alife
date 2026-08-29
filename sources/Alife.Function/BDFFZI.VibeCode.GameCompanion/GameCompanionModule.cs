using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.AIModelUtility;
using Alife.Function.FunctionCaller;
using Alife.Function.MessageFilter;
using BDFFZI.VibeCode.GameCompanion;
using BDFFZI.VibeCode.GameCompanion;
using BDFFZI.VibeCode.GameCompanion;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 游戏陪玩模块：基于语音识别与图像识别的通用陪玩系统。
/// 配置以目录方式存储在 Storage/GameCompanion/（每游戏一份 JSON，全局共享），
/// 监控循环持续采样各数据项：全部采样项数据有效且数值变动时向 AI 上报新值。
/// 图形化配置通过模块页启动的 Electron 编辑器完成。
/// </summary>
[Module("游戏陪玩",
    "基于视觉与音频采集的通用游戏陪玩系统：为不同游戏配置数据采集目标，数据有效且有变化时实时感知并汇报给 AI。可加入 Alife 官方群获取他人捏好的游戏陪玩配置。",
    defaultCategory: "真央的小工具")]
public class GameCompanionModule(
    XmlFunctionCaller functionService,
    ILogger<GameCompanionModule> logger,
    Interactor<GameCompanionModule> interactor,
    PluginSystem pluginSystem,
    Alife.Function.MessageFilter.MessageFilterService messageFilterService,
    IAudioRecognizerProvider? audioRecognizerProvider = null) :
    ChatBehaviour,
    IConfigurable<GameCompanionConfig>
{
    public GameCompanionStore Store { get; } = new();

    /// <summary>模块全局配置（框架在构造后注入，OnAwake 起可用）。</summary>
    public GameCompanionConfig Configuration { get; set; } = null!;

    // 实例沙盒隔离：每个模块实例持有自己的监控与编辑器控制器，OnDestroy 时各自回收
    readonly object monitorLock = new();
    bool stepping;
    long stepStartedAt;
    long lastStepAt; // 上次采样时刻，用于限频
    GameMonitor? activeMonitor;
    GameCompanionEditorController? editorController;
    System.Runtime.Loader.AssemblyLoadContext? pluginAlc;
    Action<System.Runtime.Loader.AssemblyLoadContext>? alcUnloadingHandler;

    /// <summary>当前监控状态（供 UI 展示）。</summary>
    public MonitorState MonitorState => activeMonitor?.State ?? MonitorState.Stopped;

    /// <summary>当前正在陪玩的游戏名（供 UI 展示）。</summary>
    public string CurrentGame => activeMonitor?.GameName ?? "";

    /// <summary>当前状态文本（供 UI 展示）。</summary>
    public string StatusText => MonitorState switch
    {
        MonitorState.Stopped => "停止",
        _ => $"陪玩中 ({CurrentGame})"
    };

    protected override async Task OnAwake()
    {
        // 确保进程按物理像素感知 DPI，使 Win32 坐标与 WGC 捕获一致
        BDFFZI.VibeCode.GameCompanion.ScreenFrame.EnsureProcessDpiAware();

        // 回复要求约束：收到陪玩消息时，AI 默认用 speak 标签回复（像真人观众插话评论）
        messageFilterService.AddMessageReplyRule(new MessageReplyRule {
            Name = nameof(GameCompanionModule),
            InputMatching = input => input.Contains(Interactor<GameCompanionModule>.GetMessageTag()),
            OutputMatching = output => output.Contains("<speak>", StringComparison.OrdinalIgnoreCase),
            CorrectionMessage = () =>
                "陪玩数据/评论请用 <speak>标签</speak> 回复（真人观战口吻，简短自然地插话评论）。若不想说话，请发送空的 <speak></speak> 标签。"
        }, DestroyCancellationToken);

        // 语音识别器由语音采样器内部经共享池创建/复用/回收，这里只需注入提供者
        VoiceDetectorPool.Initialize(audioRecognizerProvider);

        // 扫描所有插件程序集，自动注册带 [Collector] 特性的采样器
        CollectorRegistry.Initialize(pluginSystem.PluginContext);

        // 编辑器统一读写全局陪玩配置目录（每游戏一个 JSON + config.json）
        editorController = new GameCompanionEditorController(
            logger,
            pluginSystem,
            Store.RootDirectory,
            Character?.Name ?? "",
            readConfigJson: () => {
                // 编辑器读取：GameName 由 Store 注入（配置内不存），
                // 序列化前合并进每个游戏的 JSON 中供编辑器识别
                var games = Store.ListGames();
                var wrapperGames = games.Select(g => {
                    var jo = JObject.FromObject(g);
                    jo["GameName"] = g.GameName;
                    return jo;
                });
                return JsonConvert.SerializeObject(new { Games = wrapperGames }, Formatting.Indented);
            },
            writeConfigJson: json => {
                logger.LogInformation("编辑器保存配置: {Json}", json);
                var bundle = JsonConvert.DeserializeObject<CompanionConfigBundle>(json);
                if (bundle == null)
                    return;
                var savedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JObject jo in bundle.Games)
                {
                    string? name = jo["GameName"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    GameConfig? game = jo.ToObject<GameConfig>();
                    if (game == null)
                        continue;
                    game.GameName = name;
                    Store.SaveGame(game);
                    savedNames.Add(name);
                }
                // 编辑器为全量权威来源：清理本次未保存(重命名/删除)的旧文件
                Store.SyncRemoved(savedNames);
            });
        editorController.TogglePaused = () => {
            lock (monitorLock)
            {
                if (activeMonitor == null)
                    return;
                bool next = !activeMonitor.IsPaused;
                activeMonitor.SetPaused(next);
                interactor.Poke(next ? "陪玩监控已暂停。" : "陪玩监控已恢复。");
            }
        };
        editorController.GetGamesList = () => Store.ListGames();
        editorController.StartGame = gameName => StartCompanion(gameName);
        editorController.StopGame = () => StopCompanion();
        editorController.ToggleCollector = SetCollectorEnabled;

        var handler = new XmlHandler(this)
        {
            Description = "游戏陪玩：查询、启动/停止陪玩监测，打开配置编辑器",
            Explanation = """
            你具备「游戏陪玩」能力，可在游戏过程中感知实时数据变化并据此与玩家互动。

            使用场景：
            - 用户问「支持哪些游戏陪玩」「有什么游戏配置」→ 列出已配置的游戏
            - 用户说「开始陪我打XX」「开陪玩」→ 启动对应游戏的陪玩监测（需该游戏已经配置好）
            - 用户说「停止陪玩」「不玩了」→ 停止当前陪玩监测

            数据汇报规则：当监测到数据变化时会收到「【游戏陪玩·游戏名】数据名: 旧值→新值」的汇报（例：生命值 100→40、击杀 true、颜色 蓝→红）。请将它视为玩家实时状态，输出符合当下场景的陪玩台词，让玩家感到你真的在「看着」他玩（如血量骤降时关切提醒）。除非玩家要求，不要复述配置明细。
            """
        };
        functionService.RegisterHandler(handler, DocumentMode.Implicit, DestroyCancellationToken);

        // 订阅插件 ALC 卸载事件：重载/卸载插件时旧实例不会被 OnDestroy 逐一回收，
        // 受影响靠 Unloading 兜底——同步停止监控并释放编辑器/浮窗，避免残留"陪玩中"状态
        SubscribeAlcUnloading();

        // 浮窗作为唯一入口：模块启动后始终显示
        _ = editorController?.ShowFloatAsync();

        await Task.CompletedTask;
    }

    // ============ AI 工具 ============

    [XmlFunction(FunctionMode.OneShot)]
    [Description("列出所有已配置陪玩的游戏及其监测项")]
    public Task ListCompanionGames()
    {
        List<GameConfig> games = Store.ListGames();
        if (games.Count == 0)
        {
            interactor.Poke("尚未配置任何游戏陪玩。请通过陪玩配置编辑器添加游戏配置。");
            return Task.CompletedTask;
        }

        var lines = games
            .Select(g =>
            {
                string targets = g.Collectors.Count == 0
                    ? "无监测项"
                    : string.Join("、", g.Collectors.Select(t => $"{t.Name}({CollectorRegistry.DisplayName(CollectorRegistry.TypeName(t))})"));
                return $"- {g.GameName} | 监测: {targets}";
            });

        string state = MonitorState == MonitorState.Running
            ? $"陪玩中 ({CurrentGame})"
            : "未在陪玩";
        interactor.Poke($$"""
                         【已配置的游戏陪玩】
                         {{string.Join("\n", lines)}}
                         当前状态: {{state}}
                         """);
        return Task.CompletedTask;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("启动指定游戏的陪玩监测")]
    public Task StartCompanion([Description("游戏名称")] string gameName)
    {
lock (monitorLock)
        {
            if (activeMonitor != null)
            {
                interactor.Poke("已有游戏正在陪玩中，需先停止再切换。");
                return Task.CompletedTask;
            }

            GameConfig? game = Store.ListGames()
                .FirstOrDefault(g => string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase));
            if (game == null)
            {
                interactor.Poke($"未找到「{gameName}」的陪玩配置。请先在陪玩配置编辑器中配置该游戏。");
                return Task.CompletedTask;
            }

            // 无可采集目标时，拒绝启动并提示用户去配置
            var enabledCollectors = game.Collectors.Where(c => c.IsEnable).ToList();
            if (enabledCollectors.Count == 0)
            {
                interactor.Poke($"「{game.GameName}」没有任何已启用的采集目标，无法开始陪玩。请在陪玩配置编辑器为该游戏添加至少一个采集目标后再开启。");
                return Task.CompletedTask;
            }

            var monitor = new GameMonitor(
                logger,
                report: message => {
                    // 若 chatbot 恰好在对话中（AI 正在回话），返回 false 表示本次未真正推送，
                    // GameMonitor 会保留该值（不重置防抖/不记 lastPushed），等对话空闲后重试
                    if (ChatBot.IsChatOccupied)
                    {
                        logger.LogInformation("[陪玩] 对话中，推迟推送: {Msg}", message);
                        return false;
                    }
                    interactor.Poke(message);
                    return true;
                },
                reportForce: message => {
                    // 强制推送：直接 Chat 打断当前对话，立即推送（连同其他可推送项）
                    interactor.Chat(message);
                    return true;
                },
                // 状态/错误信息仅记日志，不打扰 AI：AI 只接收开启/关闭陪玩与数值变化信息
                announce: message => logger.LogInformation("[陪玩状态] {Msg}", message),
                onData: snapshot => {
                    // 实时推送到数据浮窗
                    editorController?.PushFloatData(snapshot);
                });
            // 采样前隐藏覆盖层窗口，避免其混入捕获影响取色；采样后恢复。
            // 仅当存在颜色采样器且未在编辑覆盖层时才隐藏，避免闪烁
            monitor.BeforeSample = async () => {
                if (HasColorCollector(game) && !(editorController?.IsOverlayEditing() ?? false))
                {
                    editorController?.HideOverlayForSample();
                    await System.Threading.Tasks.Task.Delay(50); // 等待合成器移除覆盖层，避免取样到覆盖层
                }
                return true;
            };
            monitor.AfterSample = () => { editorController?.RestoreOverlayForSample(); return Task.CompletedTask; };
            activeMonitor = monitor;

            string monitorGameName = game.GameName;
            // 每步从全局配置目录重读该游戏：编辑器/外部改动都立即生效
            monitor.Start(() => Store.GetGame(monitorGameName));

            // 启动陪玩时确保数据浮窗显示（入口常驻）
            _ = editorController?.ShowFloatAsync();

            // 识别明细仅写入日志，供排查配置；仅告知 AI 已开始陪玩
            logger.LogInformation("[陪玩启动] {Detail}", BuildStartMessage(game));
            interactor.Poke(
                $"已开始陪玩「{game.GameName}」。请像真人观众一样观战，在合适的时机自然地插话评论（如高光、险境、局势转折），用 speak 发声；避免流水账式地复述每条数据，只在值得说的时候开口。");
        }
        return Task.CompletedTask;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("打开陪玩配置编辑器")]
    public Task OpenCompanionEditor()
    {
        OpenEditor();
        interactor.Poke("已打开陪玩配置编辑器。");
        return Task.CompletedTask;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("停止当前游戏陪玩监测")]
    public Task StopCompanion()
    {
        lock (monitorLock)
        {
            if (activeMonitor == null)
            {
                interactor.Poke("当前没有正在陪玩的游戏。");
                return Task.CompletedTask;
            }

            activeMonitor.Stop();
            activeMonitor = null;
            // 浮窗为唯一入口，停止陪玩时保持常驻
            interactor.Poke("已停止陪玩，陪玩浮窗仍保持显示。");
        }
        return Task.CompletedTask;
    }

    /// <summary>更新循环（框架逐帧驱动）：后台执行单步采样，避免截屏/OCR 拖死更新泵。</summary>
    /// <summary>当前游戏是否含启用中的颜色采样器（点/矩形/三角/扇面取色）。</summary>
    static bool HasColorCollector(GameConfig game)
        => game.Collectors.Any(c => c.IsEnable && c is BDFFZI.VibeCode.GameCompanion.PixelCollectorConfig);

    protected override Task OnUpdate()
    {
        GameMonitor? monitor;
        lock (monitorLock)
            monitor = activeMonitor;
        if (monitor == null)
            return Task.CompletedTask;

        // 忙标志：上一帧未结束则跳过（防堆叠）；超时看门狗：卡住 15s 视为死步，允许重试
        if (stepping)
        {
            if (Environment.TickCount64 - stepStartedAt > 15000)
            {
                Console.WriteLine("[Companion] 步进超时，重置忙标志");
                stepping = false;
            }
            else
            {
                return Task.CompletedTask;
            }
        }

        // 限频：每次采样间隔至少 500ms，减缓覆盖层闪烁并降低资源占用
        if (Environment.TickCount64 - lastStepAt < 500)
            return Task.CompletedTask;
        lastStepAt = Environment.TickCount64;

        stepping = true;
        stepStartedAt = Environment.TickCount64;
        _ = Task.Run(async () => {
            try
            {
                await monitor.StepAsync(DestroyCancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "陪玩步进异常");
            }
            finally
            {
                stepping = false;
            }
        });
        return Task.CompletedTask;
    }

    // ============ 编辑器支持 ============

    /// <summary>开关指定游戏的某个采样项（浮窗快速开关用；下一监控周期自动生效）。</summary>
    void SetCollectorEnabled(string gameName, string collectName, bool enable)
    {
        GameConfig? game = Store.ListGames()
            .FirstOrDefault(g => string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase));
        CollectConfigBase? config = game?.Collectors.FirstOrDefault(c => c.Name == collectName);
        if (game == null || config == null)
            return;
        config.IsEnable = enable;
        Store.SaveGame(game);
        logger.LogInformation("[陪玩开关] {Game}::{Name} → {Enable}", gameName, collectName, enable);
    }

    /// <summary>启动配置编辑器（由模块页按钮调用，复用 Alife.Client 的 Electron 环境）。</summary>
    public void OpenEditor()
    {
        if (editorController == null)
        {
            logger.LogError("配置编辑器控制器未初始化");
            return;
        }
        _ = editorController.OpenAsync();
    }

    /// <summary>
    /// 构建启动陪玩时的汇报文案（写入日志）：告知本次会采集哪些数据（按采样器类型归类）。
    /// </summary>
    static string BuildStartMessage(GameConfig game)
    {
        // 按采样器类型显示名归类采集项（展示用）
        var grouped = new Dictionary<string, List<string>>();
        foreach (CollectConfigBase c in game.Collectors)
        {
            if (!CollectorRegistry.IsValid(c))
                continue;
            string sampler = CollectorRegistry.TypeName(c);
            string displayName = CollectorRegistry.DisplayName(sampler);
            if (!grouped.TryGetValue(displayName, out List<string>? list))
                grouped[displayName] = list = new List<string>();
            list.Add(c.Name);
        }

        var detail = new List<string>();
        foreach ((string displayName, List<string> names) in grouped)
            detail.Add($"{string.Join("、", names)}（{displayName}）");

        string collectDesc = detail.Count > 0 ? string.Join("；", detail) : "未配置采集目标";
        return $"已开始陪玩「{game.GameName}」。数据有效且有数值变动时实时上报。本次将采集：{collectDesc}。";
    }

    protected override Task OnDestroy()
    {
        UnsubscribeAlcUnloading();
        lock (monitorLock)
        {
            activeMonitor?.Stop();
            activeMonitor = null;
        }
        editorController?.Dispose();
        editorController = null;
        return Task.CompletedTask;
    }

    // 订阅插件 ALC 卸载：插件重载/卸载必然卸载旧 ALC，这里兜底回收（正常 OnDestroy 也会执行，互不冲突）
    void SubscribeAlcUnloading()
    {
        try
        {
            var alc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(GameCompanionModule).Assembly);
            if (alc == null)
                return;
            if (pluginAlc != null && !ReferenceEquals(pluginAlc, alc))
                UnsubscribeAlcUnloading();
            pluginAlc = alc;
            alcUnloadingHandler = OnPluginAlcUnloading;
            alc.Unloading += alcUnloadingHandler;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companion] 订阅 ALC 卸载失败: {ex.Message}");
        }
    }

    void UnsubscribeAlcUnloading()
    {
        try
        {
            if (pluginAlc != null && alcUnloadingHandler != null)
            {
                pluginAlc.Unloading -= alcUnloadingHandler;
                pluginAlc = null;
                alcUnloadingHandler = null;
            }
        }
        catch { }
    }

    void OnPluginAlcUnloading(System.Runtime.Loader.AssemblyLoadContext context)
    {
        _ = context;
        try
        {
            lock (monitorLock)
            {
                activeMonitor?.Stop();
                activeMonitor = null;
            }
        }
        catch (Exception e) { Console.WriteLine($"[Companion] 卸载时停止监控异常: {e.Message}"); }
        try
        {
            editorController?.Dispose();
            editorController = null;
        }
        catch (Exception e) { Console.WriteLine($"[Companion] 卸载时释放编辑器异常: {e.Message}"); }
    }
}
