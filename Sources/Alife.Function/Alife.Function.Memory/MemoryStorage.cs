using System;
using System.IO;
using System.Threading.Tasks;

namespace Alife.Function.Memory;

public struct MemoryIndex(int level, string name)
{
    public int Level { get; set; } = level;
    public string Name { get; set; } = name;

    public static MemoryIndex Parse(string str)
    {
        int splitIndex = str.IndexOf('-');
        if (int.TryParse(str[..splitIndex], out int level) == false)
            throw new Exception("无法解析存档索引中的等级信息");
        string name = str[(splitIndex + 1)..];
        if (string.IsNullOrEmpty(name))
            throw new Exception("无法解析存档索引中的名称信息");

        return new MemoryIndex(level, name);
    }
    public override string ToString()
    {
        return $"{Level}-{Name}";
    }
}

public class MemoryStorage(string rootPath)
{
    public async Task SaveAsync(MemoryIndex index, string content)
    {
        string dir = Path.Combine(rootPath, $"L{index.Level}");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string filePath = Path.Combine(dir, $"{index}.txt");
        await File.WriteAllTextAsync(filePath, content);
    }
    public async Task<string?> LoadAsync(MemoryIndex index)
    {
        string filePath = Path.Combine(rootPath, $"L{index.Level}", $"{index}.txt");
        if (!File.Exists(filePath))
            return null;

        return await File.ReadAllTextAsync(filePath);
    }
}