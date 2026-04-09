using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;

namespace Alife.Function.DeskPet;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ipc = new PetIpcHandler();
        viewModel = new DeskPetViewModel(
            ipc, 
            new InterferenceDetector(),
            () => {
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                return (dpi.DpiScaleX, dpi.DpiScaleY);
            },
            () => (Left, Top, Width, Height)
        );

        viewModel.MoveRequested += OnMoveRequested;

        Loaded += (object s, RoutedEventArgs e) => {
            InitializeWebView();
            ipc.StartListening();
        };

        LocationChanged += (object? s, EventArgs e) => {
            if (isProgrammaticMove == false)
                viewModel.ReportWindowLocation(Left, Top);
        };
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left) {
            isDragging = true;
            dragStartPoint = PointToScreen(e.GetPosition(this));
            dragStartLeft = Left; dragStartTop = Top;
            CaptureMouse();
        }
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        if (isDragging) {
            Point p = PointToScreen(e.GetPosition(this));
            Left = dragStartLeft + (p.X - dragStartPoint.X);
            Top = dragStartTop + (p.Y - dragStartPoint.Y);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
        isDragging = false;
        ReleaseCapture();
        base.OnMouseLeftButtonUp(e);
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

    readonly DeskPetViewModel viewModel;
    readonly PetIpcHandler ipc;

    bool isProgrammaticMove;
    bool isDragging;
    Point dragStartPoint;
    double dragStartLeft, dragStartTop;

    async void InitializeWebView()
    {
        try {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);
            
            PetBridge bridge = new(webView);
            viewModel.Initialize(bridge);

            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            string wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
            webView.Source = new Uri("https://app.local/index.html");
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            bridge.OnDragRequest += () => Dispatcher.Invoke(() => {
                ReleaseCapture();
                SendMessage(new WindowInteropHelper(this).Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
            });
        } catch { }
    }

    void OnMoveRequested(WindowMoveCommand cmd, Action onComplete)
    {
        Dispatcher.Invoke(() => {
            isProgrammaticMove = true;
            DpiScale dpiInfo = VisualTreeHelper.GetDpi(this);
            double targetLeft = Left + (cmd.X / dpiInfo.DpiScaleX);
            double targetTop = Top + (cmd.Y / dpiInfo.DpiScaleY);

            DoubleAnimation animX = new(Left, targetLeft, TimeSpan.FromMilliseconds(cmd.Duration)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            DoubleAnimation animY = new(Top, targetTop, TimeSpan.FromMilliseconds(cmd.Duration)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            int count = 0;
            void Check() { if (++count >= 2) { isProgrammaticMove = false; onComplete(); } }
            animX.Completed += (object? s, EventArgs e) => Check();
            animY.Completed += (object? s, EventArgs e) => Check();

            BeginAnimation(LeftProperty, animX);
            BeginAnimation(TopProperty, animY);
        });
    }
}
