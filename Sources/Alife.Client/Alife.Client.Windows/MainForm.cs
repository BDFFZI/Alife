using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Alife.Framework;

namespace Alife.Client.Windows;

class MainForm : Form
{
    public MainForm(IServiceProvider provider)
    {
        serviceProvider = provider;
        InitializeComponent();
        InitializeTrayIcon();

        void InitializeComponent()
        {
            BlazorWebView blazor = new() {
                HostPage = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
                Services = provider,
                Dock = DockStyle.Fill,
            };
            blazor.RootComponents.Add(new RootComponent("#app", typeof(Components.Routes), null));
            Controls.Add(blazor);

            Text = "Alife";
            float dpiScale = DeviceDpi / 96f;
            ClientSize = new Size((int)(1200 * dpiScale), (int)(700 * dpiScale));
            StartPosition = FormStartPosition.CenterScreen;
            Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "alife-icon.ico"));
        }
        void InitializeTrayIcon()
        {
            trayIcon = new NotifyIcon {
                Text = "Alife",
                Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "alife-icon.ico")),
            };

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示主窗口", null, (_, _) => ShowWindow());
            trayMenu.Items.Add("退出", null, (_, _) => Shutdown());
            trayIcon.ContextMenuStrip = trayMenu;

            trayIcon.MouseClick += (_, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    if (Visible)
                        HideWindow();
                    else
                        ShowWindow();
                }
            };

            trayIcon.Visible = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (shuttingDown == false)
        {
            DialogResult result = MessageBox.Show(
                this,
                "是 - 直接关闭\n否 - 最小化到托盘",
                "是否直接关闭应用？",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                Hide();
                //由 Shutdown 统一设 shuttingDown 并释放资源，避免重复守卫把清理跳过
                Shutdown();
            }
            else if (result == DialogResult.No)
            {
                HideWindow();
            }

            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    Rectangle? savedBounds;
    bool shuttingDown;

    void ShowWindow()
    {
        Show();
        ShowInTaskbar = true;

        if (savedBounds.HasValue)
        {
            Rectangle bounds = savedBounds.Value;
            WindowState = FormWindowState.Normal;
            Left = bounds.X;
            Top = bounds.Y;
            Width = bounds.Width;
            Height = bounds.Height;
        }
        else
        {
            WindowState = FormWindowState.Maximized;
        }

        Activate();
    }
    void HideWindow()
    {
        savedBounds = WindowState == FormWindowState.Normal ? new Rectangle(Left, Top, Width, Height) : null;
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        Hide();
    }
    async void Shutdown()
    {
        //先同步释放关键资源（MCP 端口、日志句柄、活动角色），再退出消息循环。
        //全部带超时，任何一步卡住也会继续走下去，不会让窗口关不掉。
        if (shuttingDown) return;
        shuttingDown = true;

        //硬性总超时：哪怕关闭链里某处死锁（插件 OnDestroy、网络等待），
        //也必须在 15 秒后强制继续走 Application.Exit，绝不让用户只能任务管理器杀进程。
        try
        {
            Task cleanup = serviceProvider.ShutdownAlife();
            Task done = await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(15)));
            if (done != cleanup)
                System.Diagnostics.Debug.WriteLine("[Shutdown] cleanup timed out, forcing exit");
            else
                await cleanup;   //让内部异常被 catch 到
        }
        catch { }

        if (trayIcon != null)
        {
            try { trayIcon.Visible = false; } catch { }
            try { trayIcon.Dispose(); } catch { }
            trayIcon = null;
        }
        if (trayMenu != null)
        {
            try { trayMenu.Dispose(); } catch { }
            trayMenu = null;
        }

        Application.Exit();
    }

    readonly IServiceProvider serviceProvider;
    NotifyIcon? trayIcon;
    ContextMenuStrip? trayMenu;
}