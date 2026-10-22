using System.Xml.Linq;
using Alife.Foundation;

namespace Alife.Client;

public record UpdateInfo(string Version, string? ReleaseNotes);

public static class ClientUpgrade
{
    public const string ReleasesAtomUrl = "https://github.com/BDFFZI/Alife/releases.atom";
    public const string ReleasesDownloadUrl = "https://github.com/BDFFZI/Alife/releases/download";
    public static string CurrentVersion => AlifeContext.AppVersion;
    public static UpdateInfo? NewVersion { get; private set; }

    public static async Task FetchNewVersion()
    {
        string response = await AlifeUtility.FetchStringAsync(ReleasesAtomUrl, TimeSpan.FromSeconds(7));
        XDocument document = XDocument.Parse(response);
        XNamespace ns = "http://www.w3.org/2005/Atom";
        XElement? entry = document.Root?.Element(ns + "entry");
        if (entry == null)
            throw new Exception("客户端版本同步失败，无法获取到版本信息。");

        string? tagName = entry.Element(ns + "title")?.Value;
        if (tagName == null)
            throw new Exception("客户端版本同步失败，无法获取到标签信息。");

        string remoteVersion = tagName.Trim().TrimStart('v');
        if (new Version(remoteVersion) <= new Version(AlifeContext.AppVersion))
        {
            NewVersion = null;
            return; //本地比云端新
        }

        NewVersion = new UpdateInfo(remoteVersion, entry.Element(ns + "content")?.Value);
    }
    public static async Task InstallNewVersion(IAlifeClient alifeClient, Action<int>? onProgress = null)
    {
        if (NewVersion == null)
            throw new Exception("没有新版本。");

        //更新包从哪来、是什么格式、如何安装与重启，全部由各平台客户端自己处理
        await alifeClient.UpgradeClient(NewVersion.Version, onProgress);
    }
}