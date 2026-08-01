using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Alife.Foundation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Alife.Function.Browser;

public class BrowserWorker : IAsyncDisposable
{
    public bool IsNavigating => isNavigating;
    public bool IsLaunched => isLaunched;
    public bool HasActivePopup => popupStack.Count > 0;

    public Task<T> ExecuteTaskAsync<T>(Func<WebView2, Task<T>> action)
    {
        TaskCompletionSource<T> tcs = new();

        browserWindow!.Invoke(async () => {
            try
            {
                tcs.SetResult(await action(ActiveWebView!));
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        return tcs.Task;
    }
    public async Task ClearPopupWindows()
    {
        if (popupStack.Count == 0)
            return;

        await ExecuteTaskAsync(async _ => {
            foreach ((WebView2 _, Form form) in popupStack)
                form.Close();
            await Task.Run(() => {
                while (popupStack.Count > 0) {}
            });
            return 0;
        });
    }

    BrowserWindow? browserWindow;
    readonly Stack<(WebView2 WebView, Form Form)> popupStack = new();
    bool isNavigating;
    bool isLaunched;

    WebView2? ActiveWebView =>
        popupStack.Count > 0 ? popupStack.Peek().WebView : browserWindow?.WebView2;

    public BrowserWorker()
    {
        var thread = new Thread(() => {
            browserWindow = new(
                Path.Combine(AlifePath.RuntimeFolderPath, "WebView2Data"),
                OnBrowserLaunched
            );
            Application.Run(browserWindow);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }
    public async ValueTask DisposeAsync()
    {
        await ExecuteTaskAsync(_ => {
            Application.ExitThread();
            return Task.FromResult(0);
        });
    }

    void OnBrowserLaunched()
    {
        try
        {
            browserWindow!.CoreWebView2.NewWindowRequested += OnWebViewNewWindowRequested;
            browserWindow.CoreWebView2.NavigationStarting += OnWebViewNavigationStarting;
            browserWindow.CoreWebView2.NavigationCompleted += OnWebViewNavigationCompleted;
            isLaunched = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    void OnWebViewNavigationStarting(object? _, CoreWebView2NavigationStartingEventArgs ev)
    {
        isNavigating = true;
    }
    void OnWebViewNavigationCompleted(object? _, CoreWebView2NavigationCompletedEventArgs ev)
    {
        isNavigating = false;
    }

    async void OnWebViewNewWindowRequested(object? _, CoreWebView2NewWindowRequestedEventArgs ev)
    {
        try
        {
            if (browserWindow == null)
                return;

            if (IsSameOrigin(ev.Uri))
            {
                browserWindow.WebView2.CoreWebView2.Navigate(ev.Uri);
                ev.Handled = true;
                return;
            }

            //非同源弹窗有可能是第三方登入弹窗，这种必须新旧窗口共存才能登录
            CoreWebView2Deferral coreWebView2Deferral = ev.GetDeferral();
            try
            {
                //创建弹窗窗口和web视图
                var popupForm = new Form {
                    Text = "Alife.Client Popup",
                    Size = new System.Drawing.Size(800, 600),
                    StartPosition = FormStartPosition.CenterScreen,
                    WindowState = browserWindow.ShowInTaskbar ? FormWindowState.Normal : FormWindowState.Minimized
                };
                var popupWebView = new WebView2 { Dock = DockStyle.Fill };
                popupForm.Controls.Add(popupWebView);

                var loadedTcs = new TaskCompletionSource();
                popupForm.Load += (_, _) => loadedTcs.SetResult();
                popupForm.Show();
                await loadedTcs.Task;

                if (browserWindow.ShowInTaskbar)
                    popupForm.Activate();

                await popupWebView.EnsureCoreWebView2Async(browserWindow.CoreWebView2Environment);
                popupWebView.CoreWebView2.Settings.UserAgent = browserWindow.CoreWebView2.Settings.UserAgent;

                //存储到窗口栈
                popupStack.Push((popupWebView, popupForm));
                popupWebView.CoreWebView2.WindowCloseRequested += (_, _) => popupForm.Close();
                popupForm.FormClosing += (_, _) => { popupStack.Pop(); };

                //完成弹出式窗口
                ev.NewWindow = popupWebView.CoreWebView2;
                ev.Handled = true;
            }
            finally
            {
                coreWebView2Deferral.Complete();
            }

            bool IsSameOrigin(string url)
            {
                var mainSource = browserWindow.WebView2.Source;
                var mainHost = mainSource?.Host ?? "";
                if (mainHost.Length == 0)
                    return false;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var popupUri))
                    return false;

                return popupUri.Host.Equals(mainHost, StringComparison.OrdinalIgnoreCase) ||
                       popupUri.Host.EndsWith("." + mainHost, StringComparison.OrdinalIgnoreCase) ||
                       mainHost.EndsWith("." + popupUri.Host, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
