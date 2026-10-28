using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 节假日日历，数据来自 holiday-cn（GitHub 直链，项目内已有 GitHub 访问能力）。
/// 按年缓存到角色的 TokenStatistics\holidays\{年}.json。
/// 刷新策略：缓存文件超过 <see cref="CacheTtlDays"/> 天、且年份属于"最近两年"时后台重拉；
/// 下载失败则沿用旧缓存（不丢数据）。完全没有数据时 <see cref="KindOf"/> 返回 Unknown，由调用方按星期判断。
/// </summary>
public sealed class HolidayCalendar : IHolidayCalendar
{
    const string SourceUrlFormat = "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{0}.json";

    /// <summary>缓存有效期：超过这个天数且年份较新就重新拉一次（应对公告后续修订）。</summary>
    public const int CacheTtlDays = 30;

    readonly string cacheFolderPath;
    readonly Func<string, Task<string?>> download;
    readonly Dictionary<int, Dictionary<DateOnly, HolidayKind>> yearMap = new();
    readonly HashSet<int> missingYears = new();

    /// <param name="cacheFolderPath">角色的 holidays 缓存目录。</param>
    /// <param name="download">给定 URL 返回内容，失败返回 null。由调用方注入（便于镜像与测试）。</param>
    public HolidayCalendar(string cacheFolderPath, Func<string, Task<string?>> download)
    {
        this.cacheFolderPath = cacheFolderPath;
        this.download = download;
    }

    /// <summary>是否有任一年份的节假日数据不可用（UI 据此标注）。</summary>
    public bool IsDataMissing => missingYears.Count > 0;

    /// <summary>已成功加载的年份。</summary>
    public IReadOnlyList<int> LoadedYears => yearMap.Keys.OrderBy(year => year).ToList();

    /// <summary>数据不可用的年份。</summary>
    public IReadOnlyList<int> MissingYears => missingYears.OrderBy(year => year).ToList();

    public HolidayKind KindOf(DateOnly date)
    {
        if (yearMap.TryGetValue(date.Year, out Dictionary<DateOnly, HolidayKind>? days) == false)
            return HolidayKind.Unknown;
        return days.TryGetValue(date, out HolidayKind kind) ? kind : HolidayKind.Unknown;
    }

    /// <summary>
    /// 加载指定年份：先读缓存，缓存缺失或已过期时下载；下载失败沿用旧缓存。
    /// </summary>
    /// <param name="year">年份。</param>
    /// <param name="force">true 表示忽略缓存直接重拉（面板"刷新"用）。</param>
    /// <returns>该年份最终是否有可用数据。</returns>
    public async Task<bool> EnsureYearAsync(int year, bool force = false)
    {
        bool loaded = yearMap.ContainsKey(year);
        if (force == false && (loaded || missingYears.Contains(year)))
            return loaded;

        string cachePath = Path.Combine(cacheFolderPath, $"{year}.json");
        bool hasCache = File.Exists(cachePath);

        string? content = null;

        // 缓存新鲜就直接用，避免每次启动都发请求
        if (hasCache && force == false && NeedsRefresh(cachePath, year) == false)
            content = File.ReadAllText(cachePath);

        if (content == null)
        {
            string? downloaded = await download(string.Format(SourceUrlFormat, year));
            if (downloaded != null)
            {
                Directory.CreateDirectory(cacheFolderPath);
                File.WriteAllText(cachePath, downloaded);
                content = downloaded;
            }
            else if (hasCache)
            {
                // 下载失败（断网/未公布）时保留旧数据
                content = File.ReadAllText(cachePath);
            }
        }

        if (content == null)
        {
            missingYears.Add(year);
            return false;
        }

        yearMap[year] = ParseYear(content);
        missingYears.Remove(year);
        return true;
    }

    /// <summary>强制刷新若干年份，返回实际刷新成功的年份（用于面板提示）。</summary>
    public async Task<IReadOnlyList<int>> RefreshAsync(IEnumerable<int> years)
    {
        List<int> refreshed = new();
        foreach (int year in years.Distinct().OrderBy(year => year))
        {
            if (await EnsureYearAsync(year, force: true))
                refreshed.Add(year);
        }
        return refreshed;
    }

    /// <summary>缓存是否该重拉：只有"最近两年"值得刷新（更早的年份不会再变）。</summary>
    static bool NeedsRefresh(string cachePath, int year)
    {
        if (year < DateTime.Now.Year - 1)
            return false;

        try
        {
            TimeSpan age = DateTime.Now - File.GetLastWriteTime(cachePath);
            return age.TotalDays >= CacheTtlDays;
        }
        catch (Exception)
        {
            return true;
        }
    }

    static Dictionary<DateOnly, HolidayKind> ParseYear(string json)
    {
        Dictionary<DateOnly, HolidayKind> map = new();
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("days", out JsonElement days) == false)
            return map;

        foreach (JsonElement day in days.EnumerateArray())
        {
            string dateText = day.GetProperty("date").GetString() ?? "";
            if (DateOnly.TryParse(dateText, out DateOnly date) == false)
                continue;
            bool isOffDay = day.GetProperty("isOffDay").GetBoolean();
            map[date] = isOffDay ? HolidayKind.Holiday : HolidayKind.WorkingAdjustment;
        }
        return map;
    }
}
