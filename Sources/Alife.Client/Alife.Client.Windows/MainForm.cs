using Microsoft.AspNetCore.Components.WebView.WindowsForms;

namespace Alife.Client.Windows;

public class MainForm : Form
{
    public MainForm(IServiceProvider provider)
    {
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
            NotifyIcon trayIcon = new() {
                Text = "Alife",
                Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "alife-icon.ico")),
            };

            ContextMenuStrip trayMenu = new();
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
                shuttingDown = true;
                Hide();
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
    void Shutdown()
    {
        Application.Exit();
    }
}