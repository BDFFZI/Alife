using System.Diagnostics;
using System.Text;

namespace Alife.Client;

/// <summary>
/// Windows 客户端的命令执行工具。
/// </summary>
static class WindowsCommand
{
    public struct CommandResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; }
        public string StandardError { get; init; }
    }

    public static CommandResult Run(string fileName, string arguments)
    {
        ProcessStartInfo psi = new() {
            FileName = "cmd.exe",
            Arguments = $"/c chcp 65001 > nul && {fileName} {arguments}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using Process? process = Process.Start(psi);
        if (process == null)
            throw new Exception("进程不存在，无法启动。");

        StringBuilder stdoutBuilder = new();
        StringBuilder stderrBuilder = new();
        process.OutputDataReceived += (_, e) => {
            Console.WriteLine(e.Data);
            stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) => {
            Console.WriteLine(e.Data);
            stderrBuilder.AppendLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new CommandResult {
            ExitCode = process.ExitCode,
            StandardOutput = stdoutBuilder.ToString().Trim(),
            StandardError = stderrBuilder.ToString().Trim(),
        };
    }
}