using Alife.Client;
using Android.App;
using Android.Graphics;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Webkit;

namespace Alife.Client.Android;

sealed class LauncherWebWindow : IDisposable
{
    readonly HtmlWebWindowOptions options;
    readonly WebView view;
    readonly IWindowManager manager;
    Dialog? dialog;
    bool visible;
    bool disposed;

    public LauncherWebWindow(BackendService service, HtmlWebWindowOptions options, Uri origin, string? contentUrl = null)
    {
        this.options = options;
        if (options.Width <= 0 || options.Height <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        manager = service.GetSystemService(global::Android.Content.Context.WindowService)!.JavaCast<IWindowManager>()!;
        var context = options.CrossApplication ? (global::Android.Content.Context)service
            : MainActivity.Current ?? throw new InvalidOperationException("The main activity is not open.");
        view = MainActivity.CreateWebView(context);
        if (options.Transparent) view.SetBackgroundColor(Color.Transparent);
        if (!options.CrossApplication)
        {
            dialog = new Dialog(context);
            dialog.SetTitle(options.Title ?? "Alife");
            dialog.SetContentView(view);
        }
        Navigate(contentUrl is not null ? new Uri(origin, contentUrl).AbsoluteUri : options.Url);
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (visible) return;
        if (options.CrossApplication)
        {
            if (!Settings.CanDrawOverlays(view.Context))
            {
                var request = new global::Android.Content.Intent(Settings.ActionManageOverlayPermission,
                    global::Android.Net.Uri.Parse($"package:{view.Context!.PackageName}"));
                request.AddFlags(global::Android.Content.ActivityFlags.NewTask);
                view.Context.StartActivity(request);
                throw new UnauthorizedAccessException("Grant Alife permission to display over other apps, then retry.");
            }
            manager.AddView(view, new WindowManagerLayoutParams(options.Width, options.Height,
                WindowManagerTypes.ApplicationOverlay, WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutNoLimits,
                Format.Translucent) { Gravity = GravityFlags.Top | GravityFlags.Start });
        }
        else { dialog!.Show(); dialog.Window?.SetLayout(options.Width, options.Height); }
        visible = true;
    }

    public void Hide()
    {
        if (!visible) return;
        if (options.CrossApplication) manager.RemoveView(view);
        else dialog!.Hide();
        visible = false;
    }

    public void Navigate(string url)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (System.IO.Path.IsPathRooted(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            string path = System.IO.Path.GetFullPath(url);
            if (!File.Exists(path)) throw new FileNotFoundException("Window HTML file not found.", path);
            url = new Uri(path).AbsoluteUri;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) ||
            (target.Scheme is not ("http" or "https" or "file") && url != "about:blank"))
            throw new NotSupportedException("Web windows require an absolute HTTP, HTTPS or file URL, or about:blank.");
        view.Settings.AllowFileAccess = target.IsFile;
        view.LoadUrl(target.ToString());
    }

    public void Dispose()
    {
        if (disposed) return;
        Hide();
        disposed = true;
        dialog?.Dismiss();
        dialog?.Dispose();
        dialog = null;
        view.Destroy();
        view.Dispose();
    }
}
