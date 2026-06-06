using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Alife.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Alife.Function.Browser;

/// <summary>
/// 在独立的 STA 线程中管理 WebView2，以支持后台执行浏览器任务。
/// 浏览器窗口默认可见，用户可实时观察 AI 的浏览行为并手动介入。
/// </summary>
public class WebViewWorker : IDisposable
{
    public bool IsNavigating => isNavigating;
    public bool IsLoaded => isLoaded;

    public Task<T> AddFormTask<T>(Func<WebView2, Task<T>> action)
    {
        if (window == null)
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

    Window? window;
    WebView2? webView;
    readonly BlockingCollection<Func<Task>> formTasks = new();
    bool isNavigating;
    bool isLoaded;

    public WebViewWorker()
    {
        var thread = new Thread(() => {
            try
            {
                window = new UnclosableWindow {
                    Title = "Alife.Client Browser",
                    Width = 1024,
                    Height = 768,
                    WindowState = WindowState.Minimized,
                    ShowInTaskbar = true,
                    ResizeMode = ResizeMode.CanResize,
                };
                webView = new WebView2();
                window.Content = webView;
                window.Loaded += OnWindowLoaded;
                window.Closing += (_, _) => System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();

                window.Show();
                System.Windows.Threading.Dispatcher.Run();
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
        formTasks.CompleteAdding();
        if (window != null)
            window.Dispatcher.Invoke(() => window.Close());
    }

    async void OnWindowLoaded(object? s, RoutedEventArgs e)
    {
        try
        {
            string userDataFolder = Path.Combine(AlifePath.RuntimeFolderPath, "WebView2Data");
            if (!Directory.Exists(userDataFolder))
                Directory.CreateDirectory(userDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

            await webView!.Dispatcher.InvokeAsync(async () => {
                await webView!.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edge/122.0.0.0";
                webView.CoreWebView2.NewWindowRequested += (_, ev) => {
                    ev.Handled = true;
                    webView.CoreWebView2.Navigate(ev.Uri);
                };
                webView.CoreWebView2.NavigationStarting += (_, ev) => isNavigating = true;
                webView.CoreWebView2.NavigationCompleted += (_, ev) => isNavigating = false;
            });

            isLoaded = true;
            await Task.Run(() => {
                foreach (Func<Task> formTask in formTasks.GetConsumingEnumerable())
                {
                    try
                    {
                        Task task = window!.Dispatcher.Invoke(formTask);
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
