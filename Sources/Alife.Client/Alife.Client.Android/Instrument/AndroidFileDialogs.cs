using Android.App;
using Android.Content;
using Alife.Foundation;

namespace Alife.Client.Android;

/// <summary>Android 端的通用文件对话框：在文件管理器中显示位置、选择目录。</summary>
static class AndroidFileDialogs
{
    public const string Authority = "com.alife.client.fileprovider";

    /// <summary>在系统文件管理器中打开指定目录。</summary>
    public static void RevealFolder(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (Directory.Exists(path) == false)
            throw new DirectoryNotFoundException($"目录不存在：{path}");

        Context context = MainApplication.Instance!;
        if (TryGetPrimaryRelative(path, out string relative))
        {
            var uri = new global::Android.Net.Uri.Builder()
                .Scheme("content")!
                .Authority("com.android.externalstorage.documents")!
                .AppendPath("document")!
                .AppendPath("primary:" + relative)!
                .Build()!;
            Intent browse = new(Intent.ActionView);
            browse.SetDataAndType(uri, "vnd.android.document/directory");
            browse.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            if (TryStart(context, browse))
                return;
        }

        OpenWithFileProvider(context, path, "resource/folder");
    }

    /// <summary>在系统文件管理器中定位指定文件（选中或直接打开其所在应用）。</summary>
    public static void RevealFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (File.Exists(path) == false)
            throw new FileNotFoundException($"文件不存在：{path}");

        OpenWithFileProvider(MainApplication.Instance!, path, GetMimeType(path));
    }

    /// <summary>通过系统目录选择器选择一个目录，返回真实文件系统路径。</summary>
    public static async Task<string?> SelectFolderAsync(CancellationToken cancellationToken = default)
    {
        Intent intent = new(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission |
                        ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);

        (Result result, Intent? data) = await AndroidActivityResultBroker.RequestAsync(intent, cancellationToken);
        if (result != Result.Ok || data?.Data == null)
            return null;

        string? documentId = GetTreeDocumentId(data.Data);
        if (string.IsNullOrEmpty(documentId))
            throw new NotSupportedException("无法解析所选目录。");

        int separator = documentId.IndexOf(':');
        if (separator <= 0)
            throw new NotSupportedException("无法解析所选目录。");

        string volume = documentId[..separator];
        string root = string.Equals(volume, "primary", StringComparison.OrdinalIgnoreCase)
            ? global::Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath
            : Path.Combine("/storage", volume);
        if (Directory.Exists(root) == false)
            throw new NotSupportedException($"无法访问所选存储卷：{volume}");

        string relative = documentId[(separator + 1)..];
        return relative.Length == 0
            ? root
            : Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>通过系统文件选择器选择单个文件，尽量返回真实路径，无法解析时复制到临时目录。</summary>
    public static async Task<string?> SelectFileAsync(CancellationToken cancellationToken = default)
    {
        Intent intent = new(Intent.ActionOpenDocument);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        intent.SetType("*/*");

        (Result result, Intent? data) = await AndroidActivityResultBroker.RequestAsync(intent, cancellationToken);
        if (result != Result.Ok || data?.Data == null)
            return null;

        return ResolveDocumentPath(MainApplication.Instance!, data.Data);
    }

    static string ResolveDocumentPath(Context context, global::Android.Net.Uri uri)
    {
        if (uri.Scheme == "file" && string.IsNullOrEmpty(uri.Path) == false)
            return uri.Path;

        string? documentId = null;
        try
        {
            documentId = global::Android.Provider.DocumentsContract.GetDocumentId(uri);
        }
        catch (Exception)
        {
            // 某些 provider 不支持 DocumentsContract，走复制兜底
        }

        if (string.IsNullOrEmpty(documentId) == false)
        {
            int separator = documentId.IndexOf(':');
            if (separator > 0)
            {
                string volume = documentId[..separator];
                string root = string.Equals(volume, "primary", StringComparison.OrdinalIgnoreCase)
                    ? global::Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath
                    : Path.Combine("/storage", volume);
                string relative = documentId[(separator + 1)..];
                string path = relative.Length == 0
                    ? root
                    : Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(path))
                    return path;
            }
        }

        return CopyToTemp(context, uri);
    }

    static string CopyToTemp(Context context, global::Android.Net.Uri uri)
    {
        string? displayName = null;
        try
        {
            using var cursor = context.ContentResolver?.Query(uri,
                [global::Android.Provider.IOpenableColumns.DisplayName], null, null, null);
            if (cursor != null && cursor.MoveToFirst())
                displayName = cursor.GetString(0);
        }
        catch (Exception)
        {
            // 忽略查询失败，仅使用随机文件名
        }

        string extension = Path.GetExtension(displayName ?? "");
        string path = Path.Combine(AlifePath.TempFolderPath,
            $"picked_{DateTime.Now:HHmmss}_{Guid.NewGuid():N}{extension}");
        Directory.CreateDirectory(AlifePath.TempFolderPath);

        using Stream? input = context.ContentResolver?.OpenInputStream(uri);
        if (input == null)
            throw new NotSupportedException("无法读取所选文件。");
        using FileStream output = File.Create(path);
        input.CopyTo(output);
        return path;
    }

    static void OpenWithFileProvider(Context context, string path, string mimeType)
    {
        var file = new Java.IO.File(path);
        global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, Authority, file);
        Intent intent = new(Intent.ActionView);
        intent.SetDataAndType(uri, mimeType);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        if (TryStart(context, intent) == false)
            throw new NotSupportedException("没有可打开该位置的应用。");
    }

    static bool TryStart(Context context, Intent intent)
    {
        try
        {
            context.StartActivity(intent);
            return true;
        }
        catch (ActivityNotFoundException)
        {
            return false;
        }
    }

    static bool TryGetPrimaryRelative(string path, out string relative)
    {
        relative = "";
        string? external = global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
        if (string.IsNullOrEmpty(external))
            return false;

        string full = Path.GetFullPath(path);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(external));
        if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        relative = full[(root.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        return true;
    }

    static string? GetTreeDocumentId(global::Android.Net.Uri uri)
    {
        System.Collections.Generic.IList<string>? segments = uri.PathSegments;
        if (segments == null)
            return null;
        for (int i = 0; i < segments.Count - 1; i++)
        {
            if (segments[i] == "tree")
                return segments[i + 1];
        }
        return null;
    }

    static string GetMimeType(string path)
    {
        string extension = Path.GetExtension(path).TrimStart('.');
        string? mime = string.IsNullOrEmpty(extension)
            ? null
            : global::Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension.ToLowerInvariant());
        return string.IsNullOrEmpty(mime) ? "*/*" : mime;
    }
}
