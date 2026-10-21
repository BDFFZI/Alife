using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alife.Foundation;
using SharpCompress.Compressors.Xz;

namespace Alife.Client.Android;

/// <summary>
/// 通过 Shizuku 把 termux 版 Python 安装到 /data/local/tmp，供插件以 shell 身份执行。
/// App 自身仍无法执行 app 数据目录里的 ELF（Android W^X），所以最终运行位置必须是 shell 可执行目录。
/// </summary>
static class ShizukuPythonInstaller
{
    public const string FinalRoot = ShizukuBridge.PythonRoot;
    public const string FinalUsr = ShizukuBridge.PythonUsr;

    static readonly string[] PackageNames = [
        "ca-certificates",
        "libc++",
        "libicu",
        "gdbm",
        "libandroid-posix-semaphore",
        "libandroid-support",
        "libbz2",
        "libcrypt",
        "libexpat",
        "libffi",
        "liblzma",
        "libsqlite",
        "ncurses",
        "ncurses-ui-libs",
        "python",
        "python-pip",
        "readline",
        "openssl",
        "zlib",
    ];

    static string GetMarker(IAlifeFoundation foundation) => Path.Combine(foundation.RuntimeFolderPath, "python-shizuku.installed.json");
    static string GetStageRoot(IAlifeFoundation foundation) => Path.Combine(foundation.TempFolderPath, "python-stage");
    static string GetTarPath(IAlifeFoundation foundation) => Path.Combine(foundation.StorageFolderPath, "Temp", "python.tar");

    public static bool IsInstalled(IAlifeFoundation foundation) => File.Exists(GetMarker(foundation));

