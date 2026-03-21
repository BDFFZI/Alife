using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using System.Reflection;

// 导入相关命名空间
// using Alife.Abstractions;
// using Alife.OfficialPlugins; // Decoupled

namespace Alife.Pet;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MouseTracker mouseTracker;


    public MainWindow()
    {
        // 强制使用 UTF-8 编码进行 IPC 通讯
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        InitializeComponent();

        Debug.WriteLine("=== MainWindow Constructor ===");

        // 允许拖动窗口 (手动实现非阻塞方案)
        this.MouseLeftButtonDown += (s, e) => {
            if (e.ChangedButton == MouseButton.Left)
            {
                OnManualDragStart(e);
            }
        };

        this.MouseMove += (s, e) => {
            if (_isDragging)
            {
                OnManualDragMove(e);
            }
        };

        this.MouseLeftButtonUp += (s, e) => {
            if (_isDragging)
            {
                OnManualDragEnd();
            }
        };

        this.Loaded += (s, e) => {
            InitializeWebView();
            // 初始化逻辑坐标和记账坐标
            _logicalLeft = this.Left;
            _logicalTop = this.Top;
            _lastManualLeft = this.Left;
            _lastManualTop = this.Top; // [FIX] 纠正之前的笔误
        };

        this.LocationChanged += (s, e) => {
            if (_isProgrammaticMove) return;

            // [FIX] 手动拖拽时同步更新逻辑坐标，确保下一次程序位移基于真实当前位置
            _logicalLeft = this.Left;
            _logicalTop = this.Top;

            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            // 如果停顿超过 300ms，重置状态
            if (now - _lastMoveTime > 300)
            {
                _totalPath = 0;
                _directionChanges = 0;
            }

            double dx = this.Left - _lastManualLeft;
            double dy = this.Top - _lastManualTop;
            double stepDist = Math.Sqrt(dx * dx + dy * dy);

            // 过滤极小位移
            if (stepDist < 2) return;

            _totalPath += stepDist;

            // 检测方向变化（反向移动）
            if (_lastManualDx != 0 && Math.Sign(dx) != Math.Sign(_lastManualDx)) _directionChanges++;
            if (_lastManualDy != 0 && Math.Sign(dy) != Math.Sign(_lastManualDy)) _directionChanges++;

            _lastManualLeft = this.Left;
            _lastManualTop = this.Top;
            _lastManualDx = dx;
            _lastManualDy = dy;
            _lastMoveTime = now;

            // 逻辑：
            // 1. 如果积累了一定路程且方向改变频繁 -> 抖动 (Shake)
            if (_totalPath > 1000 && _directionChanges >= 4)
            {
                _totalPath = 0;
                _directionChanges = 0;
                SendToWebView(new { type = "shake" });
            }
            // 2. 如果只是单向移动了很远 -> 搬家 (Move)
            else if (_totalPath > 5000 && _directionChanges < 2)
            {
                _totalPath = 0;
                _directionChanges = 0;
                SendToWebView(new { type = "move" });
            }
        };

        try
        {
            // 初始化鼠标追踪
            mouseTracker = new MouseTracker(webView);
            mouseTracker.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化失败: {ex.Message}");
        }
    }


    private void SendToWebView(object data)
    {
        if (webView.CoreWebView2 == null) return;
        string json = JsonSerializer.Serialize(data);
        webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private async void InitializeWebView()
    {
        Debug.WriteLine("Starting WebView2 initialization...");
        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);
            Debug.WriteLine("WebView2 Core initialized.");

            // 注册消息接收
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // 设置背景透明
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            string wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);

            webView.Source = new Uri("https://app.local/index.html");
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            // 【关键新增】开启 IPC 监听任务
            _ = Task.Run(StartIpcListener);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CRITICAL ERROR: {ex.Message}\n{ex.StackTrace}");
            Console.Error.WriteLine($"Pet Error: {ex.Message}");
        }
    }

    private void StartIpcListener()
    {
        while (true)
        {
            try
            {
                string? line = Console.ReadLine();
                if (line == null) break;

                // 将从 Host 接收到的命令转发给 WebView2
                Dispatcher.Invoke(() => {
                    if (webView.CoreWebView2 != null)
                    {
                        webView.CoreWebView2.PostWebMessageAsJson(line);
                    }
                    HandleHostCommand(line);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IPC Listener Error: {ex.Message}");
            }
        }
        // 当 StandardInput 关闭时（且是在重定向模式下），自动退出桌宠
        if (Console.IsInputRedirected)
        {
            Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
    }

    private bool _isProgrammaticMove = false;
    private double _logicalLeft;
    private double _logicalTop;

    // 拖拽与抖动检测状态同步到字段，方便程序位移后校准
    private double _lastManualLeft = 0, _lastManualTop = 0;
    private double _totalPath = 0;
    private int _directionChanges = 0;
    private double _lastManualDx = 0, _lastManualDy = 0;
    private long _lastMoveTime = 0;

    private void HandleHostCommand(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            if (type == "window-move")
            {
                double x = root.GetProperty("x").GetDouble();
                double y = root.GetProperty("y").GetDouble();
                int duration = root.GetProperty("duration").GetInt32();

                _isProgrammaticMove = true;

                // 因为 host 端(大脑)也是基于 1920*1080 的物理像素下发移动偏移量，
                // 所以我们在此处将要移动的 x 和 y 从物理像素转回 WPF 能接受的逻辑像素再进行叠加：
                var dpiInfo = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                double logicalX = x / dpiInfo.DpiScaleX;
                double logicalY = y / dpiInfo.DpiScaleY;

                // [FIX] 坐标堆叠逻辑：基于逻辑终点叠加位移，防止连发指令导致的步长缩减/乱走
                _logicalLeft += logicalX;
                _logicalTop += logicalY;

                // 用户要求：去掉 AI 移动时的范围限制，所以这里不再约束 _logicalLeft 和 _logicalTop 必须在 VirtualScreen 内

                double targetLeft = _logicalLeft;
                double targetTop = _logicalTop;

                // 使用 WPF 动画平滑移动 (从当前实际位置到最新逻辑终点)
                var animX = new System.Windows.Media.Animation.DoubleAnimation(Left, targetLeft, TimeSpan.FromMilliseconds(duration)) {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                var animY = new System.Windows.Media.Animation.DoubleAnimation(Top, targetTop, TimeSpan.FromMilliseconds(duration)) {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };

                // 为防止动画结束后属性被锁定，在完成后清理并将值设回本地区
                int completedCount = 0;
                void OnComplete()
                {
                    completedCount++;
                    if (completedCount >= 2)
                    {
                        // 结束动画前先更新本地区的值，确保坐标固化，防止回弹
                        BeginAnimation(LeftProperty, null);
                        BeginAnimation(TopProperty, null);
                        Left = targetLeft;
                        Top = targetTop;

                        // [FIX] 同步手动记账坐标，防止位移后瞬间触发错误的“拖拽检测”或“坐标回跳反馈”
                        _lastManualLeft = targetLeft;
                        _lastManualTop = targetTop;
                        _lastManualDx = 0;
                        _lastManualDy = 0;
                        _totalPath = 0;

                        _isProgrammaticMove = false;

                        // 【关键】通知 Host 移动已完成，解除 AI 的等待
                        Console.WriteLine(JsonSerializer.Serialize(new { type = "move-finished" }));
                    }
                }

                animX.Completed += (s, e) => OnComplete();
                animY.Completed += (s, e) => OnComplete();

                BeginAnimation(LeftProperty, animX);
                BeginAnimation(TopProperty, animY);
            }
            else if (type == "get-position")
            {
                double centerX = Left + Width / 2;
                double centerY = Top + Height / 2;

                // 转换为系统真实的物理像素 (贴合 1920*1080 坐标系)
                var dpiInfo = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                double physicalX = centerX * dpiInfo.DpiScaleX;
                double physicalY = centerY * dpiInfo.DpiScaleY;

                Console.WriteLine(JsonSerializer.Serialize(new { type = "position", x = physicalX, y = physicalY }));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Move Error: {ex.Message}");
            _isProgrammaticMove = false;
            // 发生异常也尝试通知，防止 AI 永久挂起
            Console.WriteLine(JsonSerializer.Serialize(new { type = "move-finished" }));
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            // 将从 WebView2 接收到的事件转发给 Host
            Console.WriteLine(json);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            if (type == "drag-request")
            {
                Dispatcher.Invoke(() => {
                    var helper = new WindowInteropHelper(this);
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

    private bool _isDragging = false;
    private Point _dragStartPoint;
    private double _dragStartLeft;
    private double _dragStartTop;

    private void OnManualDragStart(MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStartPoint = PointToScreen(e.GetPosition(this));
        _dragStartLeft = this.Left;
        _dragStartTop = this.Top;
        this.CaptureMouse();
    }

    private void OnManualDragMove(MouseEventArgs e)
    {
        var currentPoint = PointToScreen(e.GetPosition(this));
        double dx = currentPoint.X - _dragStartPoint.X;
        double dy = currentPoint.Y - _dragStartPoint.Y;

        this.Left = _dragStartLeft + dx;
        this.Top = _dragStartTop + dy;
    }

    private void OnManualDragEnd()
    {
        _isDragging = false;
        MainWindow.ReleaseCapture();
    }

    // Windows API
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;
}
