using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using ElectronNET.API.Entities;

namespace Alife.Function.DeskPet;

/// <summary>
/// 鼠标穿透模块：在 UI 角落提供一个圆形锁图标（根据窗口在屏幕的左右半区显示在左上或右上）。
/// 点击锁图标切换鼠标穿透。穿透开启时整个窗口对鼠标透明（点击穿透到后台），
/// 但光标落在锁图标或标记了 data-no-through 的 UI（如输入框）上时会临时恢复鼠标响应：
/// 锁图标始终可点击关闭、输入框悬停即显示且可点击使用，移开后恢复穿透。
/// 因为穿透状态下渲染进程收不到鼠标事件，由本模块把光标坐标推给渲染进程，
/// 命中测试在渲染进程内完成并回报结果（见 JsCode 中的 pointer_at）。
/// </summary>
public class MouseThroughModule : IPetModule, IDisposable
{
    public string CssCode => @"
#lock-btn {
    position:fixed; top:8px;
    width:28px; height:28px;
    background:rgba(0,0,0,0.4);
    backdrop-filter:blur(10px); border-radius:50%;
    display:flex; justify-content:center; align-items:center;
    color:white; cursor:pointer; z-index:2100;
    box-shadow:0 4px 10px rgba(0,0,0,0.2);
    border:1px solid rgba(255,255,255,0.2);
    opacity:0; transition:opacity 0.3s, background 0.3s, border-color 0.3s;
}
body:hover #lock-btn { opacity:1; }
body:hover #lock-btn.faded { opacity:0.6; }
#lock-btn.left { left:8px; }
#lock-btn.right { right:8px; }
";
    public string HtmlCode => @"
<div id='lock-btn' class='right' data-no-through title='鼠标穿透'>
    <svg id='lock-open' viewBox='0 0 24 24' width='16' height='16' fill='currentColor'>
        <path d='M12 17c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm6-9h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6h2c0-1.66 1.34-3 3-3s3 1.34 3 3v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2z'/>
    </svg>
    <svg id='lock-closed' viewBox='0 0 24 24' width='16' height='16' fill='currentColor' style='display:none'>
        <path d='M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z'/>
    </svg>
</div>
";
    public string JsCode => @"
(function() {
    var btn = document.getElementById('lock-btn');
    function corner() {
        var isLeft = window.screenX < (screen.width - window.innerWidth) / 2;
        btn.classList.toggle('right', isLeft);
        btn.classList.toggle('left', !isLeft);
    }
    function setState(on) {
        btn.classList.toggle('faded', on);
        document.getElementById('lock-open').style.display = on ? 'none' : 'block';
        document.getElementById('lock-closed').style.display = on ? 'block' : 'none';
    }
    btn.addEventListener('click', function() { postMessage({type:'lock_toggle'}); });
    messageBus.on('lock_state', function(msg) {
        setState(!!msg.on);
        if (!msg.on) {
            clearHover();
            window.__hitInteractive = null;
            window.__hitOverModel = null;
        }
    });
    window.addEventListener('resize', corner);
    setInterval(corner, 500);
    corner();

    var hovered = null;
    function clearHover() {
        if (hovered) {
            hovered.classList.remove('passthrough-hover');
            hovered = null;
        }
    }
    //主进程只推送屏幕光标坐标（与 screen.getCursorScreenPoint 同一坐标空间），
    //命中测试全部在渲染进程内完成并回报结果，主进程无需用 executeJavaScript 反向询问，因此不存在消息串扰。
    messageBus.on('pointer_at', function(msg) {
        var x = msg.sx - window.screenX;
        var y = msg.sy - window.screenY;
        var inside = x >= 0 && y >= 0 && x <= window.innerWidth && y <= window.innerHeight;
        var el = inside ? document.elementFromPoint(x, y) : null;
        var hit = (el && el.closest('[data-no-through]')) || null;
        if (hovered !== hit) {
            clearHover();
            if (hit) {
                hit.classList.add('passthrough-hover');
                hovered = hit;
            }
        }
        //交互 UI 优先：光标停在锁图标/输入框等 UI 上时不参与角色淡出判定。
        //此时输入框正因悬停而浮现，角色若同时淡出会显得杂乱，保持不透明观感更好。
        //注意这是刻意的行为策略而非解耦所需——若希望「只要在角色身上就淡出」，
        //去掉下面的 !interactive && 即可，其余逻辑无需改动。
        var interactive = !!hit;
        var overModel = !interactive && inside
            && typeof window.isOverModelAt === 'function' && window.isOverModelAt(x, y);
        if (interactive === window.__hitInteractive && overModel === window.__hitOverModel) return;
        window.__hitInteractive = interactive;
        window.__hitOverModel = overModel;
        postMessage({type:'pointer_hit', interactive: interactive, overModel: overModel});
    });
    postMessage({type:'lock_ready'});
})();
";

    /// <summary>当前是否处于鼠标穿透（锁定）状态。</summary>
    public bool IsMouseThrough => mouseThrough;

    /// <summary>锁定状态下光标是否落在角色身上（交互 UI 上为 false）。
    /// 由渲染进程的 <c>pointer_hit</c> 上报直接给出，仅供 <see cref="HoverFadeModule"/> 消费，
    /// 与本模块的穿透判定互不相关。</summary>
    public bool IsCursorOverModel { get; private set; }

    readonly PetBridge bridge;
    readonly PetWindow window;
    CancellationTokenSource? cancellationTokenSource;
    bool mouseThrough;

    /// <summary>光标移动时立即推送以保证跟手；该间隔只用于角色动作带动命中区域变化时刷新状态。</summary>
    const int IdlePushIntervalMs = 200;

    public MouseThroughModule(PetBridge bridge, PetWindow window)
    {
        this.bridge = bridge;
        this.window = window;
        bridge.OnMessage += OnBridgeMessage;
        window.MouseMoved += PushPointerPosition;
    }
    public void Dispose()
    {
        bridge.OnMessage -= OnBridgeMessage;
        window.MouseMoved -= PushPointerPosition;
        StopLoop();
    }

    void OnBridgeMessage(string type, JsonElement data)
    {
        switch (type)
        {
            case "lock_ready":
                bridge.SendMessage("lock_state", new { on = mouseThrough });
                //重载页面后若仍处于锁定态需要重新建立推送，否则收不到 pointer_hit
                if (mouseThrough)
                    StartLoop();
                break;
            case "lock_toggle":
                SetMouseThrough(!mouseThrough);
                break;
            case "pointer_hit":
                //解锁后可能仍有在途的回报，忽略以免把穿透状态错误地打开
                if (mouseThrough == false)
                    break;
                if (data.TryGetProperty("interactive", out JsonElement interactiveProp)
                    && data.TryGetProperty("overModel", out JsonElement overModelProp))
                {
                    //光标在锁图标/输入框等交互 UI 上时临时恢复鼠标响应
                    window.Window.SetIgnoreMouseEvents(!interactiveProp.GetBoolean());
                    IsCursorOverModel = overModelProp.GetBoolean();
                }
                break;
        }
    }

    void SetMouseThrough(bool enabled)
    {
        mouseThrough = enabled;
        bridge.SendMessage("lock_state", new { on = mouseThrough });
        if (enabled)
            StartLoop();
        else
        {
            StopLoop();
            IsCursorOverModel = false;
            window.Window.SetIgnoreMouseEvents(false);
        }
    }

    void StartLoop()
    {
        if (cancellationTokenSource != null)
            return;
        cancellationTokenSource = new CancellationTokenSource();
        PointerPushLoop(cancellationTokenSource.Token);
    }
    void StopLoop()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
    }

    async void PointerPushLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                PushPointerPosition();
                await Task.Delay(IdlePushIntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }

    /// <summary>只推送屏幕光标坐标；命中测试与结果回报都在渲染进程内完成，
    /// 单向推送 + 按需回报，不存在主进程反复执行脚本导致的消息串扰。</summary>
    void PushPointerPosition()
    {
        if (mouseThrough == false)
            return;
        Point cursor = window.CursorScreenPoint;
        bridge.SendMessage("pointer_at", new { sx = cursor.X, sy = cursor.Y });
    }
}
