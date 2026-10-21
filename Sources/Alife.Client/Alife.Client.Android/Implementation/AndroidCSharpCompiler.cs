using System.Reflection;
using System.Text;
using Alife.Foundation;
using Alife.Framework;
using K4os.Compression.LZ4;

namespace Alife.Client.Android;

sealed class AndroidCSharpCompiler : CSharpCompiler
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

        if (TryGetElfPayload(elf, out int payloadOffset, out int payloadSize))
        {
            if (payloadSize >= 12 &&
                elf[payloadOffset] == 'X' && elf[payloadOffset + 1] == 'A' &&
                elf[payloadOffset + 2] == 'L' && elf[payloadOffset + 3] == 'Z')
            {
                uint uncompressedSize = BitConverter.ToUInt32(elf, payloadOffset + 8);
                if (uncompressedSize <= int.MaxValue)
                {
                    byte[] image = new byte[uncompressedSize];
                    int decoded = LZ4Codec.Decode(
                        elf, payloadOffset + 12, payloadSize - 12,
                        image, 0, image.Length);
                    if (decoded == image.Length)
                        return image;
                }
            }

            int payloadEnd = payloadOffset + payloadSize;
            if (payloadEnd <= elf.Length &&
                payloadSize >= 0x40 &&
                elf[payloadOffset] == 'M' && elf[payloadOffset + 1] == 'Z')
                return elf[payloadOffset..payloadEnd];
        }

        // 兼容未压缩的旧包：payload 不一定按 4KB 对齐，扫描并校验 PE 头。
        for (int offset = 0; offset + 0x40 < elf.Length; offset++)
        {
            if (elf[offset] != 'M' || elf[offset + 1] != 'Z')
                continue;

            int peOffset = BitConverter.ToInt32(elf, offset + 0x3C);
            if (peOffset < 0 || offset + peOffset + 4 > elf.Length)
                continue;
            int pe = offset + peOffset;
            if (elf[pe] == 'P' && elf[pe + 1] == 'E' && elf[pe + 2] == 0 && elf[pe + 3] == 0)
                return elf[offset..];
        }
        return null;
    }

    static bool TryGetElfPayload(byte[] elf, out int offset, out int size)
    {
        offset = 0;
        size = 0;
        if (elf.Length < 52 ||
            elf[0] != 0x7F || elf[1] != 'E' || elf[2] != 'L' || elf[3] != 'F')
            return false;

        bool is64 = elf[4] == 2;
        if (!is64 && elf[4] != 1)
            return false;

        ulong sectionOffset = is64 ? BitConverter.ToUInt64(elf, 0x28) : BitConverter.ToUInt32(elf, 0x20);
        int sectionEntrySize = is64 ? BitConverter.ToUInt16(elf, 0x3A) : BitConverter.ToUInt16(elf, 0x2E);
        int sectionCount = is64 ? BitConverter.ToUInt16(elf, 0x3C) : BitConverter.ToUInt16(elf, 0x2C);
        int stringSectionIndex = is64 ? BitConverter.ToUInt16(elf, 0x3E) : BitConverter.ToUInt16(elf, 0x30);
        int minimumEntrySize = is64 ? 64 : 40;
        if (sectionOffset == 0 || sectionEntrySize < minimumEntrySize ||
            sectionCount <= 0 || stringSectionIndex >= sectionCount)
            return false;

        long stringSection = (long)sectionOffset + (long)stringSectionIndex * sectionEntrySize;
        if (stringSection + minimumEntrySize > elf.Length)
            return false;

        ulong stringOffset = is64
            ? BitConverter.ToUInt64(elf, (int)stringSection + 24)
            : BitConverter.ToUInt32(elf, (int)stringSection + 16);
        ulong stringSize = is64
            ? BitConverter.ToUInt64(elf, (int)stringSection + 32)
            : BitConverter.ToUInt32(elf, (int)stringSection + 20);
        if (stringOffset + stringSize > (ulong)elf.Length)
            return false;

        for (int i = 0; i < sectionCount; i++)
        {
            long section = (long)sectionOffset + (long)i * sectionEntrySize;
            if (section + minimumEntrySize > elf.Length)
                return false;

            uint nameOffset = BitConverter.ToUInt32(elf, (int)section);
            ulong sectionDataOffset = is64
                ? BitConverter.ToUInt64(elf, (int)section + 24)
                : BitConverter.ToUInt32(elf, (int)section + 16);
            ulong sectionDataSize = is64
                ? BitConverter.ToUInt64(elf, (int)section + 32)
                : BitConverter.ToUInt32(elf, (int)section + 20);
            if (sectionDataOffset + sectionDataSize > (ulong)elf.Length)
                continue;

            long namePosition = (long)stringOffset + nameOffset;
            if (namePosition < 0 || namePosition >= elf.Length)
                continue;

            int nameEnd = (int)namePosition;
            while (nameEnd < elf.Length && elf[nameEnd] != 0)
                nameEnd++;
            if (Encoding.UTF8.GetString(elf, (int)namePosition, nameEnd - (int)namePosition) != "payload")
                continue;

            offset = (int)sectionDataOffset;
            size = (int)sectionDataSize;
            return true;
        }
        return false;
    }

    readonly object referenceGate = new();
}

