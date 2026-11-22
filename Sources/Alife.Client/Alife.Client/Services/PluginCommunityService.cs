using Alife.Foundation;
using Newtonsoft.Json;

namespace Alife.Client;

public class PluginDiscussion
{
    [JsonProperty("reactions")]
    public Dictionary<string, int>? Reactions { get; set; }

    [JsonProperty("url")]
    public string? Url { get; set; }

    public List<PluginComment> Comments { get; set; } = [];

    public int GetReaction(string reaction) => Reactions?.GetValueOrDefault(reaction) ?? 0;
}

public class PluginComment
{
    public string Author { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string Url { get; set; } = "";
    public List<PluginComment> Replies { get; set; } = [];
}

public class PluginCommunityService
{
    public event Action? SnapshotUpdated;

    public int GetUpVotes(string pluginId) => snapshots.GetValueOrDefault(pluginId)?.Up ?? 0;

    public int GetDownVotes(string pluginId) => snapshots.GetValueOrDefault(pluginId)?.Down ?? 0;

    public string GetDiscussionUrl(string pluginId)
    {
        string? url = snapshots.GetValueOrDefault(pluginId)?.Url;
        return string.IsNullOrWhiteSpace(url)
            ? $"https://github.com/{Repository}/discussions?discussions_q={Uri.EscapeDataString(pluginId)}"
            : url;
    }

    public Task RefreshSnapshotAsync() =>
        snapshotRefreshTask ??= RefreshSnapshotCoreAsync();

    public async Task<PluginDiscussion?> FetchDiscussionAsync(string pluginId)
    {
        string? discussionUrl = snapshots.GetValueOrDefault(pluginId)?.Url;
        string query = TryGetDiscussionNumber(discussionUrl, out int number)
            ? $"repo={Uri.EscapeDataString(Repository)}&number={number}&last=50"
            : $"repo={Uri.EscapeDataString(Repository)}&term={Uri.EscapeDataString(pluginId)}&category=General&strict=false&last=50";

        try
        {
            string json = await AlifeUtility.FetchStringAsync($"https://giscus.app/api/discussions?{query}", TimeSpan.FromSeconds(15));
            GiscusDiscussion? discussion = JsonConvert.DeserializeObject<GiscusResponse>(json)?.Discussion;
            if (discussion?.Reactions == null)
                return null;

            return new PluginDiscussion {
                Reactions = discussion.Reactions.ToDictionary(
                    reaction => reaction.Key,
                    reaction => reaction.Value.Count),
                Url = discussion.Url,
                Comments = discussion.Comments?
                    .OrderBy(comment => comment.CreatedAt)
                    .Select(MapComment)
                    .ToList() ?? []
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            AlifeLog.LogWarning($"实时拉取插件 {pluginId} 的讨论数据失败：{ex}");
            return null;
        }
    }

    async Task RefreshSnapshotCoreAsync()
    {
        try
        {
            string json = await AlifeUtility.FetchStringAsync(SnapshotUrl, TimeSpan.FromSeconds(15));
            snapshots = JsonConvert.DeserializeObject<Dictionary<string, CommunitySnapshot>>(json)
                ?? new Dictionary<string, CommunitySnapshot>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            AlifeLog.LogWarning($"加载插件社区数据失败：{ex}");
            return;
        }
        finally
        {
            snapshotRefreshTask = null;
        }

        SnapshotUpdated?.Invoke();
    }

    static PluginComment MapComment(GiscusComment comment)
    {
        return new PluginComment {
            Author = comment.Author?.Login ?? "",
            AvatarUrl = comment.Author?.AvatarUrl ?? "",
            BodyHtml = comment.BodyHtml,
            CreatedAt = comment.CreatedAt,
            Url = comment.Url,
            Replies = comment.Replies?.OrderBy(reply => reply.CreatedAt).Select(MapComment).ToList() ?? []
        };
    }

    static bool TryGetDiscussionNumber(string? discussionUrl, out int number)
    {
        number = 0;
        if (Uri.TryCreate(discussionUrl, UriKind.Absolute, out Uri? url) == false)
            return false;

        string[] segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int discussionsIndex = Array.IndexOf(segments, "discussions");
        return discussionsIndex >= 0 &&
               discussionsIndex + 1 < segments.Length &&
               int.TryParse(segments[discussionsIndex + 1], out number);
    }

    const string Repository = "BDFFZI/Alife.PluginMarket";
    const string SnapshotUrl = "https://raw.githubusercontent.com/BDFFZI/Alife.PluginMarket/refs/heads/main/community.json";

    IReadOnlyDictionary<string, CommunitySnapshot> snapshots = new Dictionary<string, CommunitySnapshot>();
    Task? snapshotRefreshTask;

    class CommunitySnapshot
    {
        [JsonProperty("up")]
        public int Up { get; set; }

        [JsonProperty("down")]
        public int Down { get; set; }

        [JsonProperty("url")]
        public string? Url { get; set; }
    }

    class GiscusResponse
    {
        [JsonProperty("discussion")]
        public GiscusDiscussion? Discussion { get; set; }
    }

    class GiscusDiscussion
    {
        [JsonProperty("url")]
        public string Url { get; set; } = "";

        [JsonProperty("reactions")]
        public Dictionary<string, GiscusReaction>? Reactions { get; set; }

        [JsonProperty("comments")]
        public List<GiscusComment>? Comments { get; set; }
    }

    class GiscusReaction
    {
        [JsonProperty("count")]
        public int Count { get; set; }
    }

    class GiscusComment
    {
        [JsonProperty("author")]
        public GiscusAuthor? Author { get; set; }

        [JsonProperty("bodyHTML")]
        public string BodyHtml { get; set; } = "";

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; } = "";

        [JsonProperty("replies")]
        public List<GiscusComment>? Replies { get; set; }
    }

    class GiscusAuthor
    {
        [JsonProperty("login")]
        public string Login { get; set; } = "";

        [JsonProperty("avatarUrl")]
        public string AvatarUrl { get; set; } = "";
    }
}
