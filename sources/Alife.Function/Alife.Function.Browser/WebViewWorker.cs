using System.Collections.Concurrent;
using Alife.Basic;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Alife.Function.Browser;

public class WebViewWorker : IDisposable
{
    public bool IsNavigating => isNavigating;
    public bool IsLoaded => isLoaded;

    public Task<T> AddFormTask<T>(Func<WebView2, Task<T>> action)
    {
        if (form == null || form.IsDisposed)
            throw new ObjectDisposedException(nameof(WebViewWorker));

        TaskCompletionSource<T> tcs = new();

        formTasks.Add(async () => {
            try
            {
                if (webView == null)
                    throw new ArgumentNullException(nameof(webView));
                T result = await action(webView);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    public Task ShowBrowserWindowAsync()
    {
        return AddFormTask(webView => {
            ShowBrowserWindow();
            return Task.FromResult(webView);
        });
    }

    AlifeForm? form;
    WebView2? webView;
    Button? backButton;
    Button? forwardButton;
    Button? refreshButton;
    Button? goButton;
    TextBox? addressTextBox;
    readonly BlockingCollection<Func<Task>> formTasks = new();
    bool isNavigating;
    bool isLoaded;
    bool isDisposing;

    public WebViewWorker()
    {
        var thread = new Thread(() => {
            try
            {
                form = new AlifeForm {
                    Text = "Alife Browser",
                    Width = 1024,
                    Height = 768,
                    WindowState = FormWindowState.Minimized,
                    ShowInTaskbar = true,
                    FormBorderStyle = FormBorderStyle.Sizable,
                };

                webView = new WebView2 { Dock = DockStyle.Fill };
                Control toolbar = CreateToolbar();

                form.Controls.Add(webView);
                form.Controls.Add(toolbar);
                form.Load += OnFormOnLoad;
                form.FormClosing += OnFormClosing;

                Application.Run(form);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    public void Dispose()
    {
        isDisposing = true;
        formTasks.CompleteAdding();
        if (form is { IsDisposed: false })
            form.Invoke((Action)(() => form.Close()));
    }

    Control CreateToolbar()
    {
        TableLayoutPanel toolbar = new() {
            Dock = DockStyle.Top,
            Height = 36,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(4),
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));

        backButton = CreateToolbarButton("Back");
        forwardButton = CreateToolbarButton("Forward");
        refreshButton = CreateToolbarButton("Refresh");
        goButton = CreateToolbarButton("Go");
        addressTextBox = new TextBox {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "about:blank",
        };

        backButton.Click += (_, _) => {
            if (webView?.CoreWebView2?.CanGoBack == true)
                webView.CoreWebView2.GoBack();
        };
        forwardButton.Click += (_, _) => {
            if (webView?.CoreWebView2?.CanGoForward == true)
                webView.CoreWebView2.GoForward();
        };
        refreshButton.Click += (_, _) => webView?.CoreWebView2?.Reload();
        goButton.Click += (_, _) => NavigateFromAddressBar();
        addressTextBox.KeyDown += (_, e) => {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            NavigateFromAddressBar();
        };

        toolbar.Controls.Add(backButton, 0, 0);
        toolbar.Controls.Add(forwardButton, 1, 0);
        toolbar.Controls.Add(refreshButton, 2, 0);
        toolbar.Controls.Add(addressTextBox, 3, 0);
        toolbar.Controls.Add(goButton, 4, 0);

        UpdateBrowserChrome();
        return toolbar;
    }

    static Button CreateToolbarButton(string text) =>
        new() {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(2),
        };

    void NavigateFromAddressBar()
    {
        if (webView?.CoreWebView2 == null || addressTextBox == null)
            return;

        string input = addressTextBox.Text;
        if (!TryNormalizeUserUrl(input, out string url, out string error))
        {
            Console.WriteLine(error);
            addressTextBox.Text = error;
            return;
        }

        webView.CoreWebView2.Navigate(url);
    }

    static bool TryNormalizeUserUrl(string input, out string url, out string error)
    {
        url = "";
        error = "";
        string trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Navigation blocked: empty URL.";
            return false;
        }

        if (trimmed.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            url = "about:blank";
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
            trimmed = $"https://{trimmed}";

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out uri))
        {
            error = $"Navigation blocked: invalid URL '{input}'.";
            return false;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            url = uri.AbsoluteUri;
            return true;
        }

        error = $"Navigation blocked: unsupported scheme '{uri.Scheme}'.";
        return false;
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (isDisposing)
            return;

        e.Cancel = true;
        HideAndUnloadCurrentPage();
    }

    void HideAndUnloadCurrentPage()
    {
        if (webView?.CoreWebView2 != null)
        {
            try
            {
                webView.CoreWebView2.Stop();
                webView.CoreWebView2.Navigate("about:blank");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        isNavigating = false;
        UpdateBrowserChrome("about:blank");
        form?.Hide();
    }

    void ShowBrowserWindow()
    {
        if (form == null || form.IsDisposed)
            return;

        if (form.WindowState == FormWindowState.Minimized)
            form.WindowState = FormWindowState.Normal;
        form.Show();
        form.Activate();
    }

    void UpdateBrowserChrome(string? overrideUrl = null)
    {
        bool ready = webView?.CoreWebView2 != null;
        if (backButton != null)
            backButton.Enabled = ready && webView!.CoreWebView2.CanGoBack;
        if (forwardButton != null)
            forwardButton.Enabled = ready && webView!.CoreWebView2.CanGoForward;
        if (refreshButton != null)
            refreshButton.Enabled = ready;
        if (goButton != null)
            goButton.Enabled = ready;

        string currentUrl = overrideUrl ??
                            webView?.Source?.ToString() ??
                            webView?.CoreWebView2?.Source ??
                            "about:blank";
        if (addressTextBox != null)
            addressTextBox.Text = currentUrl;

        if (form != null)
        {
            string title = webView?.CoreWebView2?.DocumentTitle ?? "";
            form.Text = string.IsNullOrWhiteSpace(title) ? "Alife Browser" : $"Alife Browser - {title}";
        }
    }

    async void OnFormOnLoad(object? s, EventArgs e)
    {
        try
        {
            string userDataFolder = Path.Combine(AlifePath.StorageFolderPath, "WebView2Data");
            if (!Directory.Exists(userDataFolder))
                Directory.CreateDirectory(userDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

            await form!.InvokeAsync(async _ => {
                await webView!.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edge/122.0.0.0";
                webView.CoreWebView2.NewWindowRequested += (_, ev) => {
                    ev.Handled = true;
                    webView.CoreWebView2.Navigate(ev.Uri);
                };
                webView.CoreWebView2.NavigationStarting += (_, ev) => {
                    isNavigating = true;
                    UpdateBrowserChrome(ev.Uri);
                };
                webView.CoreWebView2.NavigationCompleted += (_, _) => {
                    isNavigating = false;
                    UpdateBrowserChrome();
                };
                webView.CoreWebView2.SourceChanged += (_, _) => UpdateBrowserChrome();
                webView.CoreWebView2.DocumentTitleChanged += (_, _) => UpdateBrowserChrome();
                UpdateBrowserChrome();
            });

            isLoaded = true;
            await Task.Run(() => {
                foreach (Func<Task> formTask in formTasks.GetConsumingEnumerable())
                {
                    if (form.IsDisposed)
                        break;

                    try
                    {
                        Task task = form.Invoke(formTask);
                        task.Wait();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}

public class AlifeForm : Form;
