using Alife.Test;

namespace Alife;

public static class PathEnvironment
{
    public static string ModelsPath { get; private set; }
    public static string OutputPath { get; private set; }
    public static string PythonPath { get; private set; }
    public static string PythonExecutablePath { get; private set; }
    public static string StoragePath { get; private set; }
    public static string TempPath { get; private set; }

    static PathEnvironment()
    {
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current) && Directory.Exists(Path.Combine(current, "models")) == false)
                current = Path.GetDirectoryName(current);
            if (current == null)
            {
                Terminal.LogError("无法确定资源目录位置！");
                throw new Exception("无法确定资源目录位置！");
            }

            ModelsPath = Path.Combine(current, "models").Replace(Path.DirectorySeparatorChar, '/');
            OutputPath = Path.Combine(current, "outputs", "bin").Replace(Path.DirectorySeparatorChar, '/');
            PythonPath = Path.Combine(current, "python").Replace(Path.DirectorySeparatorChar, '/');
            PythonExecutablePath = Path.Combine(PythonPath, "Scripts", "python.exe").Replace(Path.DirectorySeparatorChar, '/');
        }

        {
            string? oneDrivePath = Environment.GetEnvironmentVariable("OneDrive");
            if (string.IsNullOrEmpty(oneDrivePath) == false)
            {
                string path = Path.Combine(oneDrivePath, "Alife.Storage");
                StoragePath = path.Replace(Path.DirectorySeparatorChar, '/');
            }
            else
            {
                string? current = AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(current) && string.IsNullOrWhiteSpace(Path.Combine(current, "storage")) == false)
                    current = Path.GetDirectoryName(current);
                if (current == null)
                    throw new DirectoryNotFoundException("storage directory not found");
                StoragePath = Path.Combine(current, "models").Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        {
            string path = Path.Combine(Path.GetTempPath(), "Alife");
            string? dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            TempPath = path.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
