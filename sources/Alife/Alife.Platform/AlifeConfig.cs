using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Alife.Platform;

public static class AlifeConfig
{
    public static string GetString(string key, string defaultValue = "")
    {
        return data.GetValueOrDefault(key, defaultValue);
    }
    public static void SetString(string key, string value)
    {
        data[key] = value;
        Save();
    }
    public static int GetInt(string key, int defaultValue = 0)
    {
        if (int.TryParse(GetString(key), out int result))
            return result;
        return defaultValue;
    }
    public static void SetInt(string key, int value)
    {
        SetString(key, value.ToString());
    }
    public static float GetFloat(string key, float defaultValue = 0f)
    {
        if (float.TryParse(GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            return result;
        return defaultValue;
    }
    public static void SetFloat(string key, float value)
    {
        SetString(key, value.ToString(CultureInfo.InvariantCulture));
    }
    public static bool GetBool(string key, bool defaultValue = false)
    {
        string value = GetString(key);
        if (bool.TryParse(value, out bool result))
            return result;
        if (value == "1")
            return true;
        if (value == "0")
            return false;
        return defaultValue;
    }
    public static void SetBool(string key, bool value)
    {
        SetString(key, value.ToString());
    }
    public static bool HasKey(string key)
    {
        return data.ContainsKey(key);
    }
    public static void Remove(string key)
    {
        if (data.Remove(key))
            Save();
    }
    public static void Clear()
    {
        data.Clear();
        Save();
    }

    static readonly string ConfigFilePath;
    static Dictionary<string, string> data = new();

    static AlifeConfig()
    {
        ConfigFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "alife", "config.json");
        Load();
    }

    static void Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                Dictionary<string, string>? loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (loaded is not null)
                    data = loaded;
            }
            else
            {
                data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
    static void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(ConfigFilePath);
            if (string.IsNullOrEmpty(directory) is false && Directory.Exists(directory) is false)
                Directory.CreateDirectory(directory);

            JsonSerializerOptions options = new() { WriteIndented = true };
            string json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AlifeConfig save failed: {ex.Message}");
        }
    }
}
