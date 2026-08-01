using System.Diagnostics;
using Alife.Framework;
using Alife.Foundation;
using ElectronNET.API;
using Newtonsoft.Json.Linq;
using Process = System.Diagnostics.Process;

namespace Alife.Components.Services;
public record UpdateInfo(string Version, string? ReleaseNotes, string DownloadUrl);
public class ClientUpgrade(PluginSystem pluginSystem)
{
    public string CurrentVersion => pluginSystem.ClientVersion;
    public UpdateInfo? NewVersion { get; private set; }
    public async Task FetchNewVersion()
    {
        const string rawGitHubApiUrl = "https://api.github.com/repos/BDFFZI/Alife/releases/latest";
        string response = await AlifeUtility.FetchStringAsync(rawGitHubApiUrl);
        JObject json = JObject.Parse(response);

        string? tagName = json["tag_name"]?.ToString();
        if (tagName == null)
            throw new Exception("客户端版本同步失败，无法获取到标签信息。");

        string remoteVersion = tagName.TrimStart('v');
        if (new Version(remoteVersion) <= new Version(pluginSystem.ClientVersion))
        {
            NewVersion = null;
            return; //本地比云端新
        }

        string? body = json["body"]?.ToString();
        string? downloadUrl = json["assets"]?[0]?["browser_download_url"]?.ToString();
        if (string.IsNullOrEmpty(downloadUrl))
            throw new Exception("客户端版本同步失败，无法获取到下载地址。");

        NewVersion = new UpdateInfo(remoteVersion, body, downloadUrl);
    }
    public async Task ApplyNewVersion(Action<int>? onProgress = null)
    {
        if (NewVersion == null)
            throw new Exception("没有新版本。");

        string tempDir = Path.Combine(AlifePath.TempFolderPath, "Update");
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);

        string zipPath = Path.Combine(tempDir, "Alife.zip");
        await AlifeUtility.DownloadFileAsync(NewVersion.DownloadUrl, zipPath, (read, total) =>
        {
            if (total > 0)
                onProgress?.Invoke((int)(read * 100 / total));
        });

        string exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule!.FileName);
        string realLaunchPath = AppContext.BaseDirectory;
        string? parentDirectory = Path.GetDirectoryName(realLaunchPath);
        while (parentDirectory != null)
        {
            if (File.Exists(Path.Combine(parentDirectory, exeName)))
                realLaunchPath = parentDirectory;
            parentDirectory = Path.GetDirectoryName(parentDirectory);
        }

        string startDirectory = realLaunchPath;
        string exePath = Path.Combine(startDirectory, exeName);
        string psPath = Path.Combine(tempDir, "update.ps1");
        await File.WriteAllTextAsync(psPath,
            $$"""
              Write-Host '=== Alife Update ===' -ForegroundColor Cyan
              Write-Host ''

              $proc = Get-Process -Name '{{exeName.Replace(".exe", "")}}' -ErrorAction SilentlyContinue
              if ($proc) {
                  Write-Host 'Waiting for old process to exit...' -ForegroundColor Yellow
                  $proc | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
                  Start-Sleep -Seconds 2
              }

              Write-Host 'ZipPath:    {{zipPath}}'
              Write-Host 'CurrentDir: {{startDirectory}}'
              Write-Host ''
              if (-not (Test-Path '{{zipPath}}')) {
                  Write-Host 'ERROR: ZIP not found!' -ForegroundColor Red
                  Read-Host 'Press Enter to exit'
                  exit 1
              }

              $extractTemp = '{{tempDir}}\_extract_tmp'
              if (Test-Path $extractTemp) { Remove-Item $extractTemp -Recurse -Force }
              New-Item -ItemType Directory -Path $extractTemp -Force | Out-Null

              Write-Host 'Extracting to temp...' -ForegroundColor Yellow
              try {
                  Expand-Archive -Path '{{zipPath}}' -DestinationPath $extractTemp -Force
                  Write-Host 'Extraction succeeded.' -ForegroundColor Green
              } catch {
                  Write-Host "Extraction failed: $($_.Exception.Message)" -ForegroundColor Red
                  Read-Host 'Press Enter to exit'
                  exit 1
              }

              Write-Host 'Copying new files (overwrite)...' -ForegroundColor Yellow
              Copy-Item -Path "$extractTemp\*" -Destination '{{startDirectory}}' -Recurse -Force
              Remove-Item $extractTemp -Recurse -Force -ErrorAction SilentlyContinue

              Write-Host ''
              Write-Host 'Starting Alife...' -ForegroundColor Cyan
              cmd /c start "" "{{exePath}}"
              Write-Host 'Upgrade successful!' -ForegroundColor Green
              Start-Sleep -Seconds 2
              exit
              """);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -File \"{psPath}\"",
            CreateNoWindow = false,
            UseShellExecute = true
        });

        Electron.App.Exit();
    }
}