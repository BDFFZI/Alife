using System;
using System.Drawing;
using System.Text.Json;

namespace Alife.Function.DeskPet;

public class DragModule : IPetModule, IDisposable
{
    public string JsCode => @"
(() => {
    let drag = null;
    window.addEventListener('pointerdown', e => {
        if (!e.isPrimary || e.button !== 0 || !e.target.closest('[data-drag]')) return;
        drag = { id: e.pointerId, x: e.screenX, y: e.screenY, target: e.target };
        e.target.setPointerCapture(e.pointerId);
        postMessage({type:'drag_start'});
    });
    window.addEventListener('pointermove', e => {
        if (!drag || drag.id !== e.pointerId) return;
        // Cumulative screen delta survives moving the window underneath the captured pointer.
        postMessage({type:'drag_move', dx:Math.round(e.screenX-drag.x), dy:Math.round(e.screenY-drag.y)});
    });
    function end(e) { if (drag && drag.id === e.pointerId) drag = null; }
    window.addEventListener('pointerup', e => {
        if (drag && drag.id === e.pointerId)
            postMessage({type:'drag_move',dx:Math.round(e.screenX-drag.x),dy:Math.round(e.screenY-drag.y)});
        end(e);
    });
    window.addEventListener('pointercancel', end);
    window.addEventListener('lostpointercapture', end);
})();
";

    readonly PetWindow bridge;
    readonly PetWindow window;
    Rectangle startBounds;

    public DragModule(PetWindow bridge, PetWindow window)
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
            case "drag_start":
                startBounds = window.Bounds;
                break;
            case "drag_move":
                Rectangle moved = startBounds;
                moved.X += data.GetProperty("dx").GetInt32();
                moved.Y += data.GetProperty("dy").GetInt32();
                _ = window.SetBoundsAsync(moved);
                break;
        }
    }
}