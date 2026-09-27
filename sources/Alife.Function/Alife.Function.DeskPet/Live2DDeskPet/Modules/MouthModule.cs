using System;
using System.Text.Json;
using Alife.Foundation;

namespace Alife.Function.DeskPet;

/// <summary>
/// 说话口型模块：字幕气泡显示期间按时间噪声连续张合，气泡消失后停下。
/// 开关见 <see cref="Live2DDeskPetConfig.MouthEnabled"/>（默认打开），运行时切换下次说话生效；
/// 只认模型配置里声明的口型参数（model3.json Groups 中 Target=Parameter、Name=LipSync 的 Ids），
/// 没有声明的模型不会发送口型消息。
/// 写入点选在模型每帧送渲染之前（beforeModelUpdate）：此时待机动作/表情/物理都已写完，
/// 我们的写入是本帧最后一次，不会被盖掉。ticker 写入仅作兜底。
/// 字幕的显示/隐藏状态由 SubtitleModule 判定（Show 刷新计时、超时自动 Hid），这里直接跟随。
/// </summary>
public class MouthModule : IPetModule, IDisposable
{
    public string? JsCode => metadata.LipSyncParameterIds.Count == 0 ? null : @"
(function() {
    var lip = { active: false, ids: [], startAt: 0, tick: null, hooked: null };

    function coreModel() {
        if (typeof model === 'undefined' || !model) return null;
        if (model.internalModel && model.internalModel.coreModel) return model.internalModel.coreModel;
        if (model.internalModel && typeof model.internalModel.setParameterValueById === 'function') return model.internalModel;
        return null;
    }
    function writeValue(core, id, value) {
        try {
            if (core && typeof core.setParameterValueById === 'function') {
                core.setParameterValueById(id, Math.max(0, Math.min(1, value)));
                return true;
            }
        } catch (e) {}
        return false;
    }
    function hash1(i) {
        var x = Math.sin(i * 127.1) * 43758.5453;
        return x - Math.floor(x);
    }
    // 一维值噪声：每 0.11 秒一个随机格点，smoothstep 插值
    function noise1(t) {
        var step = 0.11;
        var i = Math.floor(t / step);
        var f = t / step - i;
        var u = f * f * (3 - 2 * f);
        var a = hash1(i);
        return a + (hash1(i + 1) - a) * u;
    }
    function mouthOpen(t) {
        var v = 0.2 + 0.7 * noise1(t);
        return Math.max(0.2, Math.min(0.9, v));
    }
    function valueNow() {
        // 取模避免运行数小时后大数精度退化；每小时一次的跳变对嘴部不可见
        var t = (performance.now() / 1000 - lip.startAt) % 3600;
        if (t < 0) t += 3600;
        return mouthOpen(t);
    }
    function writeNow(value) {
        var core = coreModel();
        if (!core) return;
        for (var i = 0; i < lip.ids.length; i++) writeValue(core, lip.ids[i], value);
    }
    function onBeforeModelUpdate() {
        if (!lip.active) return;
        writeNow(valueNow());
    }
    function attachHook() {
        var im = null;
        try { im = (typeof model !== 'undefined' && model) ? model.internalModel : null; } catch (e) {}
        if (im === lip.hooked) return;
        var targets = [];
        try {
            if (typeof model !== 'undefined' && model && typeof model.on === 'function') targets.push(model);
            if (im && typeof im.on === 'function') targets.push(im);
        } catch (e) {}
        for (var i = 0; i < targets.length; i++) {
            try { targets[i].on('beforeModelUpdate', onBeforeModelUpdate); } catch (e) {}
        }
        lip.hooked = im;
        try { postMessage({type: 'mouth_hook', targets: targets.length}); } catch (e) {}
    }
    function stopTick() {
        try {
            if (lip.tick && app && app.ticker && typeof app.ticker.remove === 'function') app.ticker.remove(lip.tick);
        } catch (e) {}
        lip.tick = null;
    }
    function startTick() {
        var core = coreModel();
        if (!core || lip.ids.length === 0) return;
        lip.startAt = performance.now() / 1000;
        stopTick();
        attachHook();
        lip.tick = function() {
            if (!lip.active) return;
            // 模型被替换后重新挂钩；事件钩子若始终没挂上，这里作为兜底写入
            try {
                var im = (typeof model !== 'undefined' && model) ? model.internalModel : null;
                if (im !== lip.hooked) attachHook();
            } catch (e) {}
            writeNow(valueNow());
        };
        try {
            var priority = (typeof PIXI !== 'undefined' && PIXI.UPDATE_PRIORITY && typeof PIXI.UPDATE_PRIORITY.UTILITY !== 'undefined')
                ? PIXI.UPDATE_PRIORITY.UTILITY
                : -50;
            app.ticker.add(lip.tick, null, priority);
        } catch (e) {
            lip.tick = null;
        }
    }

    messageBus.on('mouth_speaking', function(msg) {
        var on = !!msg.on;
        if (msg.ids && msg.ids.length) lip.ids = msg.ids;
        if (on === lip.active && lip.tick) return;
        lip.active = on;
        if (on) startTick();
        else stopTick();
    });
})();
";

    readonly PetBridge bridge;
    readonly PetModelMetadata metadata;
    readonly Live2DDeskPetConfig config;
    readonly SubtitleModule subtitleModule;
    bool speaking;

    public MouthModule(PetBridge bridge, PetModelMetadata metadata, Live2DDeskPetConfig config, SubtitleModule subtitleModule)
    {
        this.bridge = bridge;
        this.metadata = metadata;
        this.config = config;
        this.subtitleModule = subtitleModule;
        subtitleModule.Showed += StartSpeaking;
        subtitleModule.Hid += StopSpeaking;
        bridge.OnMessage += OnBridgeMessage;
    }
    public void Dispose()
    {
        subtitleModule.Showed -= StartSpeaking;
        subtitleModule.Hid -= StopSpeaking;
        bridge.OnMessage -= OnBridgeMessage;
    }

    void StartSpeaking()
    {
        if (config.MouthEnabled == false || metadata.LipSyncParameterIds.Count == 0 || speaking)
            return;
        speaking = true;
        bridge.SendMessage("mouth_speaking", new { on = true, ids = metadata.LipSyncParameterIds });
    }

    void StopSpeaking()
    {
        if (speaking == false)
            return;
        speaking = false;
        bridge.SendMessage("mouth_speaking", new { on = false });
    }

    void OnBridgeMessage(string type, JsonElement data)
    {
        if (type != "mouth_hook")
            return;
        if (data.TryGetProperty("targets", out JsonElement targetsProp) == false)
            return;
        AlifeLog.LogInformation($"口型帧钩子已挂载：{targetsProp.GetInt32()} 个目标");
    }
}
