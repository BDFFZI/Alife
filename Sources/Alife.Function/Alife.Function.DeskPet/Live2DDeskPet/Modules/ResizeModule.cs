using System;
using System.Drawing;
using System.Text.Json;

namespace Alife.Function.DeskPet;

public class ResizeModule : IPetModule, IDisposable
{
    public string CssCode => @"
#resize-btn {
    position:fixed; right:15px; bottom:15px;
    width:28px; height:28px;
    background:rgba(0,0,0,0.4);
    backdrop-filter:blur(10px); border-radius:50%;
    display:flex; justify-content:center; align-items:center;
    color:white; cursor:nwse-resize; z-index:2000;
    box-shadow:0 4px 10px rgba(0,0,0,0.2);
    border:1px solid rgba(255,255,255,0.15);
    opacity:0; transition:opacity 0.3s, transform 0.1s;
}
body:hover #resize-btn { opacity:1; }
#resize-btn:active { transform:scale(0.9); }
";
    public string HtmlCode => @"
<div id='resize-btn'>
    <svg viewBox='0 0 24 24' width='16' height='16' fill='currentColor'>
        <path d='M22 22H2v-2h18V2h2v20z'/>
    </svg>
</div>
";
    public string JsCode => @"
(function() {
    var btn = document.getElementById('resize-btn');
    var start = null;
    btn.addEventListener('pointerdown', function(e) {
        if (!e.isPrimary || e.button !== 0) return;
        start = { id:e.pointerId, x:e.screenX, y:e.screenY };
        btn.setPointerCapture(e.pointerId);
        postMessage({type:'resize_start'});
    });
    btn.addEventListener('pointermove', function(e) {
        if (start && start.id === e.pointerId) {
            postMessage({type:'resize_delta',dx:Math.round(e.screenX-start.x),dy:Math.round(e.screenY-start.y)});
        }
    });
    function end(e) { if (start && start.id === e.pointerId) start = null; }
    btn.addEventListener('pointerup', function(e) {
        if (start && start.id === e.pointerId)
            postMessage({type:'resize_delta',dx:Math.round(e.screenX-start.x),dy:Math.round(e.screenY-start.y)});
        end(e);
    });
    btn.addEventListener('pointercancel', end);
    btn.addEventListener('lostpointercapture', end);
})();
";

    readonly PetWindow bridge;
    readonly PetWindow window;
    const int MinSize = 150;
    Rectangle startBounds;

    public ResizeModule(PetWindow bridge, PetWindow window)
    {
        this.bridge = bridge;
        this.window = window;
        bridge.OnMessage += OnBridgeMessage;
    }
    public void Dispose()
    {
        bridge.OnMessage -= OnBridgeMessage;
    }

    void OnBridgeMessage(string type, JsonElement data)
    {
        switch (type)
        {
            case "resize_start":
                startBounds = window.Bounds;
                break;
            case "resize_delta":
                Rectangle resized = startBounds;
                resized.Width = Math.Max(startBounds.Width + data.GetProperty("dx").GetInt32(), MinSize);
                resized.Height = Math.Max(startBounds.Height + data.GetProperty("dy").GetInt32(), MinSize);
                _ = window.SetBoundsAsync(resized);
                break;
        }
    }
}