using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.GameCompanion.Collector;
using Alife.Function.GameCompanion.Screen;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Alife.Function.GameCompanion.Monitoring;

/// <summary>陪玩监控状态。</summary>
public enum MonitorState
{
    /// <summary>未在监控。</summary>
    Stopped = 0,

    /// <summary>监控运行中。</summary>
    Running = 1
}

/// <summary>
/// 游戏陪玩监控器：由框架（ChatBehaviour.Update 循环）逐帧调用 <see cref="StepAsync"/> 驱动。
/// 每步：刷新采样器 → 并行 Update → 全部「验证采样器」(IsValidator) 有效才进入上报。
/// 防抖以采样器为单位（各采样器配置 DebounceSeconds）：采样器产出新值后，距其更新时间满足防抖
/// 且值与上次推送不同（非 null）才推送；推送成功后调用采样器 <see cref="CollectorBase.Use"/> 消费触发值。
/// 若 AI 正在对话（report 返回 false），则保留本次待推状态，等 AI 空闲后重试。
/// 框架完全不感知采样器具体类型：只把它们当作「每周期产出格式化字符串」的单元。
/// </summary>
public sealed class GameMonitor
{
    sealed class CollectorEntry
    {
        public required string Signature;
        public required CollectorState State;
    }

    readonly ILogger logger;
    readonly Func<string, bool> report;
    readonly Func<string, bool> reportForce;
    readonly Action<string> announce;
    readonly Action<MonitorSnapshot>? onData;
    readonly Dictionary<string, CollectorEntry> cachedCollectors = new();
    Func<GameConfig?> getGame = () => null;
    List<CollectorState>? currentStates;
    volatile bool paused;

    /// <summary>采样前隐藏遮挡窗口（如覆盖层），返回是否继续；采样后恢复。由模块注入。</summary>
    public Func<Task<bool>>? BeforeSample;
    public Func<Task>? AfterSample;

    /// <summary>当前监控状态。</summary>
    public MonitorState State { get; private set; } = MonitorState.Stopped;

    /// <summary>是否已暂停监控。</summary>
    public bool IsPaused => paused;

    /// <summary>暂停或继续监控（恢复后沿用已采集值）。</summary>
    public void SetPaused(bool value)
    {
        paused = value;
        PushData();
    }

    /// <summary>正在监控的游戏名。</summary>
    public string GameName { get; private set; } = "";

    public GameMonitor(
        ILogger logger,
        Func<string, bool> report,
        Action<string> announce,
        Action<MonitorSnapshot>? onData = null,
        Func<string, bool>? reportForce = null)
    {
        this.logger = logger;
        this.report = report;
        this.announce = announce;
        this.onData = onData;
        this.reportForce = reportForce ?? report;
    }

    /// <summary>开始监控一个游戏（由模块 StartCompanion 调用）。</summary>
    public void Start(Func<GameConfig?> getGame)
    {
        GameConfig? game = getGame();
        if (game == null)
            return;
        this.getGame = getGame;
        GameName = game.GameName;
        State = MonitorState.Running;
        currentStates = null;
        PushData();
    }

    /// <summary>停止监控并释放采样器（由模块 StopCompanion/OnDestroy 调用）。</summary>
    public void Stop()
    {
        foreach (CollectorEntry entry in cachedCollectors.Values)
            DisposeSafe(entry.State.Collector);
        cachedCollectors.Clear();
        State = MonitorState.Stopped;
        GameName = "";
        currentStates = null;
        PushData();
    }

