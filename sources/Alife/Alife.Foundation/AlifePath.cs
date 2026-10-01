using System;
using System.Diagnostics;
using System.IO;

namespace Alife.Foundation;

public class AlifePath
{
    public static string AppFolderPath => AlifeContext.AppFolderPath;
    public static string AppPath => AppFolderPath;
    public static string StorageFolderPath => AlifeContext.StorageFolderPath;
    public static string RuntimeFolderPath => AlifeContext.RuntimeFolderPath;
    public static string TempFolderPath => AlifeContext.TempFolderPath;
}