using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using ElectronNET.API.Entities;

namespace Alife.Function.DeskPet;

/// <summary>
/// 鼠标穿透模块：在 UI 角落提供一个圆形锁图标（根据窗口在屏幕的左右半区显示在左上或右上）。
/// 点击锁图标切换鼠标穿透。穿透开启时整个窗口对鼠标透明（点击穿透到后台），
/// 但主进程会持续检测光标是否落在锁图标区域或标记了 data-no-through 的 UI（如输入框）上，
/// 命中时临时恢复鼠标响应：锁图标始终可点击关闭、输入框悬停即显示且可点击使用，移开后恢复穿透。
/// 穿透状态通过 StorageSystem 持久化。
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
<div id='lock-btn' class='right' title='鼠标穿透'>
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
        postMessage({type:'lock_corner', corner: isLeft ? 'right' : 'left'});
    }
    function setState(on) {
        btn.classList.toggle('faded', on);
        document.getElementById('lock-open').style.display = on ? 'none' : 'block';
        document.getElementById('lock-closed').style.display = on ? 'block' : 'none';
    }
    btn.addEventListener('click', function() { postMessage({type:'lock_toggle'}); });
    messageBus.on('lock_state', function(msg) { setState(!!msg.on); });
    window.addEventListener('resize', corner);
    setInterval(corner, 500);
    corner();
    //供主进程在穿透状态下按窗口坐标(CSS px)做 DOM 命中测试：
    //命中 data-no-through 元素时临时加上 passthrough-hover 类（输入框据此显示），并返回是否命中。
    window.isNoThroughAt = function(x, y) {
        var el = document.elementFromPoint(x, y);
        var hit = (el && el.closest('[data-no-through]')) || null;
        if (window.__noThroughHover !== hit) {
            if (window.__noThroughHover) window.__noThroughHover.classList.remove('passthrough-hover');
            if (hit) hit.classList.add('passthrough-hover');
            window.__noThroughHover = hit;
        }
        return !!hit;
    };
    window.clearNoThroughHover = function() {
        if (window.__noThroughHover) {
            window.__noThroughHover.classList.remove('passthrough-hover');
            window.__noThroughHover = null;
        }
    };
    postMessage({type:'lock_ready'});
})();
";

    readonly PetBridge bridge;
    readonly PetWindow window;
    CancellationTokenSource? cancellationTokenSource;
    bool mouseThrough;
    string corner = "right";

    const int LockSize = 34;
    const int LockMargin = 8;

    public MouseThroughModule(PetBridge bridge, PetWindow window)
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
            case "lock_ready":
                bridge.SendMessage("lock_state", new { on = mouseThrough });
                break;
            case "lock_corner":
                if (data.TryGetProperty("corner", out JsonElement cornerProp))
                    corner = cornerProp.GetString() == "left" ? "left" : "right";
                break;
            case "lock_toggle":
                SetMouseThrough(!mouseThrough);
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
            window.Window.SetIgnoreMouseEvents(false);
            _ = bridge.ExecuteJavaScriptAsync("window.clearNoThroughHover && window.clearNoThroughHover()");
        }
    }

    void StartLoop()
    {
        if (cancellationTokenSource != null)
            return;
        cancellationTokenSource = new CancellationTokenSource();
        MouseThroughLoop(cancellationTokenSource.Token);
    }
    void StopLoop()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
    }

    async void MouseThroughLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                await Task.Delay(50, cancellationToken);
                await ApplyIgnoreStateAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }

    async Task ApplyIgnoreStateAsync()
    {
        if (mouseThrough == false)
        {
            window.Window.SetIgnoreMouseEvents(false);
            return;
        }

        Rectangle b = window.Bounds;
        Point c = window.CursorScreenPoint;
        int rx = corner == "left" ? b.X + LockMargin : b.X + b.Width - LockMargin - LockSize;
        int ry = b.Y + LockMargin;
        bool overLock = c.X >= rx && c.X <= rx + LockSize && c.Y >= ry && c.Y <= ry + LockSize;
        //光标在锁图标或标记了 data-no-through 的 UI（如输入框）上时临时恢复鼠标响应。
        bool interactive = overLock || await IsOverNoThroughAsync(b, c);
        window.Window.SetIgnoreMouseEvents(!interactive);
    }

    async Task<bool> IsOverNoThroughAsync(Rectangle bounds, Point cursor)
    {
        double x = cursor.X - bounds.X;
        double y = cursor.Y - bounds.Y;
        if (x < 0 || y < 0 || x > bounds.Width || y > bounds.Height)
            return false;

        string script = FormattableString.Invariant($"isNoThroughAt({x},{y}) ? '1' : ''");
        return await bridge.ExecuteJavaScriptAsync(script) == "1";
    }
}