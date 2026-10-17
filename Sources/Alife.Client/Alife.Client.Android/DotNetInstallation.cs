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
    sealed record Package(string Name, string File, string Sha256);

    public async Task EnsureAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        string marker = Path.Combine(DirectoryPath, "installed.json");
        if (File.Exists(marker) && Directory.Exists(Path.Combine(SdkPath, "sdk")) &&
            Directory.Exists(Path.Combine(SdkPath, "shared", "Microsoft.AspNetCore.App"))) return;
        Directory.CreateDirectory(DirectoryPath);
        using var manifest = typeof(DotNetInstallation).Assembly.GetManifestResourceStream("SdkPackages")!;
        var packages = (await JsonSerializer.DeserializeAsync<Package[]>(manifest, cancellationToken: cancellationToken))!;
        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Alife/4.6.1");
        foreach (var package in packages)
        {
            using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            downloadTimeout.CancelAfter(TimeSpan.FromMinutes(30));
            string archive = Path.Combine(AlifePath.TempFolderPath, package.Name + ".deb");
            Exception? lastError = null;
            string[] mirrors = [
                "http://127.0.0.1:8000/apt/termux-main/", //TEMP: adb reverse 喂包，调通即删
                "https://mirrors.tuna.tsinghua.edu.cn/termux/apt/termux-main/",
                "https://packages.termux.dev/apt/termux-main/",
            ];
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                string url = AlifeMirror.TransformUrl(mirrors[(attempt - 1) % mirrors.Length] + package.File);
                try
                {
                    progress($"Installing {package.Name}...(尝试 {attempt}/3)");
                    //断点续传：弱网下每次中断都能保留已下载部分
                    long resume = File.Exists(archive) ? new FileInfo(archive).Length : 0;
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (resume > 0) request.Headers.Range = new RangeHeaderValue(resume, null);
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, downloadTimeout.Token))
                    {
                        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                        {
                            //本地已下完（或更长），直接去校验
                        }
                        else
                        {
                            response.EnsureSuccessStatusCode();
                            await using var destination = new FileStream(archive,
                                response.StatusCode == HttpStatusCode.PartialContent ? FileMode.Append : FileMode.Create,
                                FileAccess.Write, FileShare.None);
                            await response.Content.CopyToAsync(destination, downloadTimeout.Token);
                        }
                    }
                    lastError = null;
                    break;
                }
                catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    lastError = error;
                    progress($"{package.Name} 第 {attempt} 次下载失败，重试中：{error.Message}");
                }
            }
            if (lastError is not null) throw lastError;
            string actual;
            await using (var file = File.OpenRead(archive))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
            if (!actual.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                //续传文件与新版本对不上时删掉，下次启动从头下
                File.Delete(archive);
                throw new InvalidDataException($"Checksum mismatch for {package.Name}, deleted partial file, retry on next launch.");
            }
            await using (var file = File.OpenRead(archive))
            {
                ExtractDeb(file);
            }
            File.Delete(archive);
        }
        File.WriteAllText(marker, JsonSerializer.Serialize(packages));
    }

    void ExtractDeb(Stream file)
    {
        byte[] magic = new byte[8];
        file.ReadExactly(magic);
        if (Encoding.ASCII.GetString(magic) != "!<arch>\n") throw new InvalidDataException("Invalid Debian archive.");
        byte[] header = new byte[60];
        while (file.Position < file.Length)
        {
            file.ReadExactly(header);
            if (header[58] != '`' || header[59] != '\n') throw new InvalidDataException("Invalid ar member.");
            string name = Encoding.ASCII.GetString(header, 0, 16).Trim().TrimEnd('/');
            long length = long.Parse(Encoding.ASCII.GetString(header, 48, 10).Trim(), CultureInfo.InvariantCulture);
            if (name.StartsWith("data.tar", StringComparison.Ordinal))
            {
                using MemoryStream payload = new();
                byte[] buffer = new byte[81920];
                for (long remaining = length; remaining > 0;)
                {
                    int count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (count == 0) throw new EndOfStreamException();
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
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (name.Length == prefix.Length && entry.EntryType == TarEntryType.Directory) continue;
            string path = Path.GetFullPath(Path.Combine(DirectoryPath, name[prefix.Length..]));
            if (!path.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException($"Package entry escapes its installation directory: {entry.Name}");
            if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (entry.EntryType == TarEntryType.SymbolicLink)
            {
                string target = entry.LinkName.StartsWith("/" + prefix, StringComparison.Ordinal)
                    ? Path.Combine(DirectoryPath, entry.LinkName[(prefix.Length + 1)..]) : entry.LinkName;
                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target));
                if (!resolved.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException("Package symlink escapes its installation directory.");
                File.Delete(path);
                File.CreateSymbolicLink(path, target);
            }
            else if (entry.DataStream is not null)
            {
                using var output = File.Create(path);
                entry.DataStream.CopyTo(output);
                File.SetUnixFileMode(path, entry.Mode);
            }
        }
    }
}
