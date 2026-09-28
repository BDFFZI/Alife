using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using ElectronNET.API;
using ElectronNET.API.Entities;

namespace Alife.Function.DeskPet;

/// <summary>
/// 窗口置顶模块：在输入框开关按钮上方提供一个图钉开关（默认开，不持久化）。
/// 开关控制的是“是否周期性维持置顶”，而不是置顶属性本身：
/// 开启时每隔一段时间重新声明置顶，把被任务栏压下去的窗口拉回来；
/// 关闭后只是停掉维持循环，窗口保持现有层级（创建时自带置顶属性，不会被去掉）。
/// </summary>
public class TopmostModule : IPetModule, IDisposable
{
    public string CssCode => @"
#topmost-toggle-btn {
    position:fixed; right:15px; bottom:124px;
    width:28px; height:28px;
    background:rgba(0,0,0,0.4);
    backdrop-filter:blur(10px); border-radius:50%;
    display:flex; justify-content:center; align-items:center;
    color:white; cursor:pointer; z-index:2000;
    box-shadow:0 4px 10px rgba(0,0,0,0.2);
    border:1px solid rgba(255,255,255,0.15);
    opacity:0; transition:opacity 0.3s, background 0.3s, opacity 0.3s;
}
body:hover #topmost-toggle-btn { opacity:1; }
#topmost-toggle-btn:hover { background:rgba(0,0,0,0.6); }
#topmost-toggle-btn.faded {
    filter:grayscale(1);
    background:rgba(0,0,0,0.22);
    border-color:rgba(255,255,255,0.1);
}
#topmost-toggle-btn.faded:hover { background:rgba(0,0,0,0.35); }
";
    public string HtmlCode => @"
<div id='topmost-toggle-btn' data-no-through title='维持窗口置顶'>
    <svg id='topmost-toggle-on' viewBox='0 0 24 24' width='16' height='16' fill='currentColor'>
        <path d='M8 11h3v10h2V11h3l-4-4-4 4zM4 3v2h16V3H4z'/>
    </svg>
    <svg id='topmost-toggle-off' viewBox='0 0 24 24' width='16' height='16' fill='currentColor' style='display:none'>
        <path d='M8 11h3v10h2V11h3l-4-4-4 4zM4 3v2h16V3H4z'/>
        <line x1='3.5' y1='3.5' x2='20.5' y2='20.5' stroke='rgba(0,0,0,0.65)' stroke-width='2.4' stroke-linecap='round'/>
    </svg>
</div>
";
    public string JsCode => @"
(function() {
    var btn = document.getElementById('topmost-toggle-btn');
    var onIcon = document.getElementById('topmost-toggle-on');
    var offIcon = document.getElementById('topmost-toggle-off');
    messageBus.on('topmost_state', function(msg) {
        btn.classList.toggle('faded', !msg.on);
        onIcon.style.display = msg.on ? 'block' : 'none';
        offIcon.style.display = msg.on ? 'none' : 'block';
    });
    btn.addEventListener('click', function() { postMessage({type:'topmost_toggle'}); });
    postMessage({type:'topmost_ready'});
})();
";

    readonly PetBridge bridge;
    readonly PetWindow window;
    CancellationTokenSource? cancellationTokenSource;
    bool topmost = true;

    /// <summary>重新声明置顶的间隔。任务栏被点击后会把自己抬到置顶窗口之上，需定时拉回。</summary>
    const int KeepOnTopIntervalMs = 500;

    public TopmostModule(PetBridge bridge, PetWindow window)
    {
        this.bridge = bridge;
        this.window = window;
        bridge.OnMessage += OnBridgeMessage;
    }
    public void Dispose()
    {
        bridge.OnMessage -= OnBridgeMessage;
        StopLoop();
    }

    void OnBridgeMessage(string type, JsonElement data)
    {
        switch (type)
        {
            case "topmost_ready":
                bridge.SendMessage("topmost_state", new { on = topmost });
                //重载页面后若仍是置顶态，需要重新建立维持循环
                if (topmost)
                    StartLoop();
                break;
            case "topmost_toggle":
                SetTopmost(!topmost);
                break;
        }
    }

    void SetTopmost(bool enabled)
    {
        topmost = enabled;
        if (enabled)
        {
            RaiseToTop();
            StartLoop();
        }
        else
        {
            //只停掉周期性维持，不去掉窗口的置顶属性
            StopLoop();
        }
        bridge.SendMessage("topmost_state", new { on = topmost });
    }

    /// <summary>把窗口置顶层级提到最高（screen-saver），确保盖过全屏/无边框窗口与任务栏。</summary>
    void RaiseToTop()
    {
        window.Window.SetAlwaysOnTop(true, (OnTopLevel)7, 1);
    }

    void StartLoop()
    {
        if (cancellationTokenSource != null)
            return;
        cancellationTokenSource = new CancellationTokenSource();
        KeepOnTopLoop(cancellationTokenSource.Token);
    }
    void StopLoop()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
    }

    async void KeepOnTopLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                RaiseToTop();
                await Task.Delay(KeepOnTopIntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }
}
