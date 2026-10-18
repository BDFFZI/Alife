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

sealed class DotNetInstallation
{
    public string DirectoryPath { get; } = Path.Combine(AlifePath.RuntimeFolderPath, "DotNet");
    public string SdkPath => Path.Combine(DirectoryPath, "usr", "lib", "dotnet");
    public string LibraryPath => Path.Combine(DirectoryPath, "usr", "lib");
    public string CertificatePath => Path.Combine(DirectoryPath, "usr", "etc", "tls", "cert.pem");

    public async Task EnsureAsync(Action<string, int, int> progress, CancellationToken cancellationToken)
    {
        using Stream manifest = typeof(DotNetInstallation).Assembly.GetManifestResourceStream("SdkPackages")!;
        using StreamReader reader = new(manifest);
        string manifestJson = await reader.ReadToEndAsync(cancellationToken);

        string marker = Path.Combine(DirectoryPath, "installed.json");
        bool isInstalled = File.Exists(marker)
            && File.ReadAllText(marker) == manifestJson
            && Directory.Exists(Path.Combine(SdkPath, "sdk"))
            && Directory.Exists(Path.Combine(SdkPath, "shared", "Microsoft.AspNetCore.App"));
        if (isInstalled)
            return;

        if (Directory.Exists(DirectoryPath))
            Directory.Delete(DirectoryPath, true);
        Directory.CreateDirectory(DirectoryPath);

        Package[] packages = JsonSerializer.Deserialize<Package[]>(manifestJson)!;
        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Alife/{AlifeContext.AppVersion}");

        for (int index = 0; index < packages.Length; index++)
        {
            Package package = packages[index];
            await InstallPackageAsync(http, package, message => progress(message, index + 1, packages.Length), cancellationToken);
        }
        FixShebangs();
        await File.WriteAllTextAsync(marker, manifestJson, cancellationToken);
    }

    /// <summary>
    /// Termux 包内的脚本 shebang 硬编码了 Termux 绝对路径，替换为本安装根下的对应路径。
    /// </summary>
    void FixShebangs()
    {
        string binDirectory = Path.Combine(DirectoryPath, "usr", "bin");
        if (Directory.Exists(binDirectory) == false)
            return;
        byte[] termuxPrefix = "/data/data/com.termux/files"u8.ToArray();
        byte[] localPrefix = Encoding.UTF8.GetBytes(DirectoryPath);
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
            //匹配 "#!{termuxPrefix}..."，跳过 "#!" 两字节
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

    async Task InstallPackageAsync(HttpClient http, Package package, Action<string> progress, CancellationToken cancellationToken)
    {
        string archive = Path.Combine(AlifePath.TempFolderPath, package.Name + ".deb");
        Exception? lastError = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            string url = AlifeMirror.TransformUrl(Mirrors[(attempt - 1) % Mirrors.Length] + package.File);
            try
            {
                progress($"正在安装 {package.Name}（{attempt}/3）");
                await DownloadAsync(http, url, archive, cancellationToken);
                string actual = await GetSha256Async(archive, cancellationToken);
                if (actual.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase) == false)
                {
                    File.Delete(archive);
                    throw new InvalidDataException($"{package.Name} 校验失败。");
                }
                await using (FileStream file = File.OpenRead(archive))
                    ExtractDeb(file);
                File.Delete(archive);
                return;
            }
            catch (Exception error) when (error is not OperationCanceledException || cancellationToken.IsCancellationRequested == false)
            {
                lastError = error;
                progress($"{package.Name} 安装失败，正在重试：{error.Message}");
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

    void ExtractDeb(Stream file)
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
                ExtractTar(tar);
                return;
            }
            file.Seek(length + length % 2, SeekOrigin.Current);
        }
        throw new InvalidDataException("Debian archive has no data member.");
    }

    void ExtractTar(Stream stream)
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

            string path = Path.GetFullPath(Path.Combine(DirectoryPath, name[prefix.Length..]));
            if (path.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) == false)
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
                {
                    string installed = Path.GetFullPath(Path.Combine(DirectoryPath, target[(prefix.Length + 1)..]));
                    if (installed.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) == false)
                        throw new InvalidDataException("Package symlink escapes its installation directory.");
                    target = installed;
                }
                else if (Path.IsPathRooted(target))
                {
                    throw new InvalidDataException("Package symlink escapes its installation directory.");
                }

                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target));
                if (resolved.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) == false)
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

    sealed record Package(string Name, string File, string Sha256);

    static readonly string[] Mirrors = [
        "https://mirrors.tuna.tsinghua.edu.cn/termux/apt/termux-main/",
        "https://packages.termux.dev/apt/termux-main/",
    ];
}
