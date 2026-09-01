using System;
using System.Text.Json;
using ElectronNET.API.Entities;

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

    readonly PetBridge bridge;
    readonly PetWindow window;
    Rectangle? startPosition;

    public DragModule(PetBridge bridge, PetWindow window)
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
                startPosition = window.Bounds;
                break;
            case "drag_move":
                if (startPosition != null)
                {
                    startPosition.X += data.GetProperty("dx").GetInt32();
                    startPosition.Y += data.GetProperty("dy").GetInt32();
                    window.Window.SetBounds(startPosition);
                }
                break;
        }
    }
}