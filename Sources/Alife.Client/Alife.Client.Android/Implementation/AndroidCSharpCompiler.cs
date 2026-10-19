using System.Reflection;
using Alife.Foundation;
using Alife.Framework;

namespace Alife.Client.Android;

public sealed class AndroidCSharpCompiler : CSharpCompiler
{
    protected override IEnumerable<string> GetAssemblyReferenceFiles(IEnumerable<Assembly> assemblies)
    {
        string? apkPath = MainApplication.Instance?.ApplicationInfo?.SourceDir;
        if (string.IsNullOrEmpty(apkPath))
            return [];

        lock (referenceGate)
        {
            string directory = GetReferenceDirectory();
            List<string> files = [];
            using Java.Util.Zip.ZipFile zip = new(apkPath);
            foreach (Assembly assembly in assemblies)
            {
                string? assemblyName = assembly.GetName().Name;
                if (string.IsNullOrEmpty(assemblyName))
                    continue;

                string target = Path.Combine(directory, assemblyName + ".dll");
                if (File.Exists(target) == false || new FileInfo(target).Length <= 2)
                {
                    byte[]? image = ExtractAssemblyBytes(zip, assemblyName);
                    if (image == null)
                        continue;
                    File.WriteAllBytes(target, image);
                }
                files.Add(target);
            }
            return files;
        }
    }

    /// <summary>按用户安装包的更新时间分版本缓存引用，应用更新后自动重建。</summary>
    static string GetReferenceDirectory()
    {
        string version = MainApplication.Instance?.PackageManager?
            .GetPackageInfo(MainApplication.Instance.PackageName!, 0)?
            .LastUpdateTime.ToString() ?? "0";

        string root = Path.Combine(AlifePath.RuntimeFolderPath, "CompileRefs");
        string directory = Path.Combine(root, version);
        if (Directory.Exists(directory))
            return directory;

        Directory.CreateDirectory(directory);
        foreach (string old in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(old, directory, StringComparison.Ordinal))
                continue;
            try
            {
                Directory.Delete(old, true);
            }
            catch
            {
                // 被占用时忽略，下次启动继续尝试清理。
            }
        }
        return directory;
    }

    /// <summary>从 APK 的 lib_XXX.dll.so（ELF 壳）中提取原始 PE 映像。</summary>
    static byte[]? ExtractAssemblyBytes(Java.Util.Zip.ZipFile zip, string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        Java.Util.Zip.ZipEntry? entry = zip.GetEntry($"lib/arm64-v8a/lib_{assemblyName}.dll.so");
        if (entry == null)
            return null;

        using Stream? stream = zip.GetInputStream(entry);
        if (stream == null)
            return null;
        using MemoryStream buffered = new();
        stream.CopyTo(buffered);
        byte[] elf = buffered.ToArray();

        // payload 位于 4KB 对齐边界，查找 PE 的 "MZ" 头。
        for (int offset = 0x1000; offset + 2 < elf.Length; offset += 0x1000)
        {
            if (elf[offset] == 'M' && elf[offset + 1] == 'Z')
                return elf[offset..];
        }
        return null;
    }

    readonly object referenceGate = new();
}
