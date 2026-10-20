using System;
using System.IO;

namespace Alife.Foundation;

public static class AlifeContext
{
    public static string AppVersion => AlifeFoundation.Instance.AppVersion;
    public static string StorageFolderPath => AlifeFoundation.Instance.StorageFolderPath;
    public static string RuntimeFolderPath => AlifeFoundation.Instance.RuntimeFolderPath;

    public static string PlatformName { get; } =
        OperatingSystem.IsAndroid() ? "Android" :
        OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsLinux() ? "Linux" :
        "Unknown";

    public static string TempFolderPath { get; } =
#if DEBUG
        Path.Combine(Path.GetTempPath(), "Alife.ClientDebug");
#else
        Path.Combine(Path.GetTempPath(), "Alife.Client");
#endif


    static AlifeContext()
    {
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

        Directory.CreateDirectory(TempFolderPath);
    }
}