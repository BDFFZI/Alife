using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Alife.Foundation;
using Newtonsoft.Json.Linq;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace Alife.Client.Windows;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial class WinFormsAlifeClient
{
    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    const int SM_CXSCREEN = 0; // 主屏幕宽度
    const int SM_CYSCREEN = 1; // 主屏幕高度

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();
}

public partial class WinFormsAlifeClient : IAlifeClient
{
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

    public bool GetAutoStart()
    {
        try
        {
            string result = AlifeUtility.Command("schtasks", $"/query /tn \"{AutoStartTaskName}\" /fo csv /nh").StandardOutput;
            return result.Contains(AutoStartTaskName);
        }
        catch
        {
            return false;
        }
    }
    public void SetAutoStart(bool value)
    {
        AlifeUtility.Command("schtasks", value
            ? $"/create /tn \"{AutoStartTaskName}\" /tr \"\\\"{LauncherPath}\\\"\" /sc onlogon /rl highest /f"
            : $"/delete /tn \"{AutoStartTaskName}\" /f");
    }

    public Task SetupEnvironment()
    {
        return Task.Run(WindowsEnvironment.Initialize);
    }
    public IReadOnlyList<EnvironmentItem> GetEnvironmentItems()
    {
        return WindowsEnvironment.GetEnvironmentItems();
    }
    public Task InstallEnvironmentAsync(string id, IProgress<string>? progress = null)
    {
        return WindowsEnvironment.InstallEnvironmentAsync(id, progress);
    }

    public Task RevealFolderAsync(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true });
        return Task.CompletedTask;
    }
    public Task RevealFileAsync(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = true });
        return Task.CompletedTask;
    }
    public Task<string?> SelectFolderAsync()
    {
        return Task.Run(() => {
            using FolderBrowserDialog dialog = new();
            dialog.Description = "请选择文件夹";
            dialog.ShowNewFolderButton = true;

            if (dialog.ShowDialog() == DialogResult.OK)
                return dialog.SelectedPath;
            return null;
        });
    }

    public Size GetScreenSize()
    {
        double width = GetSystemMetrics(SM_CXSCREEN);
        double height = GetSystemMetrics(SM_CYSCREEN);
        return new Size((int)width, (int)height);
    }
    public Point GetCursorPosition()
    {
        GetCursorPos(out POINT point);
        return new Point(point.X, point.Y);
    }
    public float GetDpiScale()
    {
        return (float)(GetDpiForSystem() / 96.0);
    }

    public async Task<IAlifeWindow> CreateWindowAsync(AlifeWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 每个 Html 二级窗口（桌宠等）一条专属 STA UI 线程，窗口之间、与主窗口互相隔离。
        TaskCompletionSource<IAlifeWindow> tcs = new();
        Thread thread = new(_ => {
            WinFormsAlifeWindow alifeWindow = new(options);
            alifeWindow.FormClosed += (_, _) => {
                Application.ExitThread();
            };
            tcs.SetResult(alifeWindow);
            Application.Run(alifeWindow);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = options.Title;
        thread.Start();
        return await tcs.Task;
    }

    const string UpdateAssetName = "Alife.Client.zip";
    const string AutoStartTaskName = "Alife.Client.Windows";
    const string LauncherFileName = "Alife.Client.Windows.exe";
    static readonly string InstallRoot = AppContext.BaseDirectory;
    static readonly string LauncherPath = Path.Combine(InstallRoot, LauncherFileName);
}