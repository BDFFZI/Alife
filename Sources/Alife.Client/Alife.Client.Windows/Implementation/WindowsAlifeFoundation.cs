using System.Reflection;
using Alife.Foundation;

namespace Alife.Client.Windows;

class WindowsAlifeFoundation : IAlifeFoundation
{
    public string AppVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    public string StorageFolderPath => storageFolderPath;
    public string RuntimeFolderPath => runtimeFolderPath;

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

    string storageFolderPath;
    string runtimeFolderPath;

    public WindowsAlifeFoundation()
    {
        //老路径兼容
        string? rootFolderPath = Path.GetDirectoryName(AppContext.BaseDirectory);
        while (rootFolderPath != null)
        {
            if (Path.Exists(Path.Combine(rootFolderPath, "Storage")))
                break;
            rootFolderPath = Path.GetDirectoryName(rootFolderPath);
        }

        //设置默认路径
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        storageFolderPath = rootFolderPath != null
            ? Path.Combine(rootFolderPath, "Storage")
            : Path.Combine(documentsPath, "Alife", "Storage");
        runtimeFolderPath = rootFolderPath != null
            ? Path.Combine(rootFolderPath, "Runtime")
            : Path.Combine(documentsPath, "Alife", "Runtime");

        //读取存储路径
        string configStorage = AlifeConfig.GetString("storage_path");
        if (!string.IsNullOrEmpty(configStorage))
            storageFolderPath = configStorage;
        string configRuntime = AlifeConfig.GetString("runtime_path");
        if (!string.IsNullOrEmpty(configRuntime))
            runtimeFolderPath = configRuntime;

        Directory.CreateDirectory(storageFolderPath);
        Directory.CreateDirectory(runtimeFolderPath);
    }

    static string MigrateDirectory(string oldPath, string newPath)
    {
        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            return oldPath;
        if (Directory.Exists(newPath) && Directory.GetFileSystemEntries(newPath).Length == 0)
            Directory.Delete(newPath);

        if (Directory.Exists(newPath))
        {
            AlifeLog.LogWarning("路径已更新，但由于目标位置已有文件，故未迁移旧文件。");
            return newPath; //目标位置存在时无法移动
        }

        Directory.Move(oldPath, newPath);
        AlifeLog.LogInformation("目录已迁移。");
        return newPath;
    }
}