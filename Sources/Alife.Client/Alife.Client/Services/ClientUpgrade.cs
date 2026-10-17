using Alife.Foundation;
using Newtonsoft.Json.Linq;

namespace Alife.Client;

public record UpdateInfo(string Version, string? ReleaseNotes);

public static class ClientUpgrade
{
    public const string ReleasesApiUrl = "https://api.github.com/repos/BDFFZI/Alife/releases";
    public static string CurrentVersion => AlifeContext.AppVersion;
    public static UpdateInfo? NewVersion { get; private set; }

    public static async Task FetchNewVersion()
    {
        string response = await AlifeUtility.FetchStringAsync($"{ReleasesApiUrl}/latest", TimeSpan.FromSeconds(7));
        JObject json = JObject.Parse(response);

        string? tagName = json["tag_name"]?.ToString();
        if (tagName == null)
            throw new Exception("客户端版本同步失败，无法获取到标签信息。");

        string remoteVersion = tagName.TrimStart('v');
        if (new Version(remoteVersion) <= new Version(AlifeContext.AppVersion))
        {
            NewVersion = null;
            return; //本地比云端新
        }

        NewVersion = new UpdateInfo(remoteVersion, json["body"]?.ToString());
    }
    public static async Task InstallNewVersion(IAlifeClient alifeClient, Action<int>? onProgress = null)
    {
        if (NewVersion == null)
            throw new Exception("没有新版本。");

        //更新包从哪来、是什么格式、如何安装与重启，全部由各平台客户端自己处理
        await alifeClient.UpgradeClient(NewVersion.Version, onProgress);
    }
}