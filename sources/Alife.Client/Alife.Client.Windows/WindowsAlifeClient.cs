using System.Diagnostics;
using Alife.Foundation;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Newtonsoft.Json.Linq;
using MessageBoxOptions = ElectronNET.API.Entities.MessageBoxOptions;
using Process = System.Diagnostics.Process;

namespace Alife.Client;

public class WindowsAlifeClient : IAlifeClient
{
    /// <summary>本平台更新包在发布资源中的文件名。</summary>
    const string UpdateAssetName = "Alife.Client.zip";

    /// <summary>Electron 启动器文件名，位于安装根目录，由 electron-builder 的 executableName 决定。</summary>
    const string LauncherFileName = "Alife.Client.exe";

    public void Quit()
    {
        var options = new MessageBoxOptions("是否直接关闭应用？") {
            Title = "Alife",
            Buttons = ["取消", "是 - 直接关闭", "否 - 最小化到托盘"],
            Type = MessageBoxType.question,
        };
        var result = Electron.Dialog.ShowMessageBoxAsync(Program.MainWindow, options).Result;
        if (result.Response == 1) Electron.App.Exit();
        else if (result.Response == 2) Program.MainWindow.Hide();
    }
    public void Exit()
    {
        Electron.App.Exit();
    }
    public async Task<string?> ShowSelectDirectoryDialog()
    {
        var browserWindow = Electron.WindowManager.BrowserWindows.First();
        var options = new OpenDialogOptions {
            Title = "请选择文件夹",
            Properties = [OpenDialogProperty.openDirectory]
        };
        string[] result = await Electron.Dialog.ShowOpenDialogAsync(browserWindow, options);
        return result.FirstOrDefault();
    }
    public async Task UpgradeClient(string version, Action<int>? onProgress = null)
    {
        //按 tag 精确取该版本，而不是再取一次 latest，避免检查与安装之间版本漂移
        string response = await AlifeUtility.FetchStringAsync($"{ClientUpgrade.ReleasesApiUrl}/tags/v{version}", TimeSpan.FromSeconds(7));
        string? downloadUrl = (JObject.Parse(response)["assets"] as JArray)?
            .FirstOrDefault(asset => asset["name"]?.ToString() == UpdateAssetName)?["browser_download_url"]?.ToString();
        if (string.IsNullOrEmpty(downloadUrl))
            throw new Exception($"v{version} 的发布资源中不存在更新包 {UpdateAssetName}。");

        string updateDir = Path.Combine(AlifePath.TempFolderPath, "Update");
        if (Directory.Exists(updateDir))
            Directory.Delete(updateDir, true);

        string packagePath = Path.Combine(updateDir, UpdateAssetName);
        await AlifeUtility.DownloadFileAsync(downloadUrl, packagePath, (read, total) =>
        {
            if (total > 0)
                onProgress?.Invoke((int)(read * 100 / total));
        });

        // ElectronNET 布局：当前进程（.NET 宿主）位于 <安装根>\resources\bin\，
        // 而用户启动的是安装根目录下的 Electron 启动器，两者不在同一层，
        // 所以这里按目录层级直接推算安装根，不依赖进程自身 exe 的名字。
        string binDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string installRoot = Path.GetDirectoryName(Path.GetDirectoryName(binDir)) ?? binDir;
        string launcherPath = Path.Combine(installRoot, LauncherFileName);
        string processName = Path.GetFileNameWithoutExtension(launcherPath);
        string psPath = Path.Combine(updateDir, "update.ps1");

        // 交给独立脚本完成：等旧进程退出 -> 解包 -> 覆盖安装根目录 -> 重新拉起启动器。
        // 之所以要等 Electron 进程而不是本进程：被覆盖的正是 Electron 占用的那些文件。
        File.WriteAllText(psPath,
            $$"""
              Write-Host '=== Alife Update ===' -ForegroundColor Cyan
              Write-Host ''

              $proc = Get-Process -Name '{{processName}}' -ErrorAction SilentlyContinue
              if ($proc) {
                  Write-Host 'Waiting for old process to exit...' -ForegroundColor Yellow
                  $proc | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
                  Start-Sleep -Seconds 2
              }

              Write-Host 'PackagePath: {{packagePath}}'
              Write-Host 'InstallDir:  {{installRoot}}'
              Write-Host ''
              if (-not (Test-Path '{{packagePath}}')) {
                  Write-Host 'ERROR: package not found!' -ForegroundColor Red
                  Read-Host 'Press Enter to exit'
                  exit 1
              }

              $extractTemp = '{{updateDir}}\_extract_tmp'
              if (Test-Path $extractTemp) { Remove-Item $extractTemp -Recurse -Force }
              New-Item -ItemType Directory -Path $extractTemp -Force | Out-Null

              Write-Host 'Extracting to temp...' -ForegroundColor Yellow
              try {
                  Expand-Archive -Path '{{packagePath}}' -DestinationPath $extractTemp -Force
                  Write-Host 'Extraction succeeded.' -ForegroundColor Green
              } catch {
                  Write-Host "Extraction failed: $($_.Exception.Message)" -ForegroundColor Red
                  Read-Host 'Press Enter to exit'
                  exit 1
              }

              Write-Host 'Copying new files (overwrite)...' -ForegroundColor Yellow
              Copy-Item -Path "$extractTemp\*" -Destination '{{installRoot}}' -Recurse -Force
              Remove-Item $extractTemp -Recurse -Force -ErrorAction SilentlyContinue

              Write-Host ''
              Write-Host 'Starting Alife...' -ForegroundColor Cyan
              cmd /c start "" "{{launcherPath}}"
              Write-Host 'Upgrade successful!' -ForegroundColor Green
              Start-Sleep -Seconds 2
              exit
              """);

        Process.Start(new ProcessStartInfo {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -File \"{psPath}\"",
            CreateNoWindow = false,
            UseShellExecute = true
        });

        Exit();
    }
}
