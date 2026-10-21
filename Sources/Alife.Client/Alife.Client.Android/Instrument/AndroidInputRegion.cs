using Android.Runtime;
using Java.Lang.Reflect;
using View = Android.Views.View;
using Region = Android.Graphics.Region;
using Rect = Android.Graphics.Rect;

namespace Alife.Client.Android;

// Android has no public API for a WebView window's non-rectangular touch region.
// Keep interactive islands touchable even when the rest of the window passes through.
sealed class AndroidInputRegion : Java.Lang.Object, IInvocationHandler
{
    readonly View view;
    readonly Java.Lang.Class listenerType;
    readonly Java.Lang.Object listener;
    readonly Region region = new();
    readonly IntPtr regionField;
    bool enabled;

    public AndroidInputRegion(View view)
    {
        this.view = view;
        // Resolve JNI metadata on a managed worker, outside a Java reflection callback.
        regionField = Task.Run(() => {
            using var type = Java.Lang.Class.ForName("android.view.ViewTreeObserver$InternalInsetsInfo");
            return JNIEnv.GetFieldID(type.Handle, "touchableRegion", "Landroid/graphics/Region;");
        }).GetAwaiter().GetResult();
        listenerType = Java.Lang.Class.ForName("android.view.ViewTreeObserver$OnComputeInternalInsetsListener");
        listener = Proxy.NewProxyInstance(listenerType.ClassLoader, [listenerType], this);
        view.ViewTreeObserver!.Class.GetMethod("addOnComputeInternalInsetsListener", listenerType)
            .Invoke(view.ViewTreeObserver, listener);
    }

    public void Set(bool enabled, float[][] rectangles)
    {
        this.enabled = enabled;
        region.SetEmpty();
        foreach (float[] r in rectangles)
            region.InvokeOp(new Rect((int)Math.Floor(r[0]), (int)Math.Floor(r[1]),
                (int)Math.Ceiling(r[2]), (int)Math.Ceiling(r[3])), Region.Op.Union!);
        view.RequestLayout();
    }

    public Java.Lang.Object? Invoke(Java.Lang.Object? proxy, Method? method, Java.Lang.Object[]? args)
    {
        if (method?.Name == "onComputeInternalInsets" && args is [var info])
        {
            info.Class.GetMethod("setTouchableInsets", Java.Lang.Integer.Type!)
                .Invoke(info, Java.Lang.Integer.ValueOf(enabled ? 3 : 0));
            if (enabled)
            {
                using var target = GetObject<Region>(JNIEnv.GetObjectField(info.Handle, regionField), JniHandleOwnership.TransferLocalRef);
                target!.Set(region);
            }
        }
        return method?.Name switch {
            "hashCode" => Java.Lang.Integer.ValueOf(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this)),
            "equals" => Java.Lang.Boolean.ValueOf(args?[0]?.Handle == proxy?.Handle),
            "toString" => new Java.Lang.String(nameof(AndroidInputRegion)),
            _ => null,
        };
    }

    public void Detach()
    {
        view.ViewTreeObserver!.Class.GetMethod("removeOnComputeInternalInsetsListener", listenerType)
            .Invoke(view.ViewTreeObserver, listener);
        region.Dispose();
        listener.Dispose();
    }
}
