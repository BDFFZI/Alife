using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Alife.Client.Android.Backend;

public sealed class NativeHostBridge
{
    readonly Channel<NativeHostCommand> commands = Channel.CreateUnbounded<NativeHostCommand>();
    readonly ConcurrentDictionary<string, TaskCompletionSource> pending = new();

    public async Task SendAsync(string action, string? windowId = null, HtmlWebWindowOptions? options = null, string? url = null)
    {
        string id = Guid.NewGuid().ToString("N");
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await commands.Writer.WriteAsync(new(id, action, windowId, options, url));
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { pending.TryRemove(id, out _); }
    }

    public ValueTask<NativeHostCommand> ReadAsync(CancellationToken cancellationToken) => commands.Reader.ReadAsync(cancellationToken);
    public void Complete(NativeHostResult result)
    {
        if (!pending.TryGetValue(result.Id, out var completion)) return;
        if (result.Error is null) completion.TrySetResult();
        else completion.TrySetException(new InvalidOperationException(result.Error));
    }
}
