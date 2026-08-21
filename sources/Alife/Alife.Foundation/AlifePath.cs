using System;
using System.Diagnostics;
using System.IO;

namespace Alife.Foundation;

public class AlifePath
{
    public static string AppFolderPath { get; private set; }
    public static string AppPath { get; private set; }
    public static string StorageFolderPath { get; private set; }
    public static string RuntimeFolderPath { get; private set; }
    public static string TempFolderPath { get; }

    public static void SetStorageFolderPath(string path)
    {
        StorageFolderPath = MigrateDirectory(StorageFolderPath, path, "存储");
        AlifeConfig.SetString("storage_path", StorageFolderPath);
    }
    public static void SetRuntimeFolderPath(string path)
    {
        RuntimeFolderPath = MigrateDirectory(RuntimeFolderPath, path, "运行时");
        AlifeConfig.SetString("runtime_path", RuntimeFolderPath);
    }

    static AlifePath()
    {
        string exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule!.FileName);
        string realLaunchPath = AppContext.BaseDirectory;
        string? parentDirectory = Path.GetDirectoryName(realLaunchPath);
        while (parentDirectory != null)
        {
            if (File.Exists(Path.Combine(parentDirectory, exeName)))
                realLaunchPath = parentDirectory;
            parentDirectory = Path.GetDirectoryName(parentDirectory);
        }
        AppFolderPath = realLaunchPath;
        AppPath = Path.Combine(realLaunchPath, exeName);

        //老路径兼容
        string outputsFolderPath = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
        string rootFolderPath = Path.GetDirectoryName(outputsFolderPath) ?? "";

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        StorageFolderPath = Directory.Exists(Path.Combine(rootFolderPath, "Storage"))
            ? Path.Combine(rootFolderPath, "Storage")
            : Path.Combine(documentsPath, "Alife", "Storage");
        RuntimeFolderPath = Directory.Exists(Path.Combine(rootFolderPath, "Runtime"))
            ? Path.Combine(rootFolderPath, "Runtime")
            : Path.Combine(documentsPath, "Alife", "Runtime");
#if DEBUG
        TempFolderPath = Path.Combine(Path.GetTempPath(), "Alife.ClientDebug");
#else
        TempFolderPath = Path.Combine(Path.GetTempPath(), "Alife.Client");
#endif

        string configRuntime = AlifeConfig.GetString("runtime_path");
        if (!string.IsNullOrEmpty(configRuntime))
            RuntimeFolderPath = configRuntime;

        string configStorage = AlifeConfig.GetString("storage_path");
        if (!string.IsNullOrEmpty(configStorage))
            StorageFolderPath = configStorage;

        //尝试清理缓存
        if (Directory.Exists(TempFolderPath))
        {
            try
            {
                Directory.Delete(TempFolderPath, recursive: true);
            }
            catch (Exception e)
            {
                AlifeLog.LogWarning(e);
            }
        }

        Directory.CreateDirectory(StorageFolderPath);
        Directory.CreateDirectory(RuntimeFolderPath);
        Directory.CreateDirectory(TempFolderPath);
    }

    static string MigrateDirectory(string oldPath, string newPath, string label)
    {
        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            return oldPath;

        try
        {
            if (!Directory.Exists(newPath) && Directory.Exists(oldPath))
            {
                string? parent = Path.GetDirectoryName(newPath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                    Directory.CreateDirectory(parent);
                Directory.Move(oldPath, newPath);
                Console.WriteLine($"检测到{label}位置变更，数据已从 {oldPath} 迁移至 {newPath}");
            }
            else if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{label}位置迁移失败: {ex.Message}");
            if (!Directory.Exists(newPath)) Directory.CreateDirectory(newPath);
        }

        return newPath;
    }
}