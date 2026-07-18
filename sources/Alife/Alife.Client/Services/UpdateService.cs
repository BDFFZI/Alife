using System.Diagnostics;
using System.IO;
using System.Reflection;
using Alife.Platform;
using ElectronNET.API;
using Newtonsoft.Json.Linq;
using Process=System.Diagnostics.Process;

namespace Alife.Components.Services;

public record UpdateInfo(string Version, string? ReleaseNotes, string DownloadUrl);

public class UpdateService
{
    const string RawGitHubApiUrl = "https://api.github.com/repos/BDFFZI/Alife/releases/latest";

    public string LocalVersion { get; }
    public string? RemoteVersion { get; private set; }
    public bool HasUpdate { get; private set; }
    public UpdateInfo? LatestUpdate { get; private set; }

    public UpdateService()
    {
        LocalVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        Task.Run(CheckForUpdateAsync).Wait();
    }

    public string GetCurrentVersion() => LocalVersion;

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            string response = await AlifePlatform.FetchStringAsync(RawGitHubApiUrl);
            JObject json = JObject.Parse(response);

            string? tagName = json["tag_name"]?.ToString();
            if (string.IsNullOrEmpty(tagName))
                return null;

            RemoteVersion = tagName.TrimStart('v');

            if (new Version(RemoteVersion) > new Version(LocalVersion))
            {
                HasUpdate = true;
                string? body = json["body"]?.ToString();
                string? downloadUrl = json["assets"]?[0]?["browser_download_url"]?.ToString();

                if (string.IsNullOrEmpty(downloadUrl) == false)
                {
                    LatestUpdate = new UpdateInfo(RemoteVersion, body, downloadUrl);
                    return LatestUpdate;
                }
            }
            else
            {
                HasUpdate = false;
            }
        }
        catch
        {
            // 网络异常静默忽略
        }

        return null;
    }

    public async Task ApplyUpdateAsync(UpdateInfo updateInfo, Action<int>? onProgress = null)
    {
        string tempDir = Path.Combine(AlifePath.TempFolderPath, "Update");
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);

        string zipPath = Path.Combine(tempDir, "Alife.zip");
        await AlifePlatform.DownloadFileAsync(updateInfo.DownloadUrl, zipPath, (read, total) => {
            if (total > 0)
                onProgress?.Invoke((int)(read * 100 / total));
        });

        string currentDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        string exeName = Path.GetFileName(exePath);
        string psPath = Path.Combine(tempDir, "update.ps1");
        File.WriteAllText(psPath, $$"""
                                    Write-Host '=== Alife Update ===' -ForegroundColor Cyan
                                    Write-Host ''

                                    $proc = Get-Process -Name '{{exeName.Replace(".exe", "")}}' -ErrorAction SilentlyContinue
                                    if ($proc) {
                                        Write-Host 'Waiting for old process to exit...' -ForegroundColor Yellow
                                        $proc | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
                                        Start-Sleep -Seconds 2
                                    }

                                    Write-Host 'ZipPath:    {{zipPath}}'
                                    Write-Host 'CurrentDir: {{currentDir}}'
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
                                    Copy-Item -Path "$extractTemp\*" -Destination '{{currentDir}}' -Recurse -Force
                                    Remove-Item $extractTemp -Recurse -Force -ErrorAction SilentlyContinue

                                    Write-Host ''
                                    Write-Host 'Starting Alife...' -ForegroundColor Cyan
                                    cmd /c start "" "{{exePath}}"
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

        Electron.App.Exit();
    }
}
