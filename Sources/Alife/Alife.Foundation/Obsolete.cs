using System;
using System.Diagnostics;
using System.Text;

namespace Alife.Platform
{
    [Obsolete("请改用 Alife.Foundation.AlifePath")]
    public class AlifePath : Alife.Foundation.AlifePath;

    [Obsolete("请改用 Alife.Foundation.AlifeUtility")]
    public class AlifePlatform : Foundation.AlifeUtility;
}

namespace Alife.Foundation
{
    public class AlifePath
    {
        // [Obsolete("请改用IAlifeFoundation")]
        public static string StorageFolderPath => AlifeFoundation.Instance.StorageFolderPath;
        // [Obsolete("请改用IAlifeFoundation")]
        public static string RuntimeFolderPath => AlifeFoundation.Instance.RuntimeFolderPath;
        // [Obsolete("请改用IAlifeFoundation")]
        public static string TempFolderPath => AlifeFoundation.Instance.TempFolderPath;
    }

    public static partial class AlifeContext
    {
        // [Obsolete("请改用IAlifeFoundation")]
        public static string AppVersion => AlifeFoundation.Instance.AppVersion;
    }

    public partial class AlifeUtility
    {
        [Obsolete("该方法为非跨平台功能，已弃用，有需要请自行实现。")]
        public static void Command(string fileName, string arguments)
        {
            ProcessStartInfo psi = new() {
                FileName = "cmd.exe",
                Arguments = $"/c chcp 65001 > nul && {fileName} {arguments}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using Process? process = Process.Start(psi);
            if (process == null)
                throw new Exception("进程不存在，无法启动。");

            StringBuilder stdoutBuilder = new();
            StringBuilder stderrBuilder = new();
            process.OutputDataReceived += (_, e) => {
                Console.WriteLine(e.Data);
                stdoutBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) => {
                Console.WriteLine(e.Data);
                stderrBuilder.AppendLine(e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
        }
    }
}