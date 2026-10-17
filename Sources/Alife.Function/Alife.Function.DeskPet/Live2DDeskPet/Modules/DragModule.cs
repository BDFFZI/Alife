using System;
using System.Drawing;
using System.Text.Json;

namespace Alife.Function.DeskPet;

public class DragModule : IPetModule, IDisposable
{
    public string JsCode => @"
let isDragging = false;
window.addEventListener('mousedown', function(e) {
    if (e.button !== 0) return;
    if (!e.target.closest('[data-drag]')) return;
    isDragging = true;
    postMessage({type:'drag_start'});
});
window.addEventListener('mousemove', function(e) {
    if (isDragging === true) {
        postMessage({type:'drag_move',dx:e.movementX,dy:e.movementY});
    }
});
window.addEventListener('mouseup', function(e) {
    isDragging = false;
});
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
                startBounds.X += data.GetProperty("dx").GetInt32();
                startBounds.Y += data.GetProperty("dy").GetInt32();
                _ = window.SetBoundsAsync(startBounds);
                break;
        }
    }
}