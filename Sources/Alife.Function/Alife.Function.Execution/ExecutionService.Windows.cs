#if WINDOWS
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;

namespace Alife.Function.Execution;

public partial class ExecutionService
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("在Windows上执行 PowerShell")]
    public async Task Shell(string command, CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startInfo = new() {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in new[] {
                     "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
                     "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); " + command
                 })
            startInfo.ArgumentList.Add(argument);

        using Process process = new();
        process.StartInfo = startInfo;
        try
        {
            process.Start();
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 2)
        {
            throw new Exception("找不到 pwsh，请先安装 PowerShell 7 并确保其位于 PATH 中", e);
        }

        process.StandardInput.Close();
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await using CancellationTokenRegistration registration = cancellationToken.Register(() => {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignored
                }
            });

            await process.WaitForExitAsync(cancellationToken);
            string result = await output + await error;
            if (process.ExitCode != 0) throw new Exception(result);
            interactor.Poke("命令执行完成\n" + result);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new TimeoutException("命令执行堵塞或超时（注意不要写需要交互的代码）");
        }
    }
}
#endif
