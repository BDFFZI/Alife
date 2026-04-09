using Alife.Basic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using System.Windows.Media;

namespace Alife.Function.DeskPet;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(WindowMoveCommand), "window-move")]
[JsonDerivedType(typeof(GetPositionCommand), "get-position")]
public abstract record IpcCommand;

public record WindowMoveCommand(double X, double Y, int Duration) : IpcCommand;
public record GetPositionCommand() : IpcCommand;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        InitializeComponent();

        MouseLeftButtonDown += (s, e) => {
            if (e.ChangedButton == MouseButton.Left)
                OnManualDragStart(e);
        };

        MouseMove += (s, e) => {
            if (isDragging)
                OnManualDragMove(e);
        };

        MouseLeftButtonUp += (s, e) => {
            if (isDragging)
                OnManualDragEnd();
        };

        Loaded += (s, e) => {
            InitializeWebView();
            logicalLeft = Left;
            logicalTop = Top;
            lastManualLeft = Left;
            lastManualTop = Top;
        };

        LocationChanged += (s, e) => {
            if (isProgrammaticMove)
                return;

            logicalLeft = Left;
            logicalTop = Top;

            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (now - lastMoveTime > 300)
            {
                totalPath = 0;
                directionChanges = 0;
            }

            double dx = Left - lastManualLeft;
            double dy = Top - lastManualTop;
            double stepDist = Math.Sqrt(dx * dx + dy * dy);

            if (stepDist < 2) return;

            totalPath += stepDist;
            if (lastManualDx != 0 && Math.Sign(dx) != Math.Sign(lastManualDx)) directionChanges++;
            if (lastManualDy != 0 && Math.Sign(dy) != Math.Sign(lastManualDy)) directionChanges++;

            lastManualLeft = Left;
            lastManualTop = Top;
            lastManualDx = dx;
            lastManualDy = dy;
            lastMoveTime = now;

            if (totalPath > 1000 && directionChanges >= 4)
            {
                totalPath = 0;
                directionChanges = 0;
                Console.WriteLine(JsonSerializer.Serialize(new { type = "shake" }));
            }
            else if (totalPath > 5000 && directionChanges < 2)
            {
                totalPath = 0;
                directionChanges = 0;
                Console.WriteLine(JsonSerializer.Serialize(new { type = "move" }));
            }
        };

        try
        {
            mouseTracker = new MouseTracker(this);
            mouseTracker.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化失败: {ex.Message}");
        }
    }

    public void HandleMouseMoveRaw(int x, int y)
    {
        lastMouseMoveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        
        // 计算归一化坐标 [-1, 1]
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double logicalMouseX = x / dpi.DpiScaleX;
        double logicalMouseY = y / dpi.DpiScaleY;
        
        double centerX = Left + Width / 2;
        double centerY = Top + Height / 2;

        // 归一化：相对于窗口中心的偏移量 / 半个窗口宽度
        double nx = (logicalMouseX - centerX) / (Width / 2);
        double ny = (logicalMouseY - centerY) / (Height / 2);

        // 发送给桥接层进行转发
        _ = bridge?.SetFocusAsync(nx, ny);
    }

    async void InitializeWebView()
    {
        try
        {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);

            bridge = new PetBridge(webView);
            bridge.OnHit += (areas) => Console.WriteLine(JsonSerializer.Serialize(new { type = "hit", areas }));
            bridge.OnChat += (text) => Console.WriteLine(JsonSerializer.Serialize(new { type = "chat", text }));
            bridge.OnReady += () => {
                _ = bridge.LoadModelAsync("model/Mao/Mao.model3.json");
                Console.WriteLine(JsonSerializer.Serialize(new { type = "ready" }));
            };
            bridge.OnDragRequest += () => {
                Dispatcher.Invoke(() => {
                    WindowInteropHelper helper = new(this);
                    ReleaseCapture();
                    SendMessage(helper.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                });
            };

            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            string wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);

            webView.Source = new Uri("https://app.local/index.html");
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            _ = Task.Run(StartIpcListener);
            _ = Task.Run(FocusResetLoop);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Pet Error: {ex.Message}");
        }
    }

    void StartIpcListener()
    {
        while (true)
        {
            try
            {
                string? line = Console.ReadLine();
                if (line == null) break;

                Dispatcher.Invoke(() => HandleHostCommand(line));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IPC Listener Error: {ex.Message}");
            }
        }

        if (Console.IsInputRedirected)
            Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    void HandleHostCommand(string json)
    {
        try
        {
            // First check if it's a bridge command
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("type", out JsonElement typeProp))
            {
                string? type = typeProp.GetString();
                if (type is "bubble" or "expression" or "motion" or "look" or "hide-bubble")
                {
                    webView.CoreWebView2?.PostWebMessageAsJson(json);
                    return;
                }
            }

            IpcCommand? command = JsonSerializer.Deserialize<IpcCommand>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (command is WindowMoveCommand moveCmd)
            {
                isProgrammaticMove = true;
                DpiScale dpiInfo = VisualTreeHelper.GetDpi(this);
                double targetLeft = logicalLeft + (moveCmd.X / dpiInfo.DpiScaleX);
                double targetTop = logicalTop + (moveCmd.Y / dpiInfo.DpiScaleY);

                System.Windows.Media.Animation.DoubleAnimation animX = new(Left, targetLeft, TimeSpan.FromMilliseconds(moveCmd.Duration)) {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                System.Windows.Media.Animation.DoubleAnimation animY = new(Top, targetTop, TimeSpan.FromMilliseconds(moveCmd.Duration)) {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };

                int completedCount = 0;
                void OnComplete()
                {
                    if (++completedCount < 2) return;
                    BeginAnimation(LeftProperty, null);
                    BeginAnimation(TopProperty, null);
                    Left = logicalLeft = targetLeft;
                    Top = logicalTop = targetTop;
                    lastManualLeft = targetLeft;
                    lastManualTop = targetTop;
                    isProgrammaticMove = false;
                    Console.WriteLine(JsonSerializer.Serialize(new { type = "pmove-finished" }));
                }

                animX.Completed += (s, e) => OnComplete();
                animY.Completed += (s, e) => OnComplete();
                BeginAnimation(LeftProperty, animX);
                BeginAnimation(TopProperty, animY);
            }
            else if (command is GetPositionCommand)
            {
                DpiScale dpiInfo = VisualTreeHelper.GetDpi(this);
                Console.WriteLine(JsonSerializer.Serialize(new { 
                    type = "position", 
                    x = (Left + Width / 2) * dpiInfo.DpiScaleX, 
                    y = (Top + Height / 2) * dpiInfo.DpiScaleY 
                }));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Command Error: {ex.Message}");
            isProgrammaticMove = false;
            Console.WriteLine(JsonSerializer.Serialize(new { type = "pmove-finished" }));
        }
    }

    void OnManualDragStart(MouseButtonEventArgs e)
    {
        isDragging = true;
        dragStartPoint = PointToScreen(e.GetPosition(this));
        dragStartLeft = Left;
        dragStartTop = Top;
        CaptureMouse();
    }

    void OnManualDragMove(MouseEventArgs e)
    {
        Point currentPoint = PointToScreen(e.GetPosition(this));
        Left = dragStartLeft + (currentPoint.X - dragStartPoint.X);
        Top = dragStartTop + (currentPoint.Y - dragStartPoint.Y);
    }

    void OnManualDragEnd()
    {
        isDragging = false;
        ReleaseCapture();
    }

    PetBridge? bridge;
    MouseTracker? mouseTracker;

    bool isProgrammaticMove;
    double logicalLeft;
    double logicalTop;

    double lastManualLeft;
    double lastManualTop;
    double totalPath;
    int directionChanges;
    double lastManualDx;
    double lastManualDy;
    long lastMoveTime;
    long lastMouseMoveTime;

    bool isDragging;
    Point dragStartPoint;
    double dragStartLeft;
    double dragStartTop;

    async void FocusResetLoop()
    {
        while (true)
        {
            await Task.Delay(500);
            if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastMouseMoveTime > 3000)
            {
                // Reset focus to center (0, 0)
                Dispatcher.Invoke(() => {
                    _ = bridge?.SetFocusAsync(0, 0);
                });
            }
        }
    }

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    const int WM_NCLBUTTONDOWN = 0xA1;
    const int HTCAPTION = 0x2;
}
