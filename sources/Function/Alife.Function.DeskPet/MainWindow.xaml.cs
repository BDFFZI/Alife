using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;

namespace Alife.Function.DeskPet;

/// <summary>
/// 极薄的 UI 壳层，仅通过 IPetWindow 接口提供窗口服务
/// </summary>
public partial class MainWindow : Window, IPetWindow
{
    public MainWindow()
    {
        InitializeComponent();

        server = new PetServer(this);
        
        StateChanged += (s, e) => { if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; };
        MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        
        InitializeWebView();
    }

    public (double Left, double Top, double Width, double Height) GetLayout()
    {
        return (Left, Top, Width, Height);
    }

    public (double ScaleX, double ScaleY) GetDpi()
    {
        Visual? visual = PresentationSource.FromVisual(this)?.CompositionTarget?.RootVisual as Visual;
        if (visual == null) return (1.0, 1.0);
        Matrix matrix = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
        return (matrix.M11, matrix.M22);
    }

    public void ProgrammaticMove(double targetX, double targetY, int durationMs)
    {
        (double ScaleX, double ScaleY) dpi = GetDpi();
        double startX = Left;
        double startY = Top;
        double endX = targetX / dpi.ScaleX - Width / 2;
        double endY = targetY / dpi.ScaleY - Height / 2;

        DoubleAnimation xAnim = new DoubleAnimation(startX, endX, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = new QuadraticEase() };
        DoubleAnimation yAnim = new DoubleAnimation(startY, endY, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = new QuadraticEase() };

        BeginAnimation(LeftProperty, xAnim);
        BeginAnimation(TopProperty, yAnim);
    }


    async void InitializeWebView()
    {
        await webView.EnsureCoreWebView2Async();
        
        bridge = new PetBridge(webView);
        server.InitializeActivity(bridge);
        server.Start();
    }

    PetServer server;
    PetBridge? bridge;
}
