using System;
using System.Net.Http;
using System.Threading.Tasks;
using Alife.Foundation;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// holiday-cn 的下载实现：走项目统一的镜像替换（<see cref="AlifeMirror.TransformUrl"/>），
/// 失败返回 null，由 <see cref="HolidayCalendar"/> 决定是否回退到"按星期判断"。
/// </summary>
public static class HolidayDownloader
{
    static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    static HolidayDownloader()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Alife-TokenStatistics/1.0");
    }

    /// <summary>下载指定 URL 的文本内容；任何异常都吞掉并返回 null（统计页不能因此打不开）。</summary>
    public static async Task<string?> DownloadAsync(string url)
    {
        try
        {
            string finalUrl = AlifeMirror.TransformUrl(url);
            using HttpResponseMessage response = await Client.GetAsync(finalUrl);
            if (response.IsSuccessStatusCode == false)
                return null;
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
