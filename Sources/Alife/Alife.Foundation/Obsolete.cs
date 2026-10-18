using System;

namespace Alife.Platform
{
    [Obsolete("请改用 Alife.Foundation.AlifePath")]
    public class AlifePath : Alife.Foundation.AlifePath;

    [Obsolete("请改用 Alife.Foundation.AlifeUtility")]
    public class AlifePlatform : Alife.Foundation.AlifeUtility;
}

namespace Alife.Foundation
{
    public class AlifePath
    {
        public static string StorageFolderPath => AlifeContext.StorageFolderPath;
        public static string RuntimeFolderPath => AlifeContext.RuntimeFolderPath;
        public static string TempFolderPath => AlifeContext.TempFolderPath;
    }

    public static partial class AlifeContext
    {
        public static string AppVersion => AlifeFoundation.Instance.AppVersion;
        public static string StorageFolderPath => AlifeFoundation.Instance.StorageFolderPath;
        public static string RuntimeFolderPath => AlifeFoundation.Instance.RuntimeFolderPath;
    }

    public partial class AlifeUtility
    {
        public static CommandResult Command(string fileName, string arguments) => AlifeFoundation.Instance.Command(fileName, arguments);
    }
}