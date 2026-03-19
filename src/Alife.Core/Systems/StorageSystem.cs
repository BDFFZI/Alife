using Newtonsoft.Json;

namespace Alife;

public class StorageSystem
{
    public string GetStoragePath()
    {
        string? storagePath = Environment.GetEnvironmentVariable("OneDrive");
        if (storagePath == null)
            storagePath = Path.GetRelativePath(".", AppContext.BaseDirectory);
        return $"{storagePath}/Alife.Storage".Replace(Path.DirectorySeparatorChar, '/');
    }

    public string GetTempPath(string filename)
    {
        string path = $"{Path.GetTempPath()}/{filename}";
        if (Directory.Exists(Path.GetDirectoryName(path)) == false)
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
    public string? GetJson(string key, string? defaultValue = null)
    {
        return GetValue(key, "json", defaultValue);
    }
    public void SetJson(string key, string value)
    {
        SetValue(key, "json", value);
    }
    public T? GetObject<T>(string key, T? defaultValue = default, JsonSerializerSettings? settings = null)
    {
        try
        {
            string? data = GetJson(key);
            if (string.IsNullOrWhiteSpace(data))
                return defaultValue;
            return JsonConvert.DeserializeObject<T>(data, settings);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return defaultValue;
        }
    }
    public void SetObject(string key, object value, JsonSerializerSettings? settings = null)
    {
        settings ??= new JsonSerializerSettings();
        settings.Formatting = Formatting.Indented;
        string data = JsonConvert.SerializeObject(value, settings);
        SetJson(key, data);
    }

    public string? GetText(string key, string? defaultValue = null)
    {
        return GetValue(key, "txt", defaultValue);
    }
    public void SetText(string key, string value)
    {
        SetValue(key, "txt", value);
    }

    public string? GetValue(string key, string type, string? defaultValue = null)
    {
        string path = $"{GetStoragePath()}/{key}.{type}";
        if (File.Exists(path))
            return File.ReadAllText(path);
        return defaultValue;
    }
    public void SetValue(string key, string type, string value)
    {
        string path = $"{GetStoragePath()}/{key}.{type}";
        if (Directory.Exists(Path.GetDirectoryName(path)) == false)
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value);
    }
}
