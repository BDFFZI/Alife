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
/// 但主进程会持续检测光标是否落在锁图标区域，落在锁区域时临时恢复鼠标响应，保证锁图标始终可点击关闭。
/// 穿透状态通过 <see cref="StorageSystem"/> 持久化。
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
body:hover #lock-btn, #lock-btn.always { opacity:1; }
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
        btn.classList.toggle('left', isLeft);
        btn.classList.toggle('right', !isLeft);
        postMessage({type:'lock_corner', corner: isLeft ? 'left' : 'right'});
    }
    function setState(on) {
        btn.classList.toggle('always', on);
        document.getElementById('lock-open').style.display = on ? 'none' : 'block';
        document.getElementById('lock-closed').style.display = on ? 'block' : 'none';
    }
    btn.addEventListener('click', function() { postMessage({type:'lock_toggle'}); });
    messageBus.on('lock_state', function(msg) { setState(!!msg.on); });
    window.addEventListener('resize', corner);
    setInterval(corner, 500);
    corner();
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
                ApplyIgnoreState();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }

    void ApplyIgnoreState()
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
        //光标在锁图标区域内时临时恢复鼠标响应，保证锁图标可点击关闭穿透。
        window.Window.SetIgnoreMouseEvents(!overLock);
    }
}
