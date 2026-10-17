using System.Diagnostics;
using Alife.Framework;

namespace Alife.Client.Android.Backend;

public sealed class AndroidPluginRuntime : IPluginRuntime
{
    public string GetNuGetPropertyGroup()
    {
        return """
               <TargetFramework>net10.0</TargetFramework>
               """;
    }
    public void RestoreNuGetPackages(string projectFile)
    {
        ProcessStartInfo start = new("/system/bin/linker64") {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("ALIFE_DOTNET_LAUNCHER")!);
        start.ArgumentList.Add("restore");
        start.ArgumentList.Add(projectFile);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start .NET restore.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180000)) { process.Kill(true); throw new TimeoutException("NuGet restore timed out."); }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0) throw new InvalidOperationException(output.Result + error.Result);
    }
}
