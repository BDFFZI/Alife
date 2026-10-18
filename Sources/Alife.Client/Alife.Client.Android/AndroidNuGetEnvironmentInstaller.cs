using System.Diagnostics;
using System.Text;
using Alife.Foundation;
using Alife.Framework;

namespace Alife.Client.Android;

public sealed class AndroidNuGetEnvironmentInstaller(CSharpCompiler csharpCompiler) : NuGetEnvironmentInstaller(csharpCompiler)
{
    protected override string GetNuGetPropertyGroup()
    {
        // 继续使用 termux 的 net10.0 SDK；Android 专属 API 由插件系统在编译时提供。
        return """
               <TargetFramework>net10.0</TargetFramework>
               """;
    }

    protected override void RestoreNuGetPackages(string projectFile)
    {
        DotNetInstallation installation = new();
        if (Directory.Exists(installation.SdkPath) == false)
            throw new InvalidOperationException(".NET SDK 尚未安装，无法还原 NuGet 环境。");

        string launcher = EnsureLauncher();
        ProcessStartInfo start = new("/system/bin/linker64") {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add(launcher);
        start.ArgumentList.Add("restore");
        start.ArgumentList.Add(projectFile);

        string? hostfxr = Directory.GetDirectories(Path.Combine(installation.SdkPath, "host", "fxr"))
            .OrderByDescending(path => Version.Parse(Path.GetFileName(path)))
            .FirstOrDefault();
        if (hostfxr == null)
            throw new InvalidOperationException("未找到 hostfxr。");

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        start.Environment["DOTNET_ROOT"] = installation.SdkPath;
        start.Environment["DOTNET_ROOT_ARM64"] = installation.SdkPath;
        start.Environment["ALIFE_HOSTFXR"] = Path.Combine(hostfxr, "libhostfxr.so");
        start.Environment["DOTNET_HOST_PATH"] = launcher;
        start.Environment["LD_LIBRARY_PATH"] = installation.LibraryPath;
        start.Environment["HOME"] = home;
        start.Environment["DOTNET_CLI_HOME"] = home;
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["MSBuildEnableWorkloadResolver"] = "false";
        start.Environment["SSL_CERT_FILE"] = installation.CertificatePath;
        string bin = Path.Combine(installation.DirectoryPath, "usr", "bin");
        start.Environment["PATH"] = bin + ":" + Environment.GetEnvironmentVariable("PATH");

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动 .NET restore。");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (process.WaitForExit(180000) == false)
        {
            process.Kill(true);
            throw new TimeoutException("NuGet restore 超时。");
        }
        Task.WaitAll(output, error);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"NuGet restore 失败（退出码 {process.ExitCode}）：\n{error.Result}\n{output.Result}");
    }

    static string EnsureLauncher()
    {
        string launcher = Path.Combine(AlifePath.RuntimeFolderPath, "dotnet-launcher");
        using Stream source = typeof(AndroidNuGetEnvironmentInstaller).Assembly.GetManifestResourceStream("SdkLauncher")
            ?? throw new InvalidOperationException("SdkLauncher 资源缺失。");
        using FileStream destination = File.Create(launcher);
        source.CopyTo(destination);
        destination.Close();
        File.SetUnixFileMode(launcher,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return launcher;
    }
}
