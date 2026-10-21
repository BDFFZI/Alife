using Android.App;
using Android.Content;

namespace Alife.Client.Android;

/// <summary>把任意 Intent 的 Activity 结果以 Task 形式返回，供插件通用使用。</summary>
static class AndroidActivityResultBroker
{
    const int FirstRequestCode = 10000;
    const int LastRequestCode = ushort.MaxValue;

    public static async Task<(Result ResultCode, Intent? Data)> RequestAsync(
        Intent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        MainActivity activity = MainActivity.Current ?? throw new InvalidOperationException("Alife 主界面尚未创建。");
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<(Result, Intent?)> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int requestCode;
        lock (gate)
        {
            requestCode = AllocateRequestCode();
            pending.Add(requestCode, completion);
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => activity.StartActivityForResult(intent, requestCode));
            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (gate)
                pending.Remove(requestCode);
        }
    }

    public static bool Complete(int requestCode, Result resultCode, Intent? data)
    {
        TaskCompletionSource<(Result, Intent?)>? completion;
        lock (gate)
        {
            if (pending.Remove(requestCode, out completion) == false)
                return false;
        }
        completion.TrySetResult((resultCode, data));
        return true;
    }

    static int AllocateRequestCode()
    {
        for (int attempts = 0; attempts <= LastRequestCode - FirstRequestCode; attempts++)
        {
            int requestCode = nextRequestCode++;
            if (nextRequestCode > LastRequestCode)
                nextRequestCode = FirstRequestCode;
            if (pending.ContainsKey(requestCode) == false)
                return requestCode;
        }
        throw new InvalidOperationException("没有可用的 Activity 请求码。");
    }

    static readonly object gate = new();
    static readonly Dictionary<int, TaskCompletionSource<(Result, Intent?)>> pending = [];
    static int nextRequestCode = FirstRequestCode;
}