    /// <summary>
    /// 单步采样（由 ChatBehaviour.OnUpdate 循环驱动）：
    /// 采样 → 验证门 → 逐采样器防抖到期推送。
    /// </summary>
    public async Task StepAsync(CancellationToken ct)
    {
        try
        {
            await StepCoreAsync(ct);
        }
        catch (OperationCanceledException)
{
            // 正常取消
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "陪玩采样单步异常");
            // 异常不中断运行：仍推送当前状态到浮窗，下一帧继续
            PushData();
        }
    }

    async Task StepCoreAsync(CancellationToken ct)
    {
        if (paused)
        {
            PushData();
            return;
        }

        GameConfig? game = getGame();
        if (game == null || !string.Equals(game.GameName, GameName, StringComparison.OrdinalIgnoreCase))
        {
            // 配置被删除或游戏被改名：终止本监控
            announce?.Invoke($"「{GameName}」的陪玩配置已变更，停止监测");
            Stop();
            return;
        }

        // 从最新配置刷新采样器（配置未变则复用，共享服务保持存活）
        var states = RebuildCollectors(game);
        currentStates = states;

        try
        {
            // 采样前隐藏遮挡窗口（覆盖层），避免其混入捕获画面影响取色
            if (BeforeSample != null && !await BeforeSample())
            {
                PushData();
                return;
            }
            using var frame = await ScreenFrame.CaptureFullscreenAsync();
            if (frame == null)
            {
                logger.LogWarning("陪玩监控: 屏幕捕获失败");
                PushData();
                return;
            }
            var ctx = new GameContext { Frame = frame };

            // 构建名称→状态查找表（供前置采样器判断）
            var stateByName = new Dictionary<string, CollectorState>(StringComparer.Ordinal);
            foreach (CollectorState state in states)
                stateByName[state.Collector.Config.Name] = state;

            // 按配置查找表（供前置采样器判断）
            var configByName = new Dictionary<string, CollectConfigBase>(StringComparer.Ordinal);
            foreach (CollectConfigBase c in game.Collectors)
                configByName[c.Name] = c;

            // 更新采样器：有前置采样器的，仅当前置有效时才执行 Update
            foreach (CollectorState state in states)
            {
                if (configByName.TryGetValue(state.Collector.Config.Name, out CollectConfigBase? cfg)
                    && !string.IsNullOrEmpty(cfg.Prerequisite)
                    && stateByName.TryGetValue(cfg.Prerequisite, out CollectorState? prereq)
                    && prereq.CurrentValue is null)
                {
                    continue; // 前置无效，跳过更新
                }
                await state.Collector.Update(ctx, ct);
            }

            DateTime now = DateTime.UtcNow;

            // 追踪每个采样器的当前值（值变化时自动刷新防抖计时）
            foreach (CollectorState state in states)
                state.TrackCurrentValue();

        // 仅当全部「验证采样器」(IsValidator) 有效才进入上报流程
        var due = new List<CollectorState>();
        if (AllValidatorsValid(states, game))
        {
            foreach (CollectorState state in states)
            {
                if (state.CurrentValue is null)
                    continue;
                // 前置采样器无效时，不推送
                if (configByName.TryGetValue(state.Collector.Config.Name, out CollectConfigBase? pcfg)
                    && !string.IsNullOrEmpty(pcfg.Prerequisite)
                    && stateByName.TryGetValue(pcfg.Prerequisite, out CollectorState? prereqState)
                    && prereqState.CurrentValue is null)
                {
                    continue;
                }
                if (state.IsDue(now))
                    due.Add(state);
            }
        }

        var dropExpired = new List<CollectorState>();   // 过期且非强制 → 静默更新 PushedValue，不推送
        var forceNow = new List<CollectorState>();      // 过期且强制 → 过期时才 Chat 打断推送
        var normalNow = new List<CollectorState>();     // 未过期（防抖已过）→ 普通 Poke 推送，过期时间内等待
        foreach (CollectorState state in due)
        {
            string? cur = state.CurrentValue;
            if (cur is null || cur == state.PushedValue)
                continue;
            bool force = configByName.TryGetValue(state.Collector.Config.Name, out CollectConfigBase? cc) && cc.ForcePush;
            double expire = configByName.TryGetValue(state.Collector.Config.Name, out cc) ? cc.ExpireSeconds : 0;
            double elapsed = (now - new DateTime(state.LastUpdateTimeTicks)).TotalSeconds;
            bool expired = elapsed >= Math.Max(0, state.Collector.Config.DebounceSeconds) + Math.Max(0, expire);

            // 过期时刻才区分：强制→打断推送；非强制→静默丢弃。过期时间内都走普通等待推送
            if (expired)
            {
                if (force) forceNow.Add(state);
                else dropExpired.Add(state);
            }
            else
            {
                normalNow.Add(state);
            }
        }

        // 1) 过期且非强制：静默更新 PushedValue，不打扰 AI
        foreach (CollectorState state in dropExpired)
            state.OnPushed(state.CurrentValue, now);

        // 2) 有强制推送项：连同普通项一起用 Chat 打断推送
        if (forceNow.Count > 0)
        {
            var all = new List<CollectorState>(forceNow);
            all.AddRange(normalNow);
            var parts = new List<string>(all.Count);
            foreach (CollectorState state in all)
                parts.Add(FormatForPush(state));
            string message = string.Join("；", parts);
            if (reportForce(message))
            {
                foreach (CollectorState state in all)
                    state.OnPushed(state.CurrentValue, now);
            }
        }
        // 3) 仅普通项：Poke 推送（对话占用时 report 返回 false，保留待推）
        else if (normalNow.Count > 0)
        {
            var parts = new List<string>(normalNow.Count);
            foreach (CollectorState state in normalNow)
                parts.Add(FormatForPush(state));
            string message = string.Join("；", parts);
            if (report(message))
            {
                foreach (CollectorState state in normalNow)
                    state.OnPushed(state.CurrentValue, now);
            }
        }

        PushData();
        }
        finally
        {
            if (AfterSample != null)
                await AfterSample();
        }
    }

    /// <summary>推送消息片段：{Name}: {CurrentValue}。</summary>
    static string FormatForPush(CollectorState state)
    {
        string? cur = state.CurrentValue;
        return cur is null ? "" : $"{state.Collector.Config.Name}: {cur}";
    }

    /// <summary>验证门：作为「数据验证器」的采样器全部有当前值（CurrentValue != null）才允许上报。</summary>
    static bool AllValidatorsValid(List<CollectorState> states, GameConfig game)
    {
        // 名字 → 是否数据验证器
        var validatorNames = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (CollectConfigBase config in game.Collectors)
            validatorNames[config.Name] = config.IsValidator;

        foreach (CollectorState state in states)
        {
            if (validatorNames.TryGetValue(state.Collector.Config.Name, out bool isValidator) && isValidator && state.CurrentValue is null)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 根据最新配置刷新采样器：配置签名未变则复用旧实例，否则释放重建；
    /// 移除的配置一并释放（采样器内部共享服务随之回收）。
    /// </summary>
    List<CollectorState> RebuildCollectors(GameConfig game)
    {
        var builders = new List<CollectorState>();
        var next = new Dictionary<string, CollectorEntry>(StringComparer.Ordinal);

        foreach (CollectConfigBase config in game.Collectors)
        {
            CollectorBase? collector = CollectorRegistry.Create(config);
            if (collector == null)
                continue;

            string signature = SignatureOf(config);
            if (cachedCollectors.TryGetValue(config.Name, out CollectorEntry? old) && old.Signature == signature)
            {
                // 配置未变：复用旧实例（含其状态）
                next[config.Name] = old;
                builders.Add(old.State);
            }
            else
            {
                if (old != null)
                    DisposeSafe(old.State.Collector);
                var state = new CollectorState { Collector = collector };
                next[config.Name] = new CollectorEntry { Signature = signature, State = state };
                builders.Add(state);
            }
        }

        // 释放已移除配置对应实例
        foreach (CollectorEntry entry in cachedCollectors.Values)
        {
            if (!next.ContainsKey(entry.State.Collector.Config.Name))
                DisposeSafe(entry.State.Collector);
        }

        cachedCollectors.Clear();
        foreach (KeyValuePair<string, CollectorEntry> pair in next)
            cachedCollectors[pair.Key] = pair.Value;

        return builders;
    }

    static string SignatureOf(CollectConfigBase config)
    {
        string sampler = CollectorRegistry.TypeName(config);
        string data = JsonConvert.SerializeObject(config, Formatting.None);
        return $"{sampler}|{data}";
    }

    static void DisposeSafe(CollectorBase collector)
    {
        try { if (collector is IDisposable d) d.Dispose(); } catch (Exception ex) { _ = ex; }
    }

    void PushData()
    {
        if (onData == null)
            return;
        try
        {
            var values = new Dictionary<string, string?>();
            var raws = new Dictionary<string, string?>();
            var currents = new Dictionary<string, string?>();
            var pusheds = new Dictionary<string, string?>();
            var progress = new Dictionary<string, double>();
            var debounceSecs = new Dictionary<string, double>();
            var updateTicks = new Dictionary<string, long>();
            var expireSecs = new Dictionary<string, double>();
            var enabled = new Dictionary<string, bool>();

            // 列出配置中全部采样项（含禁用项，供浮窗开关）；值仅取当前运行中的有效采样器
            GameConfig? game = getGame();
            var cfgByName = new Dictionary<string, CollectConfigBase>(StringComparer.Ordinal);
            if (game != null)
            {
                foreach (CollectConfigBase config in game.Collectors)
                {
                    enabled[config.Name] = config.IsEnable;
                    cfgByName[config.Name] = config;
                }
            }
            if (currentStates != null)
            {
                DateTime now = DateTime.UtcNow;
                foreach (CollectorState state in currentStates)
                {
                    values[state.Collector.Config.Name] = state.Collector.Value;
                    raws[state.Collector.Config.Name] = state.Collector.DebugValue;
                    currents[state.Collector.Config.Name] = state.CurrentValue;
                    pusheds[state.Collector.Config.Name] = state.PushedValue;
                    // 仅当有待推内容（CurrentValue 与已推送不同）才显示进度；推后归零消失
                    bool pending = state.CurrentValue is not null && state.CurrentValue != state.PushedValue;
                    progress[state.Collector.Config.Name] = pending ? state.DebounceProgress(now) : 0;
                    debounceSecs[state.Collector.Config.Name] = state.Collector.Config.DebounceSeconds;
                    updateTicks[state.Collector.Config.Name] = state.LastUpdateTimeTicks;
                    // 过期时长（前端据此做防抖之后的平滑过期动画）
                    double expire = cfgByName.TryGetValue(state.Collector.Config.Name, out CollectConfigBase? cc) ? cc.ExpireSeconds : 0;
                    expireSecs[state.Collector.Config.Name] = Math.Max(0, expire);
                }
            }

            onData(new MonitorSnapshot {
                GameName = GameName,
                State = State,
                Enabled = enabled,
                Values = values,
                DebugValues = raws,
                CurrentValues = currents,
                PushedValues = pusheds,
                DebounceProgress = progress,
                DebounceSeconds = debounceSecs,
                LastUpdateTimeTicks = updateTicks,
                ExpireSeconds = expireSecs,
                IsPaused = paused
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "推送陪玩数据到浮窗失败");
        }
    }
}
