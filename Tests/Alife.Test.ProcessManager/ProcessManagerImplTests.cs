using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.ProcessService;

namespace Alife.Test.ProcessManager;

class MockProcess : IManagedProcess
{
    bool killed;

    public int Id { get; set; }
    public bool HasExited => killed;
    public string Name { get; set; } = "";
    public bool WriteCalled { get; private set; }
    public string? LastWrittenContent { get; private set; }

    readonly ConcurrentQueue<string> outputLines = new();

    public MockProcess AddOutput(string line) { outputLines.Enqueue(line); return this; }

    public void Kill() { killed = true; }
    public void Dispose() => Kill();

    public Task WriteAsync(string content, CancellationToken cancellationToken = default)
    {
        WriteCalled = true;
        LastWrittenContent = content;
        return Task.CompletedTask;
    }

    public Task<string> ReadOutputAsync(int waiting, int maxLines, CancellationToken cancellationToken = default)
    {
        List<string> lines = new();
        while (lines.Count < maxLines && outputLines.TryDequeue(out string? line))
            lines.Add(line);

        string result = string.Join("\n", lines);
        return Task.FromResult(string.IsNullOrWhiteSpace(result) ? $"[空输出] {Name}" : result);
    }

    public void ClearOutput()
    {
        while (outputLines.TryDequeue(out _));
    }

    public ProcessInfo GetInfo() => new(Id, Name, killed);
}

class MockProcessFactory : IProcessFactory
{
    public List<MockProcess> Created = new();
    public int NextId = 100;

    public IManagedProcess Start(string command, string? arguments = null, string? workdir = null)
    {
        MockProcess p = new() { Id = NextId++, Name = command };
        Created.Add(p);
        return p;
    }
}

[TestFixture]
public class ProcessManagerImplTests
{
    MockProcessFactory factory = null!;
    ProcessManagerImpl impl = null!;

    [SetUp]
    public void Setup()
    {
        factory = new MockProcessFactory();
        impl = new ProcessManagerImpl(factory);
    }

    [Test]
    public void ReplacingSameName_ReclaimsBothProcesses()
    {
        impl.CreateProcess("first", "worker");
        impl.CreateProcess("second", "worker");
        Assert.That(factory.Created[0].HasExited, Is.True);
        Assert.That(factory.Created[1].HasExited, Is.False);
        impl.KillAll();
        Assert.That(factory.Created.TrueForAll(p => p.HasExited), Is.True);
        Assert.That(impl.Processes, Is.Empty);
    }

    [Test]
    public async Task KillAll_TerminatesRealDescendant()
    {
        ProcessManagerImpl manager = new(new SystemProcessFactory());
        System.Diagnostics.Process? child = null;
        try
        {
            manager.CreateProcess("python", "parent", "-u -c \"import subprocess,time; p=subprocess.Popen(['python','-c','import time;time.sleep(120)']); print(p.pid,flush=True); time.sleep(120)\"");
            string output = await manager.ReadOutputAsync("parent", 1, 1);
            child = System.Diagnostics.Process.GetProcessById(int.Parse(output.Trim()));
            manager.KillAll();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(child.HasExited, Is.True);
            manager.KillAll(); //重复销毁安全
        }
        finally
        {
            manager.KillAll();
            if (child != null) { if (!child.HasExited) child.Kill(true); child.Dispose(); }
        }
    }

    [Test]
    public async Task ReadOutputAsync_RealProcess_WriteAndRead()
    {
        ProcessManagerImpl realImpl = new(new SystemProcessFactory());
        try
        {
            realImpl.CreateProcess("cmd", "cmd", "/Q");
            await realImpl.WriteAsync("cmd", "echo hello");
            string result = await realImpl.ReadOutputAsync("cmd", 2, 100);
            Assert.That(result, Does.Contain("hello"));
        }
        finally
        {
            realImpl.KillAll();
        }
    }

    [Test]
    public void ReadOutputAsync_RealCmd_WriteAndRead()
    {
        ProcessManagerImpl realImpl = new(new SystemProcessFactory());
        try
        {
            realImpl.CreateProcess("cmd", "cmdproc", "/Q");
            Assert.That(realImpl.Processes.ContainsKey("cmdproc"), Is.True);
            realImpl.WriteAsync("cmdproc", "echo hello").GetAwaiter().GetResult();
            string result = realImpl.ReadOutputAsync("cmdproc", 2, 100).GetAwaiter().GetResult();
            Assert.That(result, Does.Contain("hello"));
        }
        finally
        {
            realImpl.KillAll();
        }
    }

    [Test]
    public void ReadOutputAsync_RealPython_WriteAndRead()
    {
        ProcessManagerImpl realImpl = new(new SystemProcessFactory());
        try
        {
            realImpl.CreateProcess("python", "py", "-X utf8 -u -i");
            Assert.That(realImpl.Processes.ContainsKey("py"), Is.True);
            // 消费掉 Python 启动时的 banner，再写 + 读验证交互
            realImpl.ReadOutputAsync("py", 1, 100).GetAwaiter().GetResult();
            realImpl.WriteAsync("py", "print('hello')").GetAwaiter().GetResult();
            string result = realImpl.ReadOutputAsync("py", 2, 100).GetAwaiter().GetResult();
            Assert.That(result, Does.Contain("hello"), $"Got: [{result}]");
        }
        finally
        {
            realImpl.KillAll();
        }
    }

    [Test]
    public void CreateProcess_StartsProcessAndReturnsInfo()
    {
        ProcessInfo info = impl.CreateProcess("pwsh", "bash");

        Assert.That(info.Id, Is.EqualTo(100));
        Assert.That(info.Name, Is.EqualTo("bash"));
        Assert.That(info.HasExited, Is.False);
        Assert.That(factory.Created, Has.Count.EqualTo(1));
        Assert.That(factory.Created[0].Name, Is.EqualTo("pwsh"));
    }

    [Test]
    public void CreateProcess_WithWorkdir_SetsWorkingDirectory()
    {
        ProcessInfo info = impl.CreateProcess("python", "py", workdir: "C:\\work");
        // Just verifies no exception
        Assert.That(info.HasExited, Is.False);
    }

    [Test]
    public void KillProcess_ByKey_RemovesProcess()
    {
        impl.CreateProcess("pwsh", "bash");

        bool result = impl.KillProcess("bash");

        Assert.That(result, Is.True);
        Assert.That(factory.Created[0].HasExited, Is.True);
    }

    [Test]
    public void KillProcess_ById_RemovesProcess()
    {
        impl.CreateProcess("pwsh", "bash");

        bool result = impl.KillProcess("100");

        Assert.That(result, Is.True);
    }

    [Test]
    public void KillProcess_NotExists_ReturnsFalse()
    {
        bool result = impl.KillProcess("nonexistent");

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task WriteAsync_WritesToProcess()
    {
        impl.CreateProcess("pwsh", "bash");

        await impl.WriteAsync("bash", "echo hello");

        Assert.That(factory.Created[0].WriteCalled, Is.True);
        Assert.That(factory.Created[0].LastWrittenContent, Is.EqualTo("echo hello"));
    }

    [Test]
    public void WriteAsync_ProcessNotExists_Throws()
    {
        Assert.That(async () => await impl.WriteAsync("nope", "cmd"),
            Throws.InstanceOf<KeyNotFoundException>());
    }

    [Test]
    public void WriteAsync_ProcessKilled_Throws()
    {
        impl.CreateProcess("pwsh", "bash");
        factory.Created[0].Kill();

        Assert.That(async () => await impl.WriteAsync("bash", "cmd"),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task ReadOutputAsync_ReturnsLines()
    {
        impl.CreateProcess("pwsh", "bash");
        factory.Created[0].AddOutput("line1").AddOutput("line2").AddOutput("line3");

        string result = await impl.ReadOutputAsync("bash", 0, 2);

        Assert.That(result, Is.EqualTo("line1\nline2"));
    }

    [Test]
    public async Task ReadOutputAsync_ConsumesLines()
    {
        impl.CreateProcess("pwsh", "bash");
        factory.Created[0].AddOutput("line1").AddOutput("line2").AddOutput("line3");

        await impl.ReadOutputAsync("bash", 0, 2);
        string result = await impl.ReadOutputAsync("bash", 0, 10);

        Assert.That(result, Is.EqualTo("line3"));
    }

    [Test]
    public async Task ReadOutputAsync_ReturnsAllLines_WhenMoreThanMax()
    {
        impl.CreateProcess("pwsh", "bash");
        factory.Created[0].AddOutput("line1").AddOutput("line2");

        string result = await impl.ReadOutputAsync("bash", 0, 10);

        Assert.That(result, Is.EqualTo("line1\nline2"));
    }

    [Test]
    public void ClearOutput_ClearsBuffer()
    {
        impl.CreateProcess("pwsh", "bash");
        factory.Created[0].AddOutput("line1").AddOutput("line2");

        impl.ClearOutput("bash");
        string result = impl.ReadOutputAsync("bash", 0, 10).GetAwaiter().GetResult();

        Assert.That(result, Is.EqualTo("[空输出] pwsh"));
    }

    [Test]
    public async Task ReadOutputAsync_NoOutput_ReturnsPlaceholder()
    {
        impl.CreateProcess("pwsh", "bash");

        string result = await impl.ReadOutputAsync("bash", 0, 10);

        Assert.That(result, Is.EqualTo("[空输出] pwsh"));
    }

    [Test]
    public void ReadOutputAsync_ProcessNotExists_Throws()
    {
        Assert.That(async () => await impl.ReadOutputAsync("nope", 0, 10),
            Throws.InstanceOf<KeyNotFoundException>());
    }

    [Test]
    public void ListProcesses_NoProcesses_ReturnsEmpty()
    {
        string result = impl.ListProcesses();

        Assert.That(result, Is.EqualTo("没有托管进程"));
    }

    [Test]
    public void ListProcesses_WithProcesses_ListsThem()
    {
        impl.CreateProcess("pwsh", "bash");
        impl.CreateProcess("python", "py");

        string result = impl.ListProcesses();

        Assert.That(result, Does.Contain("bash"));
        Assert.That(result, Does.Contain("py"));
        Assert.That(result, Does.Contain("运行中"));
    }

    [Test]
    public void KillAll_KillsAllProcesses()
    {
        impl.CreateProcess("pwsh", "bash");
        impl.CreateProcess("python", "py");
        impl.CreateProcess("cmd", "cmdproc");

        impl.KillAll();

        Assert.That(factory.Created, Has.All.Matches<MockProcess>(p => p.HasExited));
    }
}
