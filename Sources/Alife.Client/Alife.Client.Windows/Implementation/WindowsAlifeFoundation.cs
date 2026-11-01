using System.Reflection;
using Alife.Foundation;

namespace Alife.Client.Windows;

class WindowsAlifeFoundation : IAlifeFoundation
{
    public string AppVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    public string StorageFolderPath => storageFolderPath;
    public string RuntimeFolderPath => runtimeFolderPath;
    public string TempFolderPath => tempFolderPath;


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
    public void SetTempFolderPath(string path)
    {
        tempFolderPath = Path.GetFullPath(path);
        Directory.CreateDirectory(tempFolderPath);
        AlifeConfig.SetString("temp_path", tempFolderPath);
    }

    string storageFolderPath;
    string runtimeFolderPath;
    string tempFolderPath;

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
        tempFolderPath = rootFolderPath != null
            ? Path.Combine(rootFolderPath, "Temp")
            : Path.Combine(documentsPath, "Alife", "Temp");

        //读取存储路径
        string configStorage = AlifeConfig.GetString("storage_path");
        if (!string.IsNullOrEmpty(configStorage))
            storageFolderPath = configStorage;
        string configRuntime = AlifeConfig.GetString("runtime_path");
        if (!string.IsNullOrEmpty(configRuntime))
            runtimeFolderPath = configRuntime;
        string configTemp = AlifeConfig.GetString("temp_path");
        if (!string.IsNullOrEmpty(configTemp))
            tempFolderPath = configTemp;

        Directory.CreateDirectory(storageFolderPath);
        Directory.CreateDirectory(runtimeFolderPath);
        Directory.CreateDirectory(tempFolderPath);
    }

    static string MigrateDirectory(string oldPath, string newPath)
    {
        string fullNewPath;
        try
        {
            //路径本身不合法时直接拒绝：把坏路径写进配置会让客户端下次启动就起不来
            fullNewPath = Path.GetFullPath(newPath);
            if (string.Equals(Path.GetFullPath(oldPath), fullNewPath, StringComparison.OrdinalIgnoreCase))
                return oldPath;
        }
        catch (Exception e)
        {
            AlifeLog.LogWarning($"路径无效，已保持原路径 {oldPath}。请换一个合法路径：{newPath}\n{e}");
            return oldPath;
        }

        try
        {
            if (Directory.Exists(fullNewPath) && Directory.GetFileSystemEntries(fullNewPath).Length == 0)
                Directory.Delete(fullNewPath);

            if (Directory.Exists(fullNewPath))
            {
                AlifeLog.LogWarning($"目标位置已有文件，未迁移旧文件。请手动把 {oldPath} 里的文件移动到 {fullNewPath}。");
                return fullNewPath; //目标位置存在时无法移动
            }

            Directory.Move(oldPath, fullNewPath);
            AlifeLog.LogInformation("目录已迁移。");
        }
        catch (Exception e)
        {
            AlifeLog.LogWarning($"目录迁移失败，请手动把 {oldPath} 里的文件移动到 {fullNewPath}，然后重启客户端。\n{e}");
        }
        return fullNewPath;
    }
}
