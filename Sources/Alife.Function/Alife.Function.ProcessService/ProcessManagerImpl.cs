using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;



namespace Alife.Function.ProcessService;

public interface IProcessFactory
{
    IManagedProcess Start(string command, string? arguments = null, string? workdir = null);
}

public interface IManagedProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    string Name { get; }
    void Kill();
    Task WriteAsync(string content, CancellationToken cancellationToken = default);
    Task<string> ReadOutputAsync(int waiting, int maxLines, CancellationToken cancellationToken = default);
    void ClearOutput();
    ProcessInfo GetInfo();
}

public readonly record struct ProcessInfo(int Id, string Name, bool HasExited);

public class SystemProcessFactory : IProcessFactory
{
    public IManagedProcess Start(string command, string? arguments = null, string? workdir = null)
    {
        ProcessStartInfo psi = new()
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (string.IsNullOrEmpty(arguments) == false)
            psi.Arguments = arguments;

        if (string.IsNullOrEmpty(workdir) == false)
            psi.WorkingDirectory = workdir;

        Process process = new() { StartInfo = psi };
        process.Start();

        return new SystemManagedProcess(process, command);
    }
}

class SystemManagedProcess : IManagedProcess
{
    public const int MaxBufferLines = 10000;

    readonly Process process;

    public int Id => process.Id;
    public bool HasExited => process.HasExited;
    public string Name { get; }

    readonly ConcurrentQueue<string> lineBuffer = new();
    int readerCount;

    public SystemManagedProcess(Process process, string name)
    {
        this.process = process;
        Name = name;

        process.OutputDataReceived += OnOutput;
        process.ErrorDataReceived += OnOutput;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Interlocked.Exchange(ref readerCount, 2);
    }

    void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null)
        {
            Interlocked.Decrement(ref readerCount);
            return;
        }

        if (lineBuffer.Count >= MaxBufferLines)
            return;

        lineBuffer.Enqueue(e.Data);
    }

    public void Kill()
    {
        if (process.HasExited == false)
            process.Kill();
        process.Dispose();
    }

    public async Task WriteAsync(string content, CancellationToken cancellationToken = default)
    {
        await process.StandardInput.WriteLineAsync(content.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    public async Task<string> ReadOutputAsync(int waiting, int maxLines, CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(waiting), cancellationToken);

        List<string> lines = new();
        while (lines.Count < maxLines && lineBuffer.TryDequeue(out string? line))
            lines.Add(line);

        return lines.Count == 0
            ? $"[空输出] {Name}"
            : string.Join("\n", lines);
    }

    public void ClearOutput()
    {
        while (lineBuffer.TryDequeue(out _)) { }
    }

    public ProcessInfo GetInfo() => new(process.Id, Name, process.HasExited);

    public void Dispose() => Kill();
}

public class ProcessManagerImpl
{
    readonly ConcurrentDictionary<string, IManagedProcess> processes = new();
    readonly IProcessFactory factory;

    public IReadOnlyDictionary<string, IManagedProcess> Processes =>
        new Dictionary<string, IManagedProcess>(processes);

    public ProcessManagerImpl(IProcessFactory factory)
    {
        this.factory = factory;
    }

    public ProcessInfo CreateProcess(string command, string name, string? arguments = null, string? workdir = null)
    {
        IManagedProcess process = factory.Start(command, arguments, workdir);
        processes[name] = process;
        return new ProcessInfo(process.Id, name, false);
    }

    public bool KillProcess(string idOrName)
    {
        foreach (var kv in processes)
        {
            if (kv.Key.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Id.ToString() == idOrName)
            {
                kv.Value.Kill();
                processes.TryRemove(kv.Key, out _);
                return true;
            }
        }
        return false;
    }

    public async Task WriteAsync(string idOrName, string content, CancellationToken cancellationToken = default)
    {
        IManagedProcess? process = FindProcess(idOrName);
        if (process == null)
            throw new KeyNotFoundException($"进程不存在: {idOrName}");
        if (process.HasExited)
            throw new InvalidOperationException($"进程已退出: {process.Name}");

        await process.WriteAsync(content, cancellationToken);
    }

    public async Task<string> ReadOutputAsync(string idOrName, int waiting, int maxLines, CancellationToken cancellationToken = default)
    {
        IManagedProcess? process = FindProcess(idOrName);
        if (process == null)
            throw new KeyNotFoundException($"进程不存在: {idOrName}");

        return await process.ReadOutputAsync(waiting, maxLines, cancellationToken);
    }

    public void ClearOutput(string idOrName)
    {
        IManagedProcess? process = FindProcess(idOrName);
        if (process == null)
            throw new KeyNotFoundException($"进程不存在: {idOrName}");

        process.ClearOutput();
    }

    public string ListProcesses()
    {
        if (processes.IsEmpty)
            return "没有托管进程";

        StringBuilder sb = new();
        foreach (var kv in processes)
        {
            ProcessInfo info = kv.Value.GetInfo();
            string status = info.HasExited ? "已退出" : "运行中";
            sb.AppendLine($"- {kv.Key} (PID: {info.Id}) [{status}]");
        }
        return sb.ToString().TrimEnd();
    }

    public void KillAll()
    {
        foreach (var kv in processes)
        {
            try { kv.Value.Kill(); } catch { }
        }
        processes.Clear();
    }

    IManagedProcess? FindProcess(string idOrName)
    {
        if (int.TryParse(idOrName, out int pid))
        {
            foreach (var kv in processes)
            {
                if (kv.Value.Id == pid)
                    return kv.Value;
            }
        }

        processes.TryGetValue(idOrName, out IManagedProcess? process);
        return process;
    }
}
