using System.Diagnostics;
using Alife.Foundation;
using Newtonsoft.Json.Linq;

namespace Alife.Client.Windows;

class WindowsAlifeClient(WindowsAlifeFoundation windowsAlifeFoundation, WindowsEnvironment windowsEnvironment, IAlifeFoundation alifeFoundation)
    : IAlifeClient
{
    public async Task UpgradeClient(string version, Action<int>? onProgress = null)
    {
        //按 tag 精确取该版本，而不是再取一次 latest，避免检查与安装之间版本漂移
        string response = await AlifeUtility.FetchStringAsync($"{ClientUpgrade.ReleasesApiUrl}/tags/v{version}", TimeSpan.FromSeconds(7));
        string? downloadUrl = (JObject.Parse(response)["assets"] as JArray)?
            .FirstOrDefault(asset => asset["name"]?.ToString() == UpdateAssetName)?["browser_download_url"]?.ToString();
        if (string.IsNullOrEmpty(downloadUrl))
            throw new Exception($"v{version} 的发布资源中不存在更新包 {UpdateAssetName}。");

        string updateDir = Path.Combine(alifeFoundation.TempFolderPath, "Update");
        if (Directory.Exists(updateDir))
            Directory.Delete(updateDir, true);

        string packagePath = Path.Combine(updateDir, UpdateAssetName);
        await AlifeUtility.DownloadFileAsync(downloadUrl, packagePath, (read, total) => {
            if (total > 0)
                onProgress?.Invoke((int)(read * 100 / total));
        });

        string launcherPath = LauncherPath;
        string processName = Path.GetFileNameWithoutExtension(launcherPath);
        string psPath = Path.Combine(updateDir, "update.ps1");

        // 交给独立脚本完成：等旧进程退出 -> 解包 -> 覆盖安装根目录 -> 重新拉起。
        await File.WriteAllTextAsync(psPath,
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
              Write-Host 'InstallDir:  {{InstallRoot}}'
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
              Copy-Item -Path "$extractTemp\*" -Destination '{{InstallRoot}}' -Recurse -Force
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

    const string UpdateAssetName = "Alife.Client.zip";
    const string AutoStartTaskName = "Alife.Client.Windows";
    const string LauncherFileName = "Alife.Client.Windows.exe";
    static readonly string InstallRoot = AppContext.BaseDirectory;
    static readonly string LauncherPath = Path.Combine(InstallRoot, LauncherFileName);
}