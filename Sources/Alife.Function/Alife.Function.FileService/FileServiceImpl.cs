using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Alife.Function.FileService;

public class FileServiceImpl
{
    public const int MaxContentLength = 51200;
    public const int DefaultReadLimit = 2000;
    public const int MaxGrepResults = 500;

    public string? TempFolderPath { get; set; }

    public async Task<FileReadResult> ReadAsync(string path, int? offset, int? limit, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(path))
        {
            List<string> entries = new();
            foreach (string directory in Directory.GetDirectories(path))
                entries.Add(Path.GetFileName(directory) + "/");
            foreach (string file in Directory.GetFiles(path))
                entries.Add(Path.GetFileName(file));
            return FileReadResult.FromContent(string.Join("\n", entries));
        }

        if (File.Exists(path) == false)
            return FileReadResult.FromError("文件不存在");

        string[] lines = await File.ReadAllLinesAsync(path, cancellationToken);

        int startLine = offset.HasValue ? offset.Value : 0;
        int readLimit = limit ?? DefaultReadLimit;

        if (startLine < 0)
        {
            startLine = Math.Max(0, lines.Length + startLine);
        }
        else
        {
            startLine = Math.Max(0, startLine - 1);
        }

        if (startLine >= lines.Length)
            return FileReadResult.FromError("起始行号超出文件范围");

        IEnumerable<string> selectedLines = lines
            .Skip(startLine)
            .Take(readLimit)
            .Select((line, i) => $"{startLine + i + 1}: {line}");

        string content = string.Join("\n", selectedLines);

        if (content.Length > MaxContentLength)
        {
            string tempFile = Path.Combine(TempFolderPath ?? Path.GetTempPath(), $"read_{Path.GetFileName(path)}.txt");
            await File.WriteAllTextAsync(tempFile, content, cancellationToken);
            return FileReadResult.FromTempFile(tempFile);
        }

        return FileReadResult.FromContent(content);
    }

    public async Task WriteAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir) == false && Directory.Exists(dir) == false)
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(filePath, content, cancellationToken);
    }

    public async Task EditAsync(string filePath, string oldString, string newString, bool replaceAll, CancellationToken cancellationToken = default)
    {
        if (File.Exists(filePath) == false)
            throw new FileNotFoundException("文件不存在", filePath);

        string content = await File.ReadAllTextAsync(filePath, cancellationToken);

        if (content.Contains(oldString, StringComparison.Ordinal) == false)
            throw new InvalidOperationException("未找到要替换的文本");

        if (replaceAll == false &&
            content.IndexOf(oldString, StringComparison.Ordinal) != content.LastIndexOf(oldString, StringComparison.Ordinal))
            throw new InvalidOperationException("找到多个匹配项，请提供更多上下文以定位唯一匹配，或设置 replaceAll=true");

        string newContent = content.Replace(oldString, newString, StringComparison.Ordinal);

        await File.WriteAllTextAsync(filePath, newContent, cancellationToken);
    }

    static string GlobToRegex(string pattern)
    {
        return "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";
    }

    public string[] Glob(string path, string pattern)
    {
        if (Directory.Exists(path) == false)
            throw new DirectoryNotFoundException($"目录不存在: {path}");

        Matcher matcher = new();
        matcher.AddInclude(pattern);

        List<string> entries = matcher.GetResultsInFullPath(path)
            .Select(f => Path.GetRelativePath(path, f))
            .ToList();

        // Matcher does not include directories; enumerate them separately
        bool isRecursive = pattern.Contains("**");
        Regex dirRegex = new(GlobToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.Compiled);

        AddMatchingDirectories(path, "", isRecursive, dirRegex, entries);

        return entries.OrderBy(f => f).ToArray();
    }

    static void AddMatchingDirectories(string rootPath, string relativePath, bool recurse, Regex regex, List<string> entries)
    {
        string currentPath = string.IsNullOrEmpty(relativePath)
            ? rootPath
            : Path.Combine(rootPath, relativePath);

        foreach (string dir in Directory.GetDirectories(currentPath))
        {
            string dirName = Path.GetFileName(dir);
            string dirRelative = string.IsNullOrEmpty(relativePath) ? dirName : relativePath + "/" + dirName;

            if (regex.IsMatch(dirRelative))
            {
                string entry = dirRelative + "/";
                if (entries.Contains(entry) == false)
                    entries.Add(entry);
            }

            if (recurse)
                AddMatchingDirectories(rootPath, dirRelative, true, regex, entries);
        }
    }

    public GrepResult Grep(string path, string pattern, string? include = null)
    {
        if (Directory.Exists(path) == false)
            throw new DirectoryNotFoundException($"目录不存在: {path}");

        Regex regex = new(pattern, RegexOptions.Compiled);
        List<string> matches = new();

        string searchPattern = include ?? "*";
        string[] files = Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories);

        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (regex.IsMatch(lines[i]))
                {
                    string relativePath = Path.GetRelativePath(path, file);
                    matches.Add($"{relativePath}:{i + 1}: {lines[i].Trim()}");

                    if (matches.Count >= MaxGrepResults)
                        return GrepResult.FromTruncated(matches);
                }
            }
        }

        return GrepResult.FromComplete(matches);
    }
}

public class FileReadResult
{
    public string? Content { get; }
    public string? TempFilePath { get; }
    public string? Error { get; }

    FileReadResult(string? content, string? tempFilePath, string? error)
    {
        Content = content;
        TempFilePath = tempFilePath;
        Error = error;
    }

    public static FileReadResult FromContent(string content) => new(content, null, null);
    public static FileReadResult FromTempFile(string tempFilePath) => new(null, tempFilePath, null);
    public static FileReadResult FromError(string error) => new(null, null, error);
}

public class GrepResult
{
    public IReadOnlyList<string> Matches { get; }
    public bool Truncated { get; }

    GrepResult(IReadOnlyList<string> matches, bool truncated)
    {
        Matches = matches;
        Truncated = truncated;
    }

    public static GrepResult FromComplete(List<string> matches) => new(matches.AsReadOnly(), false);
    public static GrepResult FromTruncated(List<string> matches) => new(matches.AsReadOnly(), true);
}
