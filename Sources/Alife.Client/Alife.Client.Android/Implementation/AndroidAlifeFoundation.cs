using System.Diagnostics;
using System.Text;
using Alife.Foundation;

namespace Alife.Client.Android;

sealed class AndroidAlifeFoundation : IAlifeFoundation
{
    public string AppVersion => MainApplication.ClientVersion;
    public string StorageFolderPath => storageFolderPath;
    public string RuntimeFolderPath => runtimeFolderPath;
    public string TempFolderPath => tempFolderPath;

    public AndroidAlifeFoundation()
    {
        string root = GetExternalRoot();
        string defaultStorage = Path.Combine(root, "Storage");
        string defaultRuntime = Path.Combine(root, "Runtime");
        tempFolderPath = Path.Combine(root, "Temp");

        storageFolderPath = AlifeConfig.GetString("storage_path");
        if (string.IsNullOrWhiteSpace(storageFolderPath))
            storageFolderPath = defaultStorage;
        runtimeFolderPath = AlifeConfig.GetString("runtime_path");
        if (string.IsNullOrWhiteSpace(runtimeFolderPath))
            runtimeFolderPath = defaultRuntime;
    }

    public void SetStorageFolderPath(string path)
    {
        storageFolderPath = MigrateDirectory(storageFolderPath, Path.GetFullPath(path));
        AlifeConfig.SetString("storage_path", storageFolderPath);
    }

    public void SetRuntimeFolderPath(string path)
    {
        runtimeFolderPath = MigrateDirectory(runtimeFolderPath, Path.GetFullPath(path));
        AlifeConfig.SetString("runtime_path", runtimeFolderPath);
    }

    internal async Task TryUseDocumentsStorageAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && ShizukuBridge.IsAvailable && ShizukuBridge.HasPermission)
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

        if (HasStorageAccess() == false)
            throw new InvalidOperationException("需要“所有文件访问权限”才能使用外部存储。请授予权限后重试。");

        tempFolderPath = Path.Combine(GetExternalRoot(), "Temp");

        try
        {
            Directory.CreateDirectory(storageFolderPath);
            Directory.CreateDirectory(runtimeFolderPath);
            Directory.CreateDirectory(tempFolderPath);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"无法访问存储目录：{e.Message}", e);
        }
    }

    static bool HasStorageAccess()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30) == false)
            return true;

#pragma warning disable CA1416
        return global::Android.OS.Environment.IsExternalStorageManager;
#pragma warning restore CA1416
    }

    static string GetExternalRoot()
    {
#pragma warning disable CA1416
        string? documents = global::Android.OS.Environment
            .GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDocuments)?
            .AbsolutePath;
#pragma warning restore CA1416
        if (string.IsNullOrEmpty(documents))
            throw new InvalidOperationException("无法获取外部存储 Documents 目录。请联系系统管理员。");
        return Path.Combine(documents, "Alife");
    }

    static string MigrateDirectory(string oldPath, string newPath)
    {
        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            return oldPath;

        if (Directory.Exists(newPath) && Directory.GetFileSystemEntries(newPath).Length == 0)
            Directory.Delete(newPath);

        if (Directory.Exists(newPath))
            return newPath;

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
                AlifeLog.LogWarning("旧目录删除失败：" + e.Message);
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
    string tempFolderPath;
}