    public static async Task<bool> VerifyAsync(IAlifeFoundation foundation)
    {
        if (IsInstalled(foundation) == false)
            return false;
        if (ShizukuBridge.IsAvailable == false || ShizukuBridge.HasPermission == false)
            return false;

        try
        {
            ShizukuShellResult result = await ShizukuBridge.ExecuteAsync(
                $"cd {FinalRoot} && export PYTHONHOME={FinalUsr} LD_LIBRARY_PATH={FinalUsr}/lib PATH={FinalUsr}/bin:$PATH && ./usr/bin/python -m pip --version 2>&1");
            if (result.ExitCode == 0 && result.Output.Contains("pip", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // 下面统一清理标记
        }

        try
        {
            File.Delete(GetMarker(foundation));
        }
        catch
        {
            // 忽略
        }
        return false;
    }

    public static async Task InstallAsync(IAlifeFoundation foundation, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (ShizukuBridge.IsAvailable == false || ShizukuBridge.HasPermission == false)
            throw new InvalidOperationException("��Ҫ�����Ӳ���Ȩ Shizuku��");
        string stageRoot = GetStageRoot(foundation);
        string tarPath = GetTarPath(foundation);
        if (await VerifyAsync(foundation))
        {
            progress?.Report("Python �Ѱ�װ�� " + FinalRoot);
            return;
        }

        progress?.Report("����׼�� Python ��װ��...");
        if (Directory.Exists(stageRoot))
            Directory.Delete(stageRoot, true);
        Directory.CreateDirectory(stageRoot);

        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Alife/{foundation.AppVersion}");

        Package[] packages = await ResolvePackagesAsync(http, cancellationToken);
        string manifestJson = JsonSerializer.Serialize(packages);

        for (int index = 0; index < packages.Length; index++)
        {
            Package package = packages[index];
            progress?.Report($"正在下载 Python 组件 {package.Name}（{index + 1}/{packages.Length}）");
            await InstallPackageAsync(http, package, foundation, stageRoot, cancellationToken);
        }

        FixShebangs(stageRoot, FinalRoot);

        progress?.Report("正在打包 Python 运行时...");
        string? tempDirectory = Path.GetDirectoryName(tarPath);
        if (string.IsNullOrEmpty(tempDirectory) == false)
            Directory.CreateDirectory(tempDirectory);
        if (File.Exists(tarPath))
            File.Delete(tarPath);
        await CreateTarAsync(stageRoot, tarPath, cancellationToken);

        progress?.Report("正在通过 Shizuku 安装 Python...");
        string extractCommand = $"""
                                 rm -rf {FinalRoot}
                                 mkdir -p {FinalRoot}
                                 /system/bin/tar -xf '{tarPath}' -C {FinalRoot}
                                 chmod -R u+rwX {FinalRoot}
                                 chmod 755 {FinalRoot}/usr/bin/python* {FinalRoot}/usr/bin/pip* 2>/dev/null || true
                                 rm -f '{tarPath}'
                                 """;
        ShizukuShellResult extractResult = await ShizukuBridge.ExecuteAsync(extractCommand + " 2>&1", cancellationToken);
        if (extractResult.ExitCode != 0)
            throw new InvalidOperationException($"Python 解压失败：{extractResult.Output}");

        progress?.Report("正在验证 Python + pip...");
        ShizukuShellResult verify = await ShizukuBridge.ExecuteAsync(
            $"cd {FinalRoot} && export PYTHONHOME={FinalUsr} LD_LIBRARY_PATH={FinalUsr}/lib PATH={FinalUsr}/bin:$PATH && ./usr/bin/python -m pip --version 2>&1",
            cancellationToken);
        if (verify.ExitCode != 0 || verify.Output.Contains("pip", StringComparison.OrdinalIgnoreCase) == false)
            throw new InvalidOperationException($"Python 验证失败：{verify.Output}");

        await File.WriteAllTextAsync(GetMarker(foundation), manifestJson, cancellationToken);
        try
        {
            if (Directory.Exists(stageRoot))
                Directory.Delete(stageRoot, true);
        }
        catch
        {
            // 忽略缓存清理失败
        }

        progress?.Report("Python 与 pip 安装完成。");
    }

    static async Task<Package[]> ResolvePackagesAsync(HttpClient http, CancellationToken cancellationToken)
    {
        foreach (string mirror in Mirrors)
        {
            try
            {
                string index = await http.GetStringAsync(
                    AlifeMirror.TransformUrl(mirror + "dists/stable/main/binary-aarch64/Packages"),
                    cancellationToken);
                Dictionary<string, (string File, string Sha256)> entries = ParsePackageIndex(index);
                List<Package> resolved = [];
                foreach (string name in PackageNames)
                {
                    if (entries.TryGetValue(name, out (string File, string Sha256) entry))
                        resolved.Add(new Package(name, entry.File, entry.Sha256));
                }
                if (resolved.Count == PackageNames.Length)
                    return resolved.ToArray();
            }
            catch (Exception error) when (error is not OperationCanceledException || cancellationToken.IsCancellationRequested == false)
            {
                // 尝试下一个镜像。
            }
        }

        using Stream manifestStream = typeof(ShizukuPythonInstaller).Assembly.GetManifestResourceStream("SdkPackages")
            ?? throw new InvalidOperationException("找不到 SdkPackages 资源。");
        using StreamReader reader = new(manifestStream);
        string manifestJson = await reader.ReadToEndAsync(cancellationToken);
        return JsonSerializer.Deserialize<Package[]>(manifestJson)!
            .Where(package => PackageNames.Contains(package.Name, StringComparer.Ordinal))
            .ToArray();
    }

    static Dictionary<string, (string File, string Sha256)> ParsePackageIndex(string index)
    {
        Dictionary<string, (string File, string Sha256)> result = new(StringComparer.Ordinal);
        foreach (string paragraph in index.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? name = null;
            string? file = null;
            string? sha256 = null;
            foreach (string rawLine in paragraph.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.StartsWith("Package: ", StringComparison.Ordinal))
                    name = line[9..].Trim();
                else if (line.StartsWith("Filename: ", StringComparison.Ordinal))
                    file = line[10..].Trim();
                else if (line.StartsWith("SHA256: ", StringComparison.Ordinal))
                    sha256 = line[8..].Trim();
            }
            if (name != null && file != null && sha256 != null)
                result[name] = (file, sha256);
        }
        return result;
    }

    static async Task InstallPackageAsync(HttpClient http, Package package, IAlifeFoundation foundation, string stageRoot, CancellationToken cancellationToken)
    {
        string archive = Path.Combine(foundation.TempFolderPath, package.Name + ".deb");
        Exception? lastError = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            string url = AlifeMirror.TransformUrl(Mirrors[(attempt - 1) % Mirrors.Length] + package.File);
            try
            {
                await DownloadAsync(http, url, archive, cancellationToken);
                string actual = await GetSha256Async(archive, cancellationToken);
                if (actual.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase) == false)
                {
                    File.Delete(archive);
                    throw new InvalidDataException($"{package.Name} 校验失败。");
                }
                await using (FileStream file = File.OpenRead(archive))
                    ExtractDeb(file, stageRoot, FinalRoot);
                File.Delete(archive);
                return;
            }
            catch (Exception error) when (error is not OperationCanceledException || cancellationToken.IsCancellationRequested == false)
            {
                lastError = error;
            }
        }
        throw lastError!;
    }

    static async Task DownloadAsync(HttpClient http, string url, string archive, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));

        long resume = File.Exists(archive) ? new FileInfo(archive).Length : 0;
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (resume > 0)
            request.Headers.Range = new RangeHeaderValue(resume, null);
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            return;

        response.EnsureSuccessStatusCode();
        FileMode mode = response.StatusCode == HttpStatusCode.PartialContent ? FileMode.Append : FileMode.Create;
        await using FileStream destination = new(archive, mode, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(destination, timeout.Token);
    }

    static async Task<string> GetSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
    }

    static async Task CreateTarAsync(string source, string target, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new("/system/bin/tar") {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-cf");
        start.ArgumentList.Add(target);
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(source);
        start.ArgumentList.Add(".");

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动 tar。");
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"tar 打包失败：{error}");
        await process.StandardOutput.ReadToEndAsync(cancellationToken);
    }

    static void ExtractDeb(Stream file, string installRoot, string symlinkRoot)
    {
        byte[] magic = new byte[8];
        file.ReadExactly(magic);
        if (Encoding.ASCII.GetString(magic) != "!<arch>\n")
            throw new InvalidDataException("Invalid Debian archive.");

        byte[] header = new byte[60];
        while (file.Position < file.Length)
        {
            file.ReadExactly(header);
            if (header[58] != '`' || header[59] != '\n')
                throw new InvalidDataException("Invalid ar member.");

            string name = Encoding.ASCII.GetString(header, 0, 16).Trim().TrimEnd('/');
            long length = long.Parse(Encoding.ASCII.GetString(header, 48, 10).Trim(), CultureInfo.InvariantCulture);
            if (name.StartsWith("data.tar", StringComparison.Ordinal))
            {
                using MemoryStream payload = new();
                byte[] buffer = new byte[81920];
                for (long remaining = length; remaining > 0;)
                {
                    int count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (count == 0)
                        throw new EndOfStreamException();
                    payload.Write(buffer, 0, count);
                    remaining -= count;
                }
                payload.Position = 0;
                using Stream tar = name.EndsWith(".xz", StringComparison.Ordinal) ? new XZStream(payload)
                    : name.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(payload, CompressionMode.Decompress)
                    : throw new InvalidDataException($"Unsupported Debian payload: {name}");
                ExtractTar(tar, installRoot, symlinkRoot);
                return;
            }
            file.Seek(length + length % 2, SeekOrigin.Current);
        }
        throw new InvalidDataException("Debian archive has no data member.");
    }

    static void ExtractTar(Stream stream, string installRoot, string symlinkRoot)
    {
        const string prefix = "data/data/com.termux/files/";
        using TarReader tar = new(stream);
        while (tar.GetNextEntry() is { } entry)
        {
            string name = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;
            if (name.StartsWith(prefix, StringComparison.Ordinal) == false)
                continue;
            if (name.Length == prefix.Length && entry.EntryType == TarEntryType.Directory)
                continue;

            string path = Path.GetFullPath(Path.Combine(installRoot, name[prefix.Length..]));
            if (path.StartsWith(installRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) == false)
                throw new InvalidDataException($"Package entry escapes its installation directory: {entry.Name}");

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (entry.EntryType == TarEntryType.SymbolicLink)
            {
                string target = entry.LinkName;
                if (target.StartsWith("/" + prefix, StringComparison.Ordinal))
                    target = Path.Combine(symlinkRoot, target[(prefix.Length + 1)..]);
                else if (Path.IsPathRooted(target))
                    throw new InvalidDataException("Package symlink escapes its installation directory.");

                File.Delete(path);
                File.CreateSymbolicLink(path, target);
            }
            else if (entry.DataStream is not null)
            {
                using FileStream output = File.Create(path);
                entry.DataStream.CopyTo(output);
                File.SetUnixFileMode(path, entry.Mode);
            }
        }
    }

    static void FixShebangs(string installRoot, string finalRoot)
    {
        string binDirectory = Path.Combine(installRoot, "usr", "bin");
        if (Directory.Exists(binDirectory) == false)
            return;
        byte[] termuxPrefix = "/data/data/com.termux/files"u8.ToArray();
        byte[] localPrefix = Encoding.UTF8.GetBytes(finalRoot);
        foreach (string file in Directory.EnumerateFiles(binDirectory))
        {
            byte[] content;
            using (FileStream stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 2 + termuxPrefix.Length)
                    continue;
                content = new byte[stream.Length];
                stream.ReadExactly(content);
            }
            if (content.AsSpan(0, 2).SequenceEqual("#!"u8) == false
                || content.AsSpan(2, termuxPrefix.Length).SequenceEqual(termuxPrefix) == false)
                continue;
            using FileStream writer = File.Open(file, FileMode.Create, FileAccess.Write, FileShare.None);
            writer.Write(ReplaceAll(content, termuxPrefix, localPrefix));
        }

        static byte[] ReplaceAll(byte[] source, byte[] from, byte[] to)
        {
            using MemoryStream output = new(source.Length);
            for (int index = 0; index < source.Length;)
            {
                if (index + from.Length <= source.Length && source.AsSpan(index, from.Length).SequenceEqual(from))
                {
                    output.Write(to);
                    index += from.Length;
                }
                else
                {
                    output.WriteByte(source[index]);
                    index++;
                }
            }
            return output.ToArray();
        }
    }

    sealed record Package(string Name, string File, string Sha256);

    static readonly string[] Mirrors = [
        "https://mirrors.tuna.tsinghua.edu.cn/termux/apt/termux-main/",
        "https://packages.termux.dev/apt/termux-main/",
    ];
}
