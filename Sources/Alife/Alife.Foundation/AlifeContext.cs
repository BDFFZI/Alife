using System;
using System.IO;

namespace Alife.Foundation;

public static partial class AlifeContext
{
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