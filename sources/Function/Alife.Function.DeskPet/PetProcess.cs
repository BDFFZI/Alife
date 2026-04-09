using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.DeskPet;

/// <summary>
/// 负责进程生命周期管理与底层管道通讯 (StdIO)
/// </summary>
public class PetProcess : IDisposable
{
    public event Action<IpcCommand>? CommandReceived;
    public event Action<IpcEvent>? EventReceived;

    /// <summary>
    /// 自持构造：用于桌宠 EXE 内部，绑定到当前进程的 Console
    /// </summary>
    public PetProcess()
    {
        reader = Console.In;
        writer = Console.Out;
    }

    /// <summary>
    /// 托管构造：用于 AI 宿主，准备启动子进程
    /// </summary>
    public PetProcess(string exePath)
    {
        if (File.Exists(exePath) == false) throw new FileNotFoundException($"找不到桌宠程序: {exePath}");

        process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            }
        };
    }

    public void Launch()
    {
        if (process != null)
        {
            process.Start();
            reader = process.StandardOutput;
            writer = process.StandardInput;

            process.BeginErrorReadLine();
            process.ErrorDataReceived += (s, e) => {
                if (e.Data != null) Console.WriteLine($"[PetProcess Error] {e.Data}");
            };
        }
        
        StartListening();
    }

    public void StartListening()
    {
        cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (cts.IsCancellationRequested == false && reader != null)
            {
                try
                {
                    string? line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) break;

                    if (process == null) 
                    {
                        IpcCommand? c = JsonSerializer.Deserialize<IpcCommand>(line, jsonOptions);
                        if (c != null) CommandReceived?.Invoke(c);
                    }
                    else 
                    {
                        IpcEvent? e = JsonSerializer.Deserialize<IpcEvent>(line, jsonOptions);
                        if (e != null) EventReceived?.Invoke(e);
                    }
                }
                catch { }
            }
        });
    }

    public void Send(object msg)
    {
        try
        {
            string json = JsonSerializer.Serialize(msg, jsonOptions);
            writer?.WriteLine(json);
            writer?.Flush();
        }
        catch { }
    }

    public void Dispose()
    {
        cts?.Cancel();
        if (process != null && process.HasExited == false)
        {
            process.Kill();
            process.Dispose();
        }
    }

    public static readonly JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true };

    Process? process;
    TextReader? reader;
    TextWriter? writer;
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
public record GetPositionCommand() : IpcCommand;
public record BubbleCommand(string Text) : IpcCommand;
public record PlayExpressionCommand(string? Id) : IpcCommand;
public record MotionCommand(string Group, int Index) : IpcCommand;
public record HideBubbleCommand() : IpcCommand;

[JsonDerivedType(typeof(ReadyEvent), "ready")]
[JsonDerivedType(typeof(HitEvent), "hit")]
[JsonDerivedType(typeof(ChatEvent), "chat")]
[JsonDerivedType(typeof(PokeEvent), "poke")]
[JsonDerivedType(typeof(ShakeEvent), "shake")]
[JsonDerivedType(typeof(MoveEvent), "move")]
[JsonDerivedType(typeof(MoveFinishedEvent), "pmove-finished")]
[JsonDerivedType(typeof(PositionEvent), "position")]
[JsonDerivedType(typeof(DragRequestEvent), "drag-request")]
public abstract record IpcEvent;

public record ReadyEvent() : IpcEvent;
public record HitEvent(List<string> Areas) : IpcEvent;
public record ChatEvent(string Text) : IpcEvent;
public record PokeEvent(string Text) : IpcEvent;
public record ShakeEvent() : IpcEvent;
public record MoveEvent() : IpcEvent;
public record MoveFinishedEvent() : IpcEvent;
public record PositionEvent(double X, double Y) : IpcEvent;
public record DragRequestEvent() : IpcEvent;

public record InteractionItem
{
    public string? Text { get; set; }
    public string? Exp { get; set; }
    public MotionRef? Mtn { get; set; }
}

public record MotionRef(string Group, int Index);
