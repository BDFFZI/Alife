using System;

namespace Alife.Foundation;

public static class AlifeContext
{
    public static string AppVersion => AlifeFoundation.Instance.AppVersion;
    public static string StorageFolderPath => AlifeFoundation.Instance.StorageFolderPath;
    public static string RuntimeFolderPath => AlifeFoundation.Instance.RuntimeFolderPath;
    public static string TempFolderPath => AlifeFoundation.Instance.TempFolderPath;

    public static string PlatformName { get; } =
        OperatingSystem.IsAndroid() ? "Android" :
        OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsLinux() ? "Linux" :
        "Unknown";
}