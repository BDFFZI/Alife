using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Alife.Function.DeskPet;

/// <summary>
/// 桌面进程的中枢控制器
/// </summary>
public class DeskPetViewModel
{
    public event Action<WindowMoveCommand, Action>? MoveRequested;

    public PetBridge? Bridge { get; private set; }

    public DeskPetViewModel(
        PetIpcHandler ipc, 
        InterferenceDetector detector, 
        Func<(double ScaleX, double ScaleY)> getDpi, 
        Func<(double Left, double Top, double Width, double Height)> getLayout)
    {
        this.ipc = ipc;
        this.detector = detector;
        this.getDpi = getDpi;
        this.getLayout = getLayout;
        this.tracker = new MouseTracker();

        this.ipc.CommandReceived += OnIpcCommandReceived;
        this.detector.Shaked += () => ipc.SendEvent(new { type = "shake" });
        this.detector.Moved += () => ipc.SendEvent(new { type = "move" });
        this.detector.MouseShaked += () => ipc.SendEvent(new { type = "mouse-shake" });
        this.tracker.MouseMoved += (int x, int y) => HandleMouseMove(x, y);
    }

    public void Initialize(PetBridge bridge)
    {
        this.Bridge = bridge;
        this.Bridge.OnHit += (System.Collections.Generic.List<string> areas) => ipc.SendEvent(new { type = "hit", areas });
        this.Bridge.OnChat += (string text) => ipc.SendEvent(new { type = "chat", text });
        this.Bridge.OnReady += () =>
        {
            Bridge.LoadModelAsync("model/Mao/Mao.model3.json");
            ipc.SendEvent(new { type = "ready" });
        };
        this.Bridge.OnDragRequest += () => ipc.SendEvent(new { type = "drag-request" });

        this.tracker.Start();
        _ = StartFocusResetLoop();
    }

    public void ReportWindowLocation(double left, double top)
    {
        detector.ReportLocation(left, top);
    }

    public void HandleMouseMove(int x, int y)
    {
        lastMouseMoveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        (double ScaleX, double ScaleY) dpi = getDpi.Invoke();
        (double Left, double Top, double Width, double Height) layout = getLayout.Invoke();

        double logicalMouseX = x / dpi.ScaleX;
        double logicalMouseY = y / dpi.ScaleY;
        
        double centerX = layout.Left + layout.Width / 2;
        double centerY = layout.Top + layout.Height / 2;

        detector.ReportMouseLocation(logicalMouseX, logicalMouseY, centerX, centerY);

        double nx = (logicalMouseX - centerX) / (layout.Width / 2);
        double ny = (logicalMouseY - centerY) / (layout.Height / 2);

        Bridge?.SetFocusAsync(nx, ny);
    }

    void OnIpcCommandReceived(IpcCommand cmd)
    {
        switch (cmd)
        {
            case WindowMoveCommand moveCmd:
                MoveRequested?.Invoke(moveCmd, () => ipc.SendEvent(new { type = "pmove-finished" }));
                break;

            case GetPositionCommand:
                (double Left, double Top, double Width, double Height) layout = getLayout.Invoke();
                (double ScaleX, double ScaleY) dpi = getDpi.Invoke();
                ipc.SendEvent(new
                {
                    type = "position",
                    x = (layout.Left + layout.Width / 2) * dpi.ScaleX,
                    y = (layout.Top + layout.Height / 2) * dpi.ScaleY
                });
                break;

            case GenericBridgeCommand:
                Bridge?.SetFocusAsync(0, 0);
                break;
        }
    }

    async Task StartFocusResetLoop()
    {
        while (true)
        {
            await Task.Delay(500);
            if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastMouseMoveTime > 3000)
            {
                Bridge?.SetFocusAsync(0, 0);
            }
        }
    }

    readonly PetIpcHandler ipc;
    readonly InterferenceDetector detector;
    readonly MouseTracker tracker;
    readonly Func<(double ScaleX, double ScaleY)> getDpi;
    readonly Func<(double Left, double Top, double Width, double Height)> getLayout;

    long lastMouseMoveTime;
}
