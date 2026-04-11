using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DeskPet;

/// <summary>
/// 负责跨进程管道的透明收发（双向流收发器）
/// </summary>
public class PetProcess : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public event Action<IpcCommand>? InputReceived;
    public event Action<IpcEvent>? OutputReceived;

    public PetProcess(TextWriter writer, TextReader reader)
    {
        this.writer = writer;
        this.reader = reader;
    }

    public void SendInput(IpcCommand cmd)
    {
        Send(cmd);
    }

    public void SendOutput(IpcEvent ev)
    {
        Send(ev);
    }

    public void ListenInput()
    {
        StartListening(InputReceived);
    }

    public void ListenOutput()
    {
        StartListening(OutputReceived);
    }

    public void Dispose()
    {
        cts?.Cancel();
    }

    void Send(object msg)
    {
        string json = JsonSerializer.Serialize(msg, JsonOptions);
        writer.WriteLine(json);
        writer.Flush();
    }

    void StartListening<T>(Action<T>? callback)
    {
        cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        Task.Run(async () => {
            while (token.IsCancellationRequested == false)
            {
                string? line = await reader.ReadLineAsync(token);
                if (string.IsNullOrEmpty(line)) break;

                T? msg = JsonSerializer.Deserialize<T>(line, JsonOptions);
                if (msg != null) callback?.Invoke(msg);
            }
        }, token);
    }

    readonly TextWriter writer;
    readonly TextReader reader;
    CancellationTokenSource? cts;
}

// --- IPC Protocol ---
[JsonDerivedType(typeof(WindowMoveCommand), "window-move")]
[JsonDerivedType(typeof(GetPositionCommand), "get-position")]
[JsonDerivedType(typeof(BubbleCommand), "bubble")]
[JsonDerivedType(typeof(PlayExpressionCommand), "expression")]
[JsonDerivedType(typeof(MotionCommand), "motion")]
[JsonDerivedType(typeof(HideBubbleCommand), "hide-bubble")]
public abstract record IpcCommand;

public record WindowMoveCommand(double X, double Y, int Duration) : IpcCommand;

public record GetPositionCommand : IpcCommand;

public record BubbleCommand(string Text) : IpcCommand;

public record PlayExpressionCommand(string? Id) : IpcCommand;

public record MotionCommand(string Group, int Index) : IpcCommand;

public record HideBubbleCommand : IpcCommand;

[JsonDerivedType(typeof(ReadyEvent), "ready")]
[JsonDerivedType(typeof(HitEvent), "hit")]
[JsonDerivedType(typeof(ChatEvent), "chat")]
[JsonDerivedType(typeof(PokeEvent), "poke")]
[JsonDerivedType(typeof(ShakeEvent), "shake")]
[JsonDerivedType(typeof(MoveEvent), "move")]
[JsonDerivedType(typeof(PositionEvent), "position")]
[JsonDerivedType(typeof(DragRequestEvent), "drag-request")]
public abstract record IpcEvent;

public record ReadyEvent : IpcEvent;

public record HitEvent(List<string> Areas) : IpcEvent;

public record ChatEvent(string Text) : IpcEvent;

public record PokeEvent(string Text) : IpcEvent;

public record ShakeEvent : IpcEvent;

public record MoveEvent : IpcEvent;

public record PositionEvent(double X, double Y) : IpcEvent;

public record DragRequestEvent : IpcEvent;
