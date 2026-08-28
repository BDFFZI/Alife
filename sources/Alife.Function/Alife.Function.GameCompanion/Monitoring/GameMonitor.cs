using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.GameCompanion.Collectors;
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
        public required CollectorBase Instance;
    }

    readonly ILogger logger;
    readonly Func<string, bool> report;
    readonly Func<string, bool> reportForce;
    readonly Action<string> announce;
    readonly Action<MonitorSnapshot>? onData;
    readonly Dictionary<string, CollectorEntry> cachedCollectors = new();
    Func<GameConfig?> getGame = () => null;
    List<CollectorBase>? currentCollectors;
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
        currentCollectors = null;
        PushData();
    }

    /// <summary>停止监控并释放采样器（由模块 StopCompanion/OnDestroy 调用）。</summary>
    public void Stop()
    {
        foreach (CollectorEntry entry in cachedCollectors.Values)
            DisposeSafe(entry.Instance);
        cachedCollectors.Clear();
        State = MonitorState.Stopped;
        GameName = "";
        currentCollectors = null;
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
        var collectors = RebuildCollectors(game);
        currentCollectors = collectors;

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

            foreach (CollectorBase collector in collectors)
                await collector.Update(ctx, ct);

            DateTime now = DateTime.UtcNow;

            // 追踪每个采样器的当前值（值变化时自动刷新防抖计时）
            foreach (CollectorBase collector in collectors)
                collector.TrackCurrentValue();

        // 仅当全部「验证采样器」(IsValidator) 有效才进入上报流程
        var due = new List<CollectorBase>();
        if (AllValidatorsValid(collectors, game))
        {
            foreach (CollectorBase collector in collectors)
            {
                if (collector.CurrentValue is null)
                    continue;
                if (collector.IsDue(now))
                    due.Add(collector);
            }
        }

        // 每个采样器配置查找（用于过期/强制推送判断）
        var configByName = new Dictionary<string, CollectConfigBase>(StringComparer.Ordinal);
        foreach (CollectConfigBase c in game.Collectors)
            configByName[c.Name] = c;

        var dropExpired = new List<CollectorBase>();   // 过期且非强制 → 静默更新 PushedValue，不推送
        var forceNow = new List<CollectorBase>();      // 过期且强制 → 过期时才 Chat 打断推送
        var normalNow = new List<CollectorBase>();     // 未过期（防抖已过）→ 普通 Poke 推送，过期时间内等待
        foreach (CollectorBase collector in due)
        {
            string? cur = collector.CurrentValue;
            if (cur is null || cur == collector.PushedValue)
                continue;
            bool force = configByName.TryGetValue(collector.Name, out CollectConfigBase? cc) && cc.ForcePush;
            double expire = configByName.TryGetValue(collector.Name, out cc) ? cc.ExpireSeconds : 0;
            double elapsed = (now - new DateTime(collector.LastUpdateTimeTicks)).TotalSeconds;
            bool expired = elapsed >= Math.Max(0, collector.DebounceSeconds) + Math.Max(0, expire);

            // 过期时刻才区分：强制→打断推送；非强制→静默丢弃。过期时间内都走普通等待推送
            if (expired)
            {
                if (force) forceNow.Add(collector);
                else dropExpired.Add(collector);
            }
            else
            {
                normalNow.Add(collector);
            }
        }

        // 1) 过期且非强制：静默更新 PushedValue，不打扰 AI
        foreach (CollectorBase collector in dropExpired)
            collector.OnPushed(collector.CurrentValue, now);

        // 2) 有强制推送项：连同普通项一起用 Chat 打断推送
        if (forceNow.Count > 0)
        {
            var all = new List<CollectorBase>(forceNow);
            all.AddRange(normalNow);
            var parts = new List<string>(all.Count);
            foreach (CollectorBase collector in all)
                parts.Add(FormatForPush(collector));
            string message = string.Join("；", parts);
            if (reportForce(message))
            {
                foreach (CollectorBase collector in all)
                    collector.OnPushed(collector.CurrentValue, now);
            }
        }
        // 3) 仅普通项：Poke 推送（对话占用时 report 返回 false，保留待推）
        else if (normalNow.Count > 0)
        {
            var parts = new List<string>(normalNow.Count);
            foreach (CollectorBase collector in normalNow)
                parts.Add(FormatForPush(collector));
            string message = string.Join("；", parts);
            if (report(message))
            {
                foreach (CollectorBase collector in normalNow)
                    collector.OnPushed(collector.CurrentValue, now);
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
    static string FormatForPush(CollectorBase collector)
    {
        string? cur = collector.CurrentValue;
        return cur is null ? "" : $"{collector.Name}: {cur}";
    }

    /// <summary>验证门：作为「数据验证器」的采样器全部有当前值（CurrentValue != null）才允许上报。</summary>
    static bool AllValidatorsValid(List<CollectorBase> collectors, GameConfig game)
    {
        // 名字 → 是否数据验证器
        var validatorNames = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (CollectConfigBase config in game.Collectors)
            validatorNames[config.Name] = config.IsValidator;

        foreach (CollectorBase collector in collectors)
        {
            if (validatorNames.TryGetValue(collector.Name, out bool isValidator) && isValidator && collector.CurrentValue is null)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 根据最新配置刷新采样器：配置签名未变则复用旧实例，否则释放重建；
    /// 移除的配置一并释放（采样器内部共享服务随之回收）。
    /// </summary>
    List<CollectorBase> RebuildCollectors(GameConfig game)
    {
        var builders = new List<CollectorBase>();
        var next = new Dictionary<string, CollectorEntry>(StringComparer.Ordinal);

        foreach (CollectConfigBase config in game.Collectors)
        {
            CollectorBase? collector = CollectorRegistry.Create(config);
            if (collector == null)
                continue;

            string signature = SignatureOf(config);
            if (cachedCollectors.TryGetValue(config.Name, out CollectorEntry? old) && old.Signature == signature)
            {
                // 配置未变：复用旧实例
                collector = old.Instance;
            }
            else
            {
                if (old != null)
                    DisposeSafe(old.Instance);
            }
            next[config.Name] = new CollectorEntry { Signature = signature, Instance = collector };
            builders.Add(collector);
        }

        // 释放已移除配置对应实例
        foreach (CollectorEntry entry in cachedCollectors.Values)
        {
            if (!next.ContainsKey(entry.Instance.Name))
                DisposeSafe(entry.Instance);
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
        try { collector.Dispose(); } catch (Exception ex) { _ = ex; }
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
            if (currentCollectors != null)
            {
                DateTime now = DateTime.UtcNow;
                foreach (CollectorBase collector in currentCollectors)
                {
                    values[collector.Name] = collector.Value;
                    raws[collector.Name] = collector.DebugValue;
                    currents[collector.Name] = collector.CurrentValue;
                    pusheds[collector.Name] = collector.PushedValue;
                    // 仅当有待推内容（CurrentValue 与已推送不同）才显示进度；推后归零消失
                    bool pending = collector.CurrentValue is not null && collector.CurrentValue != collector.PushedValue;
                    progress[collector.Name] = pending ? collector.DebounceProgress(now) : 0;
                    debounceSecs[collector.Name] = collector.DebounceSeconds;
                    updateTicks[collector.Name] = collector.LastUpdateTimeTicks;
                    // 过期时长（前端据此做防抖之后的平滑过期动画）
                    double expire = cfgByName.TryGetValue(collector.Name, out CollectConfigBase? cc) ? cc.ExpireSeconds : 0;
                    expireSecs[collector.Name] = Math.Max(0, expire);
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