using System.Text.Json;

namespace Alife.Function.DeskPet;

public class DragModule : IPetModule
{
    public string JsCode => @"
window.addEventListener('mousedown', async function(e) {
    if (e.button !== 0 || e.target.tagName !== 'CANVAS') return;
    var areas = await model.hitTest(e.clientX, e.clientY);
    if (!areas || areas.length === 0) {
        window.petDragging = true;
        postMessage({type:'drag_start'});
    }
});
window.addEventListener('mouseup', function(e) {
    if (window.petDragging === true) {
        window.petDragging = false;
        postMessage({type:'drag_end'});
    }
});
";

    readonly PetBridge bridge;
    bool isDragging;
    double lastMouseX, lastMouseY;

    public DragModule(PetBridge bridge, MouseTracker tracker, PetWindow window)
    {
        this.bridge = bridge;
        bridge.OnMessage += OnBridgeMessage;
        tracker.MouseMoved += (x, y) => {
            (double scaleX, double scaleY) dpi = window.GetDpi();
            double windowMouseX = x / dpi.scaleX;
            double windowMouseY = y / dpi.scaleY;
            if (isDragging)
                window.MoveBy(windowMouseX - lastMouseX, windowMouseY - lastMouseY);
            lastMouseX = windowMouseX;
            lastMouseY = windowMouseY;
        };
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
                isDragging = true;
                break;
            case "drag_end":
                isDragging = false;
                break;
        }
    }
}