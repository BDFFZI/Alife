using System.Diagnostics;
using Alife.Foundation;

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

        // 直接启动安装包：本进程会在下面立即退出，安装包由安装器自己接管并静默安装、重启客户端。
        Process.Start(new ProcessStartInfo {
            FileName = packagePath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
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
        try
        {
            string result = WindowsCommand.Run("schtasks", $"/query /tn \"{AutoStartTaskName}\" /fo csv /nh").StandardOutput;
            return result.Contains(AutoStartTaskName);
        }
        catch
        {
            return false;
        }
    }
    public void SetAutoStart(bool value)
    {
        WindowsCommand.Run("schtasks", value
            ? $"/create /tn \"{AutoStartTaskName}\" /tr \"\\\"{LauncherPath}\\\"\" /sc onlogon /rl highest /f"
            : $"/delete /tn \"{AutoStartTaskName}\" /f");
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
    const string AutoStartTaskName = "Alife.Client.Windows";
    const string LauncherFileName = "Alife.Client.Windows.exe";
    static readonly string InstallRoot = AppContext.BaseDirectory;
    static readonly string LauncherPath = Path.Combine(InstallRoot, LauncherFileName);
}