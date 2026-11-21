using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Alife.Client;

sealed class GithubMirrorProbe : IDisposable
{
    public sealed record Result(long? Milliseconds, string? Error);

    public const string GithubOrigin = "https://github.com";
    public const string RawOrigin = "https://raw.githubusercontent.com";
    public const string ApiOrigin = "https://api.github.com";

    const int SampleSize = 64 * 1024;
    const int MaximumControlResponseSize = 256 * 1024;
    const string WindowsAssetName = "Alife.Client.Windows.exe";
    const string AndroidAssetName = "Alife.Client.Android.apk";
    const string ReadmeUrl = RawOrigin + "/BDFFZI/Alife/refs/heads/master/README.md";
    const string MarketZipUrl = GithubOrigin + "/BDFFZI/Alife.PluginMarket/archive/refs/heads/main.zip";
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(7);

    readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public static string GetUrl(string domain, string upstreamUrl) => $"https://{domain}/{upstreamUrl}";

    public async Task<Result> RunAsync(string domain, CancellationToken cancellationToken)
    {
        string assetUrl = "";
        long assetSize = 0;
        Task<Result> raw = CheckAsync("Raw", async token =>
        {
            using HttpResponseMessage response = await GetAsync(domain, ReadmeUrl, token);
            await response.Content.LoadIntoBufferAsync(MaximumControlResponseSize, token);
            string content = await response.Content.ReadAsStringAsync(token);
            if (content.Contains("\n# Alife ", StringComparison.Ordinal) == false)
                throw new InvalidDataException();
        }, cancellationToken);

        Task<Result> market = CheckAsync("插件市场", async token =>
        {
            using HttpResponseMessage response = await GetAsync(domain, MarketZipUrl, token, sampleResponse: true);
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
                if (range?.Unit != "bytes" || range.From != 0 || range.To != SampleSize - 1 ||
                    range.Length is not long length || length < SampleSize)
                    throw new InvalidDataException();
            }
            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            byte[] sample = new byte[SampleSize];
            await stream.ReadExactlyAsync(sample, token);
            if (sample.AsSpan().StartsWith("PK\x03\x04"u8) == false)
                throw new InvalidDataException();
        }, cancellationToken);

        bool isAndroid = OperatingSystem.IsAndroid();
        string assetName = isAndroid ? AndroidAssetName : WindowsAssetName;
        Result api = await CheckAsync("更新 API", async token =>
        {
            using HttpResponseMessage response = await GetAsync(domain, $"{ClientUpgrade.ReleasesApiUrl}/latest", token);
            await response.Content.LoadIntoBufferAsync(MaximumControlResponseSize, token);
            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            JsonElement release = json.RootElement;
            string? tag = release.GetProperty("tag_name").GetString();
            if (Version.TryParse(tag?.TrimStart('v'), out Version? version) == false)
                throw new InvalidDataException();

            string expectedUrl = $"{ClientUpgrade.ReleasesDownloadUrl}/v{version}/{assetName}";
            foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("browser_download_url").GetString() == expectedUrl &&
                    asset.GetProperty("size").TryGetInt64(out long size) && size >= SampleSize)
                {
                    assetUrl = expectedUrl;
                    assetSize = size;
                    return;
                }
            }
            throw new InvalidDataException();
        }, cancellationToken);

        Result installer = api.Error is null
            ? await CheckAsync("安装包", async token =>
            {
                using HttpResponseMessage response = await GetAsync(domain, assetUrl, token, sampleResponse: true);
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
                    if (range?.Unit != "bytes" || range.From != 0 || range.To != SampleSize - 1 || range.Length != assetSize)
                        throw new InvalidDataException();
                }
                else if (response.Content.Headers.ContentLength is long length && length != assetSize)
                    throw new InvalidDataException();

                await using Stream stream = await response.Content.ReadAsStreamAsync(token);
                byte[] sample = new byte[SampleSize];
                await stream.ReadExactlyAsync(sample, token);
                if (isAndroid ? sample.AsSpan().StartsWith("PK\x03\x04"u8) == false : sample.AsSpan().StartsWith("MZ"u8) == false)
                    throw new InvalidDataException();
            }, cancellationToken)
            : new Result(null, null);

        Result[] checks = [api, await market, installer, await raw];
        string errors = string.Join("；", checks.Select(result => result.Error).OfType<string>());
        return errors.Length == 0
            ? new Result(checks.Sum(result => result.Milliseconds), null)
            : new Result(null, errors);
    }

    public void Dispose() => client.Dispose();

    async Task<HttpResponseMessage> GetAsync(string domain, string upstreamUrl, CancellationToken token, bool sampleResponse = false)
    {
        Uri target = new(GetUrl(domain, upstreamUrl));
        for (int redirects = 0; ; redirects++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, target);
            request.Headers.UserAgent.ParseAdd("Alife-MirrorCheck/1.0");
            if (sampleResponse)
                request.Headers.Range = new RangeHeaderValue(0, SampleSize - 1);

            HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            HttpStatusCode status = response.StatusCode;
            if (status == HttpStatusCode.OK || (sampleResponse && status == HttpStatusCode.PartialContent))
                return response;

            Uri? location = response.Headers.Location;
            response.Dispose();
            if (status is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                throw new HttpRequestException(null, null, status);
            if (redirects == 3)
                throw new HttpRequestException("重定向过多");
            if (location is null || Uri.TryCreate(target, location, out Uri? next) == false ||
                next.Scheme != Uri.UriSchemeHttps || next.IsDefaultPort == false ||
                string.Equals(next.IdnHost, domain, StringComparison.OrdinalIgnoreCase) == false)
                throw new HttpRequestException("重定向离开镜像");
            target = next;
        }
    }

    static async Task<Result> CheckAsync(string name, Func<CancellationToken, Task> check, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            await check(timeout.Token);
            return new Result(stopwatch.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
        {
            return new Result(null, $"{name}超时");
        }
        catch (HttpRequestException exception)
        {
            string reason = exception.StatusCode is HttpStatusCode status ? $"HTTP {(int)status}"
                : exception.HttpRequestError == HttpRequestError.Unknown ? exception.Message : "连接或读取失败";
            return new Result(null, $"{name} {reason}");
        }
        catch (IOException)
        {
            return new Result(null, $"{name}数据不完整");
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new Result(null, $"{name}内容校验失败");
        }
    }
}
