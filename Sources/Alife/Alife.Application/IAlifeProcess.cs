namespace Alife.Client;

public interface IAlifeProcess : IDisposable
{
    StreamWriter StandardInput { get; }
    StreamReader StandardOutput { get; }
    StreamReader StandardError { get; }
    Task<int> Completion { get; }
    bool HasExited { get; }
    void Kill();

    /// <summary>等待进程结束并返回退出码；标准输出与标准错误会合并。</summary>
    async Task<AlifeProcessResult> WaitCompletion(CancellationToken cancellationToken = default)
    {
        StandardInput.Close();
        Task<string> output = StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = StandardError.ReadToEndAsync(cancellationToken);
        await using var registration = cancellationToken.Register(Kill);
        int exitCode = await Completion.WaitAsync(cancellationToken);
        return new AlifeProcessResult(exitCode, await output + await error);
    }
}

public sealed record AlifeProcessResult(int ExitCode, string Output);