using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Alife.Function.Browser;

public class BrowserWindow : Form
{
    public WebView2 WebView2 => webView2;
    public CoreWebView2 CoreWebView2 => webView2.CoreWebView2;
    public CoreWebView2Environment CoreWebView2Environment => coreWebView2Environment!;

    readonly string userDataFolder;
    readonly Action launched;
    readonly NotifyIcon notifyIcon;
    readonly WebView2 webView2;
    CoreWebView2Environment? coreWebView2Environment;

    Rectangle? savedBounds;

    void ShowWindow()
    {
        Show();
        ShowInTaskbar = true;

        if (savedBounds.HasValue)
        {
            WindowState = FormWindowState.Normal;
            Bounds = savedBounds.Value;
        }
        else
        {
            WindowState = FormWindowState.Maximized;
        }

        Activate();
    }
    void HideWindow()
    {
        savedBounds = WindowState == FormWindowState.Normal ? Bounds : null;
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        Hide();
    }

    public BrowserWindow(string userDataFolder, Action launched)
    {
        this.userDataFolder = userDataFolder;
        this.launched = launched;

        Text = "Alife Browser";
        Size = new Size(1024, 768);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(webView2 = new WebView2 { Dock = DockStyle.Fill });

        //额外的工具栏功能
        this.launched += () => {
            Controls.Add(CreateToolStrip());
        };

        //使用托盘形窗口
        notifyIcon = CreateNotifyIcon();
        this.launched += HideWindow;

        NotifyIcon CreateNotifyIcon()
        {
            NotifyIcon trayIcon = new();
            trayIcon.Text = "Alife Browser";
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Visible = true;
            trayIcon.MouseClick += (_, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    if (ShowInTaskbar)
                        HideWindow();
                    else
                        ShowWindow();
                }
            };
            return trayIcon;
        }

        ToolStrip CreateToolStrip()
        {
            ToolStrip toolStrip = new();

            ToolStripButton backButton = new("后退");
            ToolStripButton forwardButton = new("前进");
            ToolStripButton refreshButton = new("刷新");
            ToolStripSpringTextBox addressBar = new();
            ToolStripButton goButton = new("前往");
            toolStrip.Items.AddRange(
                backButton, forwardButton, refreshButton,
                new ToolStripSeparator(), addressBar,
                new ToolStripSeparator(), goButton
            );

            backButton.Click += (_, _) => {
                if (webView2.CoreWebView2.CanGoBack)
                    webView2.CoreWebView2.GoBack();
            };
            forwardButton.Click += (_, _) => {
                if (webView2.CoreWebView2.CanGoForward)
                    webView2.CoreWebView2.GoForward();
            };
            refreshButton.Click += (_, _) => webView2.CoreWebView2.Reload();
            addressBar.KeyDown += (_, e) => {
                if (e.KeyCode != Keys.Enter)
                    return;
                e.Handled = true;

                string? url = NormalizeUserUrl(addressBar.Text);
                if (url != null)
                    webView2.CoreWebView2.Navigate(url);
            };
            goButton.Click += (_, _) => {
                string? url = NormalizeUserUrl(addressBar.Text);
                if (url != null)
                    webView2.CoreWebView2.Navigate(url);
            };

            webView2.CoreWebView2.SourceChanged += (_, _) => {
                forwardButton.Enabled = webView2.CoreWebView2.CanGoBack;
                backButton.Enabled = webView2.CoreWebView2.CanGoBack;
                addressBar.Text = webView2.CoreWebView2.Source;
            };

            Controls.Add(toolStrip);

            static string? NormalizeUserUrl(string text)
            {
                string url = text.Trim();
                if (string.IsNullOrWhiteSpace(url))
                    return null;
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                {
                    url = "https://" + url;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                        return null;
                }
                return uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
            }

            return toolStrip;
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideWindow();
            return;
        }
        base.OnFormClosing(e);
    }
    protected override async void OnLoad(EventArgs e)
    {
        try
        {
            base.OnLoad(e);

            coreWebView2Environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await webView2.EnsureCoreWebView2Async(coreWebView2Environment);
            webView2.CoreWebView2.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edge/122.0.0.0";
            webView2.Source = new Uri("https://www.bing.com");

            launched();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}

class ToolStripSpringTextBox : ToolStripTextBox
{
    public override Size GetPreferredSize(Size constrainingSize)
    {
        if (Owner != null)
        {
            int used = Owner.Items.Cast<ToolStripItem>()
                .Where(i => i != this).Sum(i => i.Width + i.Margin.Horizontal);
            return new Size(Math.Max(Owner.Width - used - 30, 50), Height);
        }
        return base.GetPreferredSize(constrainingSize);
    }
}
