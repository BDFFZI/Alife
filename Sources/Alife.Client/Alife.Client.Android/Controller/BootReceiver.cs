using Android.App;
using Android.Content;

namespace Alife.Client.Android;

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
            return;
        string flag = Path.Combine(context.FilesDir!.AbsolutePath, "Alife", "AutoStart");
        if (File.Exists(flag) == false)
            return;
        context.StartForegroundService(new Intent(context, typeof(KeepAliveService)));
    }
}
