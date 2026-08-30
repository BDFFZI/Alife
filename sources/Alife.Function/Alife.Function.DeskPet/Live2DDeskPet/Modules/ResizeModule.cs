using System.Text.Json;

namespace Alife.Function.DeskPet;

public class ResizeModule : IPetModule
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
    var sx, sy;
    btn.addEventListener('pointerdown', function(e) {
        if (e.button !== 0) return;
        btn.setPointerCapture(e.pointerId);
        sx = e.screenX; sy = e.screenY;
    });
    btn.addEventListener('pointermove', function(e) {
        if (btn.hasPointerCapture(e.pointerId)) {
            var dx = e.screenX - sx, dy = e.screenY - sy;
            if (dx !== 0 || dy !== 0) {
                postMessage({type:'resize_delta', dx:dx, dy:dy});
                sx = e.screenX; sy = e.screenY;
            }
        }
    });
    btn.addEventListener('pointerup', function(e) {
        btn.releasePointerCapture(e.pointerId);
    });
})();
";

    readonly PetBridge bridge;
    readonly PetWindow window;

    public ResizeModule(PetBridge bridge, PetWindow window)
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
        if (type == "resize_delta")
        {
            (double scaleX, double scaleY) dpi = window.GetDpi();
            double dx = data.GetProperty("dx").GetDouble() / dpi.scaleX;
            double dy = data.GetProperty("dy").GetDouble() / dpi.scaleY;
            window.ResizeBy(dx, dy);
        }
    }
}