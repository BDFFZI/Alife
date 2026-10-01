using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alife.Foundation;
using Newtonsoft.Json;

namespace Alife.Framework;

public class StorageSystem
{
    public string? GetProperty(string key, string? defaultValue = null)
    {
        return GetValue("Settings/StringStorage/" + key, "txt", defaultValue);
    }
    public void SetProperty(string key, string value)
    {
        SetValue("Settings/StringStorage/" + key, "txt", value);
    }
    public T? GetSetting<T>(string key, T? defaultValue = default)
    {
        return GetObject("Settings/" + key, defaultValue);
    }
    public void SetSetting(string key, object value)
    {
        SetObject("Settings/" + key, value);
    }

    public string[] GetSubFolders(string path)
    {
        string absolutePath = $"{AlifePath.StorageFolderPath}/{path}";
        if (Directory.Exists(absolutePath) == false)
            return [];
        return Directory.GetDirectories(absolutePath)
            .Select(Path.GetFileNameWithoutExtension)
            .Cast<string>()
            .ToArray();
    }
    public string GetObjectAbsolutePath(string path)
    {
        return Path.Combine(AlifePath.StorageFolderPath, path + ".json");
    }
    public T? GetObject<T>(string path, T? defaultValue = default, JsonSerializerSettings? settings = null)
    {
        try
        {
            string? data = GetValue(path, "json");
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
    public void SetObject(string path, object value, JsonSerializerSettings? settings = null)
    {
        settings ??= new JsonSerializerSettings();
        settings.Formatting = Formatting.Indented;
        string data = JsonConvert.SerializeObject(value, settings);
        SetValue(path, "json", data);
    }
    public void DeleteObject(string path)
    {
        DeleteValue(path, "json");
    }

    string? GetValue(string path, string type, string? defaultValue = null)
    {
        string absolutePath = $"{AlifePath.StorageFolderPath}/{path}.{type}";
        if (File.Exists(absolutePath))
            return File.ReadAllText(absolutePath);
        return defaultValue;
    }
    void SetValue(string path, string type, string value)
    {
        string absolutePath = $"{AlifePath.StorageFolderPath}/{path}.{type}";
        if (Directory.Exists(Path.GetDirectoryName(absolutePath)) == false)
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, value);
    }
    void DeleteValue(string path, string type)
    {
        string absolutePath = $"{AlifePath.StorageFolderPath}/{path}.{type}";
        if (File.Exists(absolutePath))
            File.Delete(absolutePath);
    }
}
