namespace Alife;

public static class PathEnvironment
{
    public static string ModelsPath { get; private set; }

    public static string StoragePath { get; private set; }

    public static string TempPath { get; private set; }

    static PathEnvironment()
    {
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current) && Directory.Exists(Path.Combine(current, "models")) == false)
                current = Path.GetDirectoryName(current);
            if (current == null)
                throw new DirectoryNotFoundException("models directory not found");
            ModelsPath = Path.Combine(current, "models").Replace(Path.DirectorySeparatorChar, '/');
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
