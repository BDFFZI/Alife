using System;
using System.Text.Json;
using Alife.Foundation;
using Alife.Function.Speech;

namespace Alife.Function.DeskPet;

/// <summary>
/// 说话口型模块：噪声口型与真音量口型叠加，真音量在播放期间优先覆盖噪声。
/// 噪声口型跟随字幕气泡（由 SubtitleModule 判定），字幕出现即有，保证开场/交互动画等无音频场景也有口型；
/// 真音量口型由 <see cref="ISpeechService"/> 的播放事件经 <see cref="PetBridge"/> 下发前端包络驱动，
/// 音频播放期间覆盖噪声，停止后交还噪声。是否注入语音服务由 <see cref="Live2DDeskPetConfig.SpeechLipSyncEnabled"/> 控制。
/// 总开关见 <see cref="Live2DDeskPetConfig.MouthEnabled"/>（默认打开）；
/// 只认模型配置里声明的口型参数（model3.json Groups 中 Target=Parameter、Name=LipSync 的 Ids），
/// 没有声明的模型不会发送口型消息。
/// 写入点选在模型每帧送渲染之前（beforeModelUpdate）：此时待机动作/表情/物理都已写完，
/// 我们的写入是本帧最后一次，不会被盖掉。ticker 写入仅作兜底。
/// </summary>
public class MouthModule : IPetModule, IDisposable
{
    public string? JsCode => metadata.LipSyncParameterIds.Count == 0 ? null : @"
(function() {
    var lip = { active: false, ids: [], startAt: 0, tick: null, hooked: null };
    // 真音频包络状态：有语音服务时由 mouth_envelope 消息驱动，本帧优先覆盖噪声
    var env = { active: false, data: [], windowMs: 40, startAt: 0, disp: 0, silent: 0 };
    // 真音量口型幅度系数：调小则张嘴幅度更小（相对噪声口型收一点）
    var ENV_SCALE = 0.9;

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
    // 噪声口型取值：取模避免运行数小时后大数精度退化；每小时一次的跳变对嘴部不可见
    function noiseValue() {
        var t = (performance.now() / 1000 - lip.startAt) % 3600;
        if (t < 0) t += 3600;
        return mouthOpen(t);
    }
    // 真音量查表：elapsed 对齐播放起点，静音帧快速归零避免换气处嘴还在动
    function envelopeValue() {
        var elapsed = Date.now() - env.startAt;
        var idx = Math.floor(elapsed / env.windowMs);
        var target = (idx >= 0 && idx < env.data.length) ? env.data[idx] : 0;
        if (target < 0) target = 0;
        if (target > 1) target = 1;
        var k;
        if (target < 0.02) {
            env.silent++;
            k = env.silent >= 2 ? 0.55 : 0.4;
        } else {
            env.silent = 0;
            k = target > env.disp ? 0.35 : 0.28;
        }
        env.disp += (target - env.disp) * k;
        if (env.disp < 0.005) env.disp = 0;
        return env.disp * ENV_SCALE;
    }
    // 本帧口型值：有真音频时用包络覆盖噪声，否则退回噪声；都未激活则返回 null 不写
    function frameValue() {
        if (env.active) return envelopeValue();
        if (lip.active) return noiseValue();
        return null;
    }
    function writeNow(value) {
        var core = coreModel();
        if (!core) return;
        for (var i = 0; i < lip.ids.length; i++) writeValue(core, lip.ids[i], value);
    }
    function onBeforeModelUpdate() {
        var v = frameValue();
        if (v !== null) writeNow(v);
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
    // 确保每帧写入器在跑（噪声或包络任一激活即可），不重置噪声相位
    function ensureTick() {
        if (lip.tick) return;
        var core = coreModel();
        if (!core || lip.ids.length === 0) return;
        attachHook();
        lip.tick = function() {
            if (!lip.active && !env.active) return;
            // 模型被替换后重新挂钩；事件钩子若始终没挂上，这里作为兜底写入
            try {
                var im = (typeof model !== 'undefined' && model) ? model.internalModel : null;
                if (im !== lip.hooked) attachHook();
            } catch (e) {}
            var v = frameValue();
            if (v !== null) writeNow(v);
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

    // 噪声口型：跟随字幕气泡（原版行为），无音频时也始终有效
    messageBus.on('mouth_speaking', function(msg) {
        var on = !!msg.on;
        if (msg.ids && msg.ids.length) lip.ids = msg.ids;
        if (on) {
            if (!lip.active) lip.startAt = performance.now() / 1000;
            lip.active = true;
            ensureTick();
        } else {
            lip.active = false;
            if (!env.active) stopTick();
        }
    });
    // 真音频包络：播放期间优先覆盖噪声，结束后交还噪声
    messageBus.on('mouth_envelope', function(msg) {
        var on = !!msg.on;
        if (msg.ids && msg.ids.length) lip.ids = msg.ids;
        if (on) {
            env.data = msg.envelope || [];
            env.windowMs = msg.windowMs || 40;
            env.startAt = msg.startAt || Date.now();
            env.disp = 0;
            env.silent = 0;
            env.active = true;
            ensureTick();
        } else {
            env.active = false;
            env.disp = 0;
            if (lip.active) return; // 噪声兜底，本帧起由 noiseValue 继续
            stopTick();
            writeNow(0);
        }
    });
})();
";

    readonly PetBridge bridge;
    readonly PetModelMetadata metadata;
    readonly Live2DDeskPetConfig config;
    readonly SubtitleModule subtitleModule;
    readonly ISpeechService? speechService;
    bool speaking;
    bool envelopeActive;

    public MouthModule(PetBridge bridge, PetModelMetadata metadata, Live2DDeskPetConfig config, SubtitleModule subtitleModule, IServiceProvider services)
    {
        this.bridge = bridge;
        this.metadata = metadata;
        this.config = config;
        this.subtitleModule = subtitleModule;
        speechService = services.GetService(typeof(ISpeechService)) as ISpeechService;
        // 噪声口型常驻：跟随字幕气泡，无音频场景（开场/交互动画）也有口型
        subtitleModule.Showed += StartSpeaking;
        subtitleModule.Hid += StopSpeaking;
        // 真音量口型：仅当注入语音服务时订阅，播放时覆盖噪声
        if (speechService != null)
        {
            speechService.AudioStarted += OnAudioStarted;
            speechService.AudioStopped += OnAudioStopped;
        }
        bridge.OnMessage += OnBridgeMessage;
    }
    public void Dispose()
    {
        subtitleModule.Showed -= StartSpeaking;
        subtitleModule.Hid -= StopSpeaking;
        if (speechService != null)
        {
            speechService.AudioStarted -= OnAudioStarted;
            speechService.AudioStopped -= OnAudioStopped;
        }
        bridge.OnMessage -= OnBridgeMessage;
    }

    void OnAudioStarted(SpeechAudioContext context)
    {
        if (config.MouthEnabled == false || metadata.LipSyncParameterIds.Count == 0 || context.Envelope.Length == 0)
            return;
        envelopeActive = true;
        bridge.SendMessage("mouth_envelope", new {
            on = true,
            ids = metadata.LipSyncParameterIds,
            envelope = context.Envelope,
            windowMs = context.WindowMs,
            startAt = context.StartUnixMs
        });
    }
    void OnAudioStopped()
    {
        if (envelopeActive == false)
            return;
        envelopeActive = false;
        bridge.SendMessage("mouth_envelope", new { on = false });
        // 音频结束但字幕可能仍在显示（字幕按文本长度停留）：一并停掉噪声口型，
        // 让嘴交回待机动作，避免音频结束后嘴巴继续乱动
        if (speaking)
            bridge.SendMessage("mouth_speaking", new { on = false });
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
