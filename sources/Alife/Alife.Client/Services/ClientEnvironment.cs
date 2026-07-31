using Alife.Platform;

namespace Alife.Components.Services;

/// <summary>
/// 客户端功能，可以实现自动帮用户配置软件运行环境。
/// </summary>
public static class ClientEnvironment
{
    public static bool IsVCRedistReady { get; private set; }
    public static bool IsPythonReady { get; private set; }
    public static bool IsDotNetSdkReady { get; private set; }
    public static bool IsCudaReady { get; private set; }

    /// <summary>
    /// 重新计算设置环境信息，在软件启动和环境变动（安装卸载）后应当执行一次
    /// </summary>
    public static void SyncEnvironment()
    {
        //检查 VC++
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "vcruntime140.dll");
            IsVCRedistReady = File.Exists(path);
        }

        //检查Dotnet
        {
            string output = AlifeUtility.Command("dotnet", "--list-sdks").StandardOutput;
            IsDotNetSdkReady = output.Contains("10.");
        }

        //检查python
        {
            string? pythonDir = FindPythonDir();
            IsPythonReady = pythonDir != null;
            if (pythonDir != null)
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (path.Contains(pythonDir) == false)
                    Environment.SetEnvironmentVariable("PATH", $"{pythonDir}{Path.PathSeparator}{path}");
            }

            static string? FindPythonDir()
            {
                //优先使用 Runtime 中的沙盒 python
                string runtimePy = Path.Combine(AlifePath.RuntimeFolderPath, "Python312");
                if (File.Exists(Path.Combine(runtimePy, "python.exe")))
                    return runtimePy;

                //否则尝试使用环境中的 Python
                string? paths = Environment.GetEnvironmentVariable("Path");
                if (paths != null)
                {
                    foreach (var path in paths.Split(";"))
                    {
                        if (File.Exists(Path.Combine(path, "python.exe")))
                            return path;
                    }
                }

                return null;
            }
        }

        //检查cuda
        {
            if (IsPythonReady == false)
            {
                IsCudaReady = false;
            }
            else
            {
                string output = AlifeUtility.Command("python", "-c \"import torch; print(torch.version.cuda or 'none')\"").StandardOutput;
                IsCudaReady = output.Contains("12.");
            }
        }
    }

    public static async Task InstallVCppRedistAsync(IProgress<string>? progress = null)
    {
        progress?.Report("正在下载 Visual C++ Redistributable...");
        string tempExe = Path.Combine(Path.GetTempPath(), "vc_redist.x64.exe");
        await AlifeUtility.DownloadFileAsync("https://aka.ms/vs/17/release/vc_redist.x64.exe", tempExe);

        progress?.Report("正在静默安装 Visual C++ Redistributable...");
        AlifeUtility.Command(tempExe, "/install /quiet /norestart");
        File.Delete(tempExe);

        progress?.Report("Visual C++ 安装完成");
    }
    public static async Task InstallDotNetSdkAsync(IProgress<string>? progress = null)
    {
        progress?.Report("正在下载 .NET SDK 10 安装包...");
        string tempExe = Path.Combine(Path.GetTempPath(), "dotnet-sdk-10.exe");
        await AlifeUtility.DownloadFileAsync("https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.301/dotnet-sdk-10.0.301-win-x64.exe", tempExe);

        progress?.Report("正在安装 .NET SDK 10...");
        AlifeUtility.Command(tempExe, "/install /quiet /norestart");
        File.Delete(tempExe);

        progress?.Report(".NET SDK 10 安装完成");
    }
    public static async Task InstallPythonAsync(IProgress<string>? progress = null)
    {
        string pyDir = Path.Combine(AlifePath.RuntimeFolderPath, "Python312");
        Directory.CreateDirectory(pyDir);

        progress?.Report("正在安装 Python 3.12 嵌入版...");
        await AlifeUtility.DownloadZipFileAsync(
            "https://repo.huaweicloud.com/python/3.12.10/python-3.12.10-embed-amd64.zip",
            pyDir);

        progress?.Report("配置 site-packages...");
        string pthFile = Path.Combine(pyDir, "python312._pth");
        if (File.Exists(pthFile))
        {
            string content = await File.ReadAllTextAsync(pthFile);
            content = content.Replace("#import site", "import site");
            await File.WriteAllTextAsync(pthFile, content);
        }

        string pyExe = Path.Combine(pyDir, "python.exe");

        progress?.Report("正在安装 pip...");
        string getPyUrl = "https://bootstrap.pypa.io/get-pip.py";
        string getPyPath = Path.Combine(Path.GetTempPath(), "get-pip.py");
        await AlifeUtility.DownloadFileAsync(getPyUrl, getPyPath);
        AlifeUtility.Command(pyExe, $"\"{getPyPath}\" --no-warn-script-location");

        progress?.Report("正在安装 setuptools / wheel...");

        AlifeUtility.Command(pyExe, "-m pip install --upgrade pip setuptools wheel --quiet --no-warn-script-location");

        progress?.Report("Python 3.12 安装完成");
    }
    public static void InstallCuda(IProgress<string>? progress = null)
    {
        if (IsPythonReady == false)
            throw new InvalidOperationException("请先安装 Python");

        progress?.Report("正在卸载已有 torch...");
        AlifeUtility.Command("python.exe", "-m pip uninstall torch torchvision -y");

        progress?.Report("正在安装 PyTorch 2.10.0 + CUDA 12.8（可能需要较长时间）...");
        string pytorchIndex = AlifeMirror.TransformUrl("--index-url https://download.pytorch.org/whl/cu128");
        string pipInstall = $"install torch==2.10.0+cu128 torchvision==0.25.0+cu128 {pytorchIndex}";
        AlifeUtility.Command("python.exe", $"-m pip {pipInstall}");

        progress?.Report("CUDA 安装完成");
    }
}