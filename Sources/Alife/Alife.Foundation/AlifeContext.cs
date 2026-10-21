using System;

namespace Alife.Foundation;

public  static partial class AlifeContext
{
  
    public static string PlatformName { get; } =
        OperatingSystem.IsAndroid() ? "Android" :
        OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsLinux() ? "Linux" :
        "Unknown";
}