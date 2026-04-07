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

            if (stepDist < 2)
                return;

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
                SendToWebView(new { type = "shake" });
            }
            else if (totalPath > 5000 && directionChanges < 2)
            {
                totalPath = 0;
                directionChanges = 0;
                SendToWebView(new { type = "move" });
            }
        };

        try
        {
            mouseTracker = new MouseTracker(webView);
            mouseTracker.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化失败: {ex.Message}");
        }
    }

    void SendToWebView(object data)
    {
        if (webView.CoreWebView2 == null)
            return;
        string json = JsonSerializer.Serialize(data);
        webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    async void InitializeWebView()
    {
        try
        {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            string wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);

            webView.Source = new Uri("https://app.local/index.html");
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            _ = Task.Run(StartIpcListener);
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
                if (line == null)
                    break;

                Dispatcher.Invoke(() => {
                    if (webView.CoreWebView2 != null)
                        webView.CoreWebView2.PostWebMessageAsJson(line);
                    HandleHostCommand(line);
                });
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
            IpcCommand? command = JsonSerializer.Deserialize<IpcCommand>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (command is WindowMoveCommand moveCmd)
            {
                isProgrammaticMove = true;

                DpiScale dpiInfo = VisualTreeHelper.GetDpi(this);
                double logicalX = moveCmd.X / dpiInfo.DpiScaleX;
                double logicalY = moveCmd.Y / dpiInfo.DpiScaleY;

                logicalLeft += logicalX;
                logicalTop += logicalY;

                double targetLeft = logicalLeft;
                double targetTop = logicalTop;

                System.Windows.Media.Animation.DoubleAnimation animX = new(Left, targetLeft, TimeSpan.FromMilliseconds(moveCmd.Duration)) {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                System.Windows.Media.Animation.DoubleAnimation animY = new(Top, targetTop, TimeSpan.FromMilliseconds(moveCmd.Duration)) {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };

                int completedCount = 0;
                void OnComplete()
                {
                    completedCount++;
                    if (completedCount >= 2)
                    {
                        BeginAnimation(LeftProperty, null);
                        BeginAnimation(TopProperty, null);
                        Left = targetLeft;
                        Top = targetTop;

                        lastManualLeft = targetLeft;
                        lastManualTop = targetTop;
                        lastManualDx = 0;
                        lastManualDy = 0;
                        totalPath = 0;

                        isProgrammaticMove = false;

                        Console.WriteLine(JsonSerializer.Serialize(new { type = "pmove-finished" }));
                    }
                }

                animX.Completed += (s, e) => OnComplete();
                animY.Completed += (s, e) => OnComplete();

                BeginAnimation(LeftProperty, animX);
                BeginAnimation(TopProperty, animY);
            }
            else if (command is GetPositionCommand)
            {
                double centerX = Left + Width / 2;
                double centerY = Top + Height / 2;

                DpiScale dpiInfo = VisualTreeHelper.GetDpi(this);
                double physicalX = centerX * dpiInfo.DpiScaleX;
                double physicalY = centerY * dpiInfo.DpiScaleY;

                Console.WriteLine(JsonSerializer.Serialize(new { type = "position", x = physicalX, y = physicalY }));
            }
        }
        catch (JsonException)
        {
            // 对于单纯发给前端的未知命令，反序列化可能被忽略返回 null 或直接抛出异常，无需处理
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Move Error: {ex.Message}");
            isProgrammaticMove = false;
            Console.WriteLine(JsonSerializer.Serialize(new { type = "pmove-finished" }));
        }
    }

    void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            Console.WriteLine(json);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("type", out JsonElement typeProp) == false)
                return;
            string? type = typeProp.GetString();

            if (type == "drag-request")
            {
                Dispatcher.Invoke(() => {
                    WindowInteropHelper helper = new(this);
                    ReleaseCapture();
                    SendMessage(helper.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebMessage processing error: {ex.Message}");
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
        double dx = currentPoint.X - dragStartPoint.X;
        double dy = currentPoint.Y - dragStartPoint.Y;

        Left = dragStartLeft + dx;
        Top = dragStartTop + dy;
    }

    void OnManualDragEnd()
    {
        isDragging = false;
        ReleaseCapture();
    }

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

    bool isDragging;
    Point dragStartPoint;
    double dragStartLeft;
    double dragStartTop;

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    const int WM_NCLBUTTONDOWN = 0xA1;
    const int HTCAPTION = 0x2;
}
