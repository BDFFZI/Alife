using System.Diagnostics;
using System.Text;
using Alife.Foundation;

namespace Alife.Client.Android;

public sealed class AndroidAlifeFoundation : IAlifeFoundation
{
    public string AppVersion => MainApplication.ClientVersion;
    public string StorageFolderPath => storageFolderPath;
    public string RuntimeFolderPath => runtimeFolderPath;

    public AndroidAlifeFoundation()
    {
        string home = MainApplication.Instance?.FilesDir?.AbsolutePath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string root = Path.Combine(home, "Alife");
        string defaultStorage = Path.Combine(root, "Storage");
        string defaultRuntime = Path.Combine(root, "Runtime");

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            try
            {
#pragma warning disable CA1416
                if (global::Android.OS.Environment.IsExternalStorageManager)
#pragma warning restore CA1416
                {
                    string? documents = global::Android.OS.Environment
                        .GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDocuments)?
                        .AbsolutePath;
                    if (string.IsNullOrEmpty(documents) == false)
                        defaultStorage = Path.Combine(documents, "Alife", "Storage");
                }
            }
            catch
            {
                // 权限查询失败时继续使用应用私有目录。
            }
        }

        storageFolderPath = AlifeConfig.GetString("storage_path");
        if (string.IsNullOrWhiteSpace(storageFolderPath))
            storageFolderPath = defaultStorage;
        runtimeFolderPath = AlifeConfig.GetString("runtime_path");
        if (string.IsNullOrWhiteSpace(runtimeFolderPath))
            runtimeFolderPath = defaultRuntime;

        try
        {
            Directory.CreateDirectory(storageFolderPath);
        }
        catch (Exception e)
        {
            AlifeLog.LogWarning($"无法使用配置的 Storage 路径，回退到应用私有目录：{e.Message}");
            storageFolderPath = defaultStorage;
            Directory.CreateDirectory(storageFolderPath);
        }
        Directory.CreateDirectory(runtimeFolderPath);
    }

    public async Task OnInitializedAsync()
    {
        AndroidEnvironment.SyncEnvironment();
        await TryUseDocumentsStorageAsync();
        AndroidEnvironment.SyncEnvironment();
    }

    public CommandResult Command(string fileName, string arguments)
    {
        ProcessStartInfo start = new("/system/bin/sh") {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"{fileName} {arguments}");

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动命令进程。");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(output, error);

        return new CommandResult {
            ExitCode = process.ExitCode,
            StandardOutput = output.Result.Trim(),
            StandardError = error.Result.Trim(),
        };
    }

    public void SetStorageFolderPath(string path)
    {
        storageFolderPath = MigrateDirectory(storageFolderPath, path);
        AlifeConfig.SetString("storage_path", storageFolderPath);
    }

    public void SetRuntimeFolderPath(string path)
    {
        runtimeFolderPath = MigrateDirectory(runtimeFolderPath, path);
        AlifeConfig.SetString("runtime_path", runtimeFolderPath);
    }

    internal async Task TryUseDocumentsStorageAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30) == false)
            return;

        if (ShizukuBridge.IsAvailable && ShizukuBridge.HasPermission)
        {
            try
            {
                string packageName = MainApplication.Instance?.PackageName ?? "com.alife.client";
                await ShizukuBridge.ExecuteAsync($"appops set {packageName} MANAGE_EXTERNAL_STORAGE allow");
            }
            catch (Exception e)
            {
                AlifeLog.LogWarning("通过 Shizuku 授予所有文件访问权限失败：" + e.Message);
            }
        }

#pragma warning disable CA1416
        if (global::Android.OS.Environment.IsExternalStorageManager == false)
#pragma warning restore CA1416
        {
            AlifeLog.LogWarning("未授予所有文件访问权限，Storage 使用应用私有目录。");
            return;
        }

        string? documents = global::Android.OS.Environment
            .GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDocuments)?
            .AbsolutePath;
        if (string.IsNullOrEmpty(documents))
            return;

        string target = Path.Combine(documents, "Alife", "Storage");
        if (string.Equals(Path.GetFullPath(storageFolderPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            SetStorageFolderPath(target);
            AlifeLog.LogInformation($"Storage 已切换到文档目录: {target}");
        }
        catch (Exception e)
        {
            AlifeLog.LogError(new Exception($"Storage 迁移到文档目录失败，继续使用 {storageFolderPath}", e));
        }
    }

    static string MigrateDirectory(string oldPath, string newPath)
    {
        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            return oldPath;

        if (Directory.Exists(newPath) && Directory.GetFileSystemEntries(newPath).Length == 0)
            Directory.Delete(newPath);

        if (Directory.Exists(newPath))
        {
            AlifeLog.LogWarning("目标目录已存在且非空，保留目标目录内容，未迁移旧文件。");
            Directory.CreateDirectory(newPath);
            return newPath;
        }

        Directory.CreateDirectory(newPath);
        if (Directory.Exists(oldPath))
        {
            CopyDirectory(oldPath, newPath);
            try
            {
                Directory.Delete(oldPath, true);
            }
            catch (Exception e)
            {
                AlifeLog.LogWarning("旧目录清理失败：" + e.Message);
            }
        }
        return newPath;
    }

    static void CopyDirectory(string source, string target)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    string storageFolderPath;
    string runtimeFolderPath;
}
