using System.Diagnostics;

namespace Alife.Client.Windows;

sealed class WindowsPythonProcess : IAlifeProcess
{
    public WindowsPythonProcess(Process process)
    {
        this.process = process;
        StandardInput = process.StandardInput;
        StandardOutput = process.StandardOutput;
        StandardError = process.StandardError;
        Completion = GetExitCodeAsync();
    }

    readonly Process process;

    public StreamWriter StandardInput { get; }
    public StreamReader StandardOutput { get; }
    public StreamReader StandardError { get; }
    public Task<int> Completion { get; }
    public bool HasExited => process.HasExited;

    public void Kill()
    {
        try
        {
            if (HasExited == false)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        Kill();
        StandardInput.Dispose();
        StandardOutput.Dispose();
        StandardError.Dispose();
        process.Dispose();
    }

    async Task<int> GetExitCodeAsync()
    {
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
