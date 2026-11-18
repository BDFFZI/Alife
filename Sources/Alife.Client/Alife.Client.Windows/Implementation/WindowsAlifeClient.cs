using System.Diagnostics;
using Alife.Foundation;
using Microsoft.Win32;

namespace Alife.Client.Windows;

class WindowsAlifeClient(WindowsAlifeFoundation windowsAlifeFoundation, WindowsEnvironment windowsEnvironment, IAlifeFoundation alifeFoundation)
    : IAlifeClient
{
    public async Task UpgradeClient(string version, Action<int>? onProgress = null)
    {
        //按 tag 精确取该版本，而不是再取一次 latest，避免检查与安装之间版本漂移
        string downloadUrl = $"{ClientUpgrade.ReleasesDownloadUrl}/v{version}/{UpdateAssetName}";

        string updateDir = Path.Combine(alifeFoundation.TempFolderPath, "Update");
        if (Directory.Exists(updateDir))
            Directory.Delete(updateDir, true);

        string packagePath = Path.Combine(updateDir, UpdateAssetName);
        await AlifeUtility.DownloadFileAsync(downloadUrl, packagePath, (read, total) => {
            if (total > 0)
                onProgress?.Invoke((int)(read * 100 / total));
        });

        // 启动安装包：显示安装向导，安装完成后由安装器启动客户端
        Process.Start(new ProcessStartInfo {
            FileName = packagePath,
            Arguments = "/NORESTART",
            UseShellExecute = true
        });
        Environment.Exit(0);
    }

    public void SetStorageFolderPath(string path)
    {
        windowsAlifeFoundation.SetStorageFolderPath(path);
    }
    public void SetRuntimeFolderPath(string path)
    {
        windowsAlifeFoundation.SetRuntimeFolderPath(path);
    }
    public void SetTempFolderPath(string path)
    {
        windowsAlifeFoundation.SetTempFolderPath(path);
    }

    public IReadOnlyList<string> GetRecommendedPluginIds()
    {
        return
        [
            //地基
            "Alife.Function.FunctionCaller",
            "Alife.Function.Mcp",
            "Alife.Function.Skill",
            //环境
            "Alife.Function.Developer",
            "Alife.Function.Memory",
            "Alife.Function.MessageFilter",
            "Alife.Function.SystemEvent",
            "Alife.Function.VirtualWorld",
            //工具
            "Alife.Function.Browser",
            "Alife.Function.FileService",
            "Alife.Function.Execution",
            "Alife.Function.TimedTask",
            "Alife.Function.Todo",
            //交互
            "Alife.Function.DeskPet",
            "Alife.Function.Language.OpenAI",
            "Alife.Function.Auditory.SenseVoice",
            "Alife.Function.Speech.VITS",
            "Alife.Function.Vision",
        ];
    }

    public bool GetAutoStart()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryPath);
        return (string?)key?.GetValue(AutoStartValueName) == AutoStartCommand;
    }
    public void SetAutoStart(bool value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(AutoStartRegistryPath);
        if (value)
            key.SetValue(AutoStartValueName, AutoStartCommand);
        else
            key.DeleteValue(AutoStartValueName, false);
    }

    public IReadOnlyList<EnvironmentItem> GetEnvironmentItems()
    {
        return windowsEnvironment.GetEnvironmentItems();
    }
    public Task InstallEnvironmentAsync(string id, IProgress<string>? progress = null)
    {
        return windowsEnvironment.InstallEnvironmentAsync(id, progress);
    }

    const string UpdateAssetName = "Alife.Client.Windows.exe";
    const string AutoStartRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AutoStartValueName = "Alife.Client.Windows";
    const string LauncherFileName = "Alife.Client.Windows.exe";
    static readonly string InstallRoot = AppContext.BaseDirectory;
    static readonly string LauncherPath = Path.Combine(InstallRoot, LauncherFileName);
    static readonly string AutoStartCommand = $"\"{LauncherPath}\"";
}
