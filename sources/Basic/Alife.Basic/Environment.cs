namespace Alife.Basic;

public static class Environment
{
    public static string ModelsFolderPath { get; private set; }
    public static string PythonFolderPath { get; private set; }
    public static string PythonExecutablePath { get; private set; }
    public static string StorageFolderPath { get; private set; }
    public static string OutputsFolderPath { get; private set; }
    public static string TempFolderPath { get; private set; }

    static Environment()
    {
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current) && Directory.Exists(Path.Combine(current, "Outputs")) == false)
                current = Path.GetDirectoryName(current);
            if (current == null)
            {
                Terminal.LogError("无法确定项目根目录位置！");
                throw new Exception("无法确定项目根目录位置！");
            }

            ModelsFolderPath = Path.Combine(current, "Resources", "Models").Replace(Path.DirectorySeparatorChar, '/');
            PythonFolderPath = Path.Combine(current, "Resources", "Python").Replace(Path.DirectorySeparatorChar, '/');
            PythonExecutablePath = Path.Combine(PythonFolderPath, "Scripts", "python.exe").Replace(Path.DirectorySeparatorChar, '/');
            OutputsFolderPath = Path.Combine(current, "Outputs").Replace(Path.DirectorySeparatorChar, '/');
        }

        {
            string? oneDrivePath = System.Environment.GetEnvironmentVariable("OneDrive");
            if (string.IsNullOrEmpty(oneDrivePath) == false)
            {
                string path = Path.Combine(oneDrivePath, "Alife.Storage");
                StorageFolderPath = path.Replace(Path.DirectorySeparatorChar, '/');
            }
            else
            {
                string? current = AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(current) && string.IsNullOrWhiteSpace(Path.Combine(current, "storage")) == false)
                    current = Path.GetDirectoryName(current);
                if (current == null)
                    throw new DirectoryNotFoundException("storage directory not found");
                StorageFolderPath = Path.Combine(current, "models").Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        {
            string path = Path.Combine(Path.GetTempPath(), "Alife");
            string? dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            TempFolderPath = path.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
