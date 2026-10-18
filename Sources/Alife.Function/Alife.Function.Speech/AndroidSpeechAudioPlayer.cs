#if ANDROID
using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Media;
using Android.OS;

namespace Alife.Function.Speech;

/// <summary>
/// Android 平台播放器：插件直接调用进程内的 MediaPlayer，无需客户端注入。
/// </summary>
public class AndroidSpeechAudioPlayer : ISpeechAudioPlayer
{
    public async Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Handler handler = new(Looper.MainLooper!);
        handler.Post(() => {
            try
            {
                Release();
                MediaPlayer created = new();
                player = created;
                created.SetAudioAttributes(new AudioAttributes.Builder()!
                    .SetUsage(AudioUsageKind.Media)!
                    .SetContentType(AudioContentType.Speech)!
                    .Build()!);
                created.SetDataSource(filePath);
                created.Prepare();
                created.Completion += (_, _) => {
                    Release();
                    completion.TrySetResult();
                };
                created.Error += (_, args) => {
                    Release();
                    completion.TrySetException(new InvalidOperationException($"MediaPlayer error: {args.What}/{args.Extra}"));
                };
                created.Start();
            }
            catch (Exception e)
            {
                Release();
                completion.TrySetException(e);
            }
        });

        using CancellationTokenRegistration registration = cancellationToken.Register(() => {
            handler.Post(Release);
            completion.TrySetCanceled(cancellationToken);
        });
        await completion.Task;
    }

    public void Stop()
    {
        Handler handler = new(Looper.MainLooper!);
        handler.Post(Release);
    }

    void Release()
    {
        MediaPlayer? current = player;
        player = null;
        if (current == null)
            return;
        try
        {
            current.Stop();
        }
        catch
        {
            // 已结束或未开始时 Stop 会抛异常，忽略。
        }
        current.Release();
        current.Dispose();
    }

    MediaPlayer? player;
}
#endif
