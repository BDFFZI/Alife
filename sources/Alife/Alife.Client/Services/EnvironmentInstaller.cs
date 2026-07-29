using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Platform;

namespace Alife.Components.Services;



public class EnvironmentInstaller
{
    public bool IsVCRedistReady { get; private set; }
    public bool IsPythonReady { get; private set; }
    public bool IsDotNetSdkReady { get; private set; }
    public bool IsCudaReady { get; private set; }

    public string? PythonDir { get; private set; }

    public async Task CheckAllAsync()
    {
        await Task.WhenAll(
            CheckVCppRedistAsync(),
            Task.Run(CheckPython),
            CheckDotNetSdkAsync(),
            CheckCudaAsync()
        );
    }

    public Task CheckVCppRedistAsync()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "vcruntime140.dll");
        IsVCRedistReady = File.Exists(path);
        return Task.CompletedTask;
    }
    public void CheckPython()
    {
        PythonDir = FindPythonDir();
        IsPythonReady = PythonDir != null;
    }
    public async Task CheckDotNetSdkAsync()
    {
        // 先尝试系统 PATH 中的 dotnet
        string output = AlifeUtility.Command("dotnet", "--list-sdks");
        if (output.Contains("10."))
        {
            IsDotNetSdkReady = true;
            return;
        }

        // 再尝试 Program Files 路径
        string programFilesDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        if (File.Exists(programFilesDotnet))
        {
            output = AlifeUtility.Command($"\"{programFilesDotnet}\"", "--list-sdks");
            if (output.Contains("10."))
            {
                IsDotNetSdkReady = true;
                return;
            }
        }

        // 最后尝试 LocalAppData 路径
        string localDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet", "dotnet.exe");
        if (File.Exists(localDotnet))
        {
            output = AlifeUtility.Command($"\"{localDotnet}\"", "--list-sdks");
            if (output.Contains("10."))
            {
                IsDotNetSdkReady = true;
                return;
            }
        }

        IsDotNetSdkReady = false;
    }
    public async Task CheckCudaAsync()
    {
        if (PythonDir == null)
        {
            IsCudaReady = false;
            return;
        }

        try
        {
            string pyExe = Path.Combine(PythonDir, "python.exe");
            string output = AlifeUtility.Command(pyExe, "-c \"import torch; print(torch.version.cuda or 'none')\"");
            string trimmed = output.Trim();
            IsCudaReady = trimmed.Contains("12.");
        }
        catch
        {
            IsCudaReady = false;
        }
    }

    public async Task InstallVCppRedistAsync(IProgress<string>? progress = null)
    {
        progress?.Report("正在下载 Visual C++ Redistributable...");
        string tempExe = Path.Combine(Path.GetTempPath(), "vc_redist.x64.exe");
        await AlifeUtility.DownloadFileAsync("https://aka.ms/vs/17/release/vc_redist.x64.exe", tempExe);

        progress?.Report("正在静默安装 Visual C++ Redistributable...");
        AlifeUtility.Command(tempExe, "/install /quiet /norestart");

        try { File.Delete(tempExe); }
        catch {}

        await CheckVCppRedistAsync();
        progress?.Report("Visual C++ 安装完成");
    }
    public async Task InstallPythonAsync(IProgress<string>? progress = null)
    {
        string pyDir = Path.Combine(AlifePath.RuntimeFolderPath, "Python312");
        Directory.CreateDirectory(pyDir);

        progress?.Report("正在下载 Python 3.12 嵌入版...");
        await AlifeUtility.DownloadFileAsync(
            "https://repo.huaweicloud.com/python/3.12.10/python-3.12.10-embed-amd64.zip",
            Path.Combine(Path.GetTempPath(), "py.zip"));

        progress?.Report("正在解压 Python 3.12...");
        System.IO.Compression.ZipFile.ExtractToDirectory(
            Path.Combine(Path.GetTempPath(), "py.zip"), pyDir, overwriteFiles: true);
        try { File.Delete(Path.Combine(Path.GetTempPath(), "py.zip")); }
        catch {}

        progress?.Report("配置 site-packages...");
        string pthFile = Path.Combine(pyDir, "python312._pth");
        if (File.Exists(pthFile))
        {
            string content = await File.ReadAllTextAsync(pthFile);
            content = content.Replace("#import site", "import site");
            await File.WriteAllTextAsync(pthFile, content);
        }

        progress?.Report("正在安装 pip...");
        string getPyUrl = "https://bootstrap.pypa.io/get-pip.py";
        string getPyPath = Path.Combine(Path.GetTempPath(), "get-pip.py");
        await AlifeUtility.DownloadFileAsync(getPyUrl, getPyPath);

        AlifeUtility.Command(Path.Combine(pyDir, "python.exe"), $"\"{getPyPath}\" --no-warn-script-location");
        try { File.Delete(getPyPath); }
        catch {}

        progress?.Report("正在安装 setuptools / wheel...");
        string pyExe = Path.Combine(pyDir, "python.exe");
        AlifeUtility.Command(pyExe, "-m pip install --upgrade pip setuptools wheel --quiet --no-warn-script-location");

        PythonDir = pyDir;
        CheckPython();
        progress?.Report("Python 3.12 安装完成");
    }
    public async Task InstallDotNetSdkAsync(IProgress<string>? progress = null)
    {
        progress?.Report("正在下载 .NET SDK 10 安装包...");
        string tempExe = Path.Combine(Path.GetTempPath(), "dotnet-sdk-10.exe");
        await AlifeUtility.DownloadFileAsync("https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.301/dotnet-sdk-10.0.301-win-x64.exe", tempExe);

        progress?.Report("正在安装 .NET SDK 10...");
        AlifeUtility.Command(tempExe, "/install /quiet /norestart");

        try { File.Delete(tempExe); }
        catch {}

        await CheckDotNetSdkAsync();
        progress?.Report(".NET SDK 10 安装完成");
    }
    public async Task InstallCudaAsync(IProgress<string>? progress = null)
    {
        if (PythonDir == null)
        {
            throw new InvalidOperationException("请先安装 Python");
        }

        string pyExe = Path.Combine(PythonDir, "python.exe");

        progress?.Report("正在卸载已有 torch...");
        AlifeUtility.Command(pyExe, "-m pip uninstall torch torchvision -y");

        progress?.Report("正在安装 PyTorch 2.10.0 + CUDA 12.8（可能需要较长时间）...");
        string pytorchIndex = AlifeMirror.TransformUrl("--index-url https://download.pytorch.org/whl/cu128");
        string pipInstall = $"install torch==2.10.0+cu128 torchvision==0.25.0+cu128 {pytorchIndex}";
        AlifeUtility.Command(pyExe, $"-m pip {pipInstall}");

        await CheckCudaAsync();
        progress?.Report("CUDA 安装完成");
    }

    public static void SetupEnvironmentPaths()
    {
        List<string> paths = new();

        string? pythonDir = FindPythonDir();
        if (pythonDir != null) paths.Add(pythonDir);

        string dotnetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        if (Directory.Exists(dotnetDir)) paths.Add(dotnetDir);

        string dotnetLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
        if (Directory.Exists(dotnetLocal)) paths.Add(dotnetLocal);

        if (paths.Count > 0)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            string extra = string.Join(Path.PathSeparator, paths.Where(p => !path.Contains(p)));
            if (extra.Length > 0)
                Environment.SetEnvironmentVariable("PATH", $"{extra}{Path.PathSeparator}{path}");
        }
    }
    static string? FindPythonDir()
    {
        // 1. Check %LOCALAPPDATA%\Programs\Python\Python312
        string systemPy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312");
        if (File.Exists(Path.Combine(systemPy, "python.exe")))
            return systemPy;

        // 2. Check Runtime dir
        string runtimePy = Path.Combine(AlifePath.RuntimeFolderPath, "Python312");
        if (File.Exists(Path.Combine(runtimePy, "python.exe")))
            return runtimePy;

        return null;
    }
}
