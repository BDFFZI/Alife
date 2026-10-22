using Android.Content;
using Android.Provider;

namespace Alife.Client.Android;

/// <summary>调用系统安装器安装 APK。</summary>
static class AndroidPackageInstaller
{
    public static void Install(string apkPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(apkPath);
        if (File.Exists(apkPath) == false)
            throw new FileNotFoundException("APK 文件不存在。", apkPath);

        Context context = MainApplication.Instance!;
        if (context.PackageManager?.CanRequestPackageInstalls() == false)
        {
            Intent settings = new(Settings.ActionManageUnknownAppSources,
                global::Android.Net.Uri.Parse("package:" + context.PackageName));
            settings.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(settings);
            throw new InvalidOperationException("请允许 Alife 安装未知来源的应用，然后重试更新。");
        }

        global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            context, AndroidFileDialogs.Authority, new Java.IO.File(apkPath));
        Intent intent = new(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
