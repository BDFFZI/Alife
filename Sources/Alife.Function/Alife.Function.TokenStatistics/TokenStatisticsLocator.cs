using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alife.Foundation;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 统计数据的定位与装配工具。
/// 统计页需要在本角色"未激活/未加载模块"时也能看到历史数据，
/// 因此这里直接按存储目录组装服务，不依赖 <see cref="TokenStatisticsModule"/> 实例。
/// </summary>
public static class TokenStatisticsLocator
{
    /// <summary>某角色的对话记录目录。</summary>
    public static string RecordsFolderFor(string characterName)
        => Path.Combine(AlifePath.StorageFolderPath, "Character", characterName, "TokenStatistics", "records");

    /// <summary>某角色的节假日缓存目录。</summary>
    public static string HolidayFolderFor(string characterName)
        => Path.Combine(AlifePath.StorageFolderPath, "Character", characterName, "TokenStatistics", "holidays");

    /// <summary>某角色的价格选择文件（只存配置 id）。</summary>
    public static string PriceFileFor(string characterName)
        => Path.Combine(AlifePath.StorageFolderPath, "Character", characterName, "TokenStatistics", "price.json");

    /// <summary>全局价格配置库（所有角色共享）。</summary>
    public static string PriceProfilesFile
        => Path.Combine(AlifePath.StorageFolderPath, "TokenStatistics", "price-profiles.json");

    /// <summary>读取全局配置库。</summary>
    public static List<PriceProfile> LoadPriceProfiles() => PriceProfileStore.Load(PriceProfilesFile);

    /// <summary>解析某角色当前选用的价格配置（找不到就用默认配置）。</summary>
    public static PriceProfile ResolvePriceProfile(string characterName, IReadOnlyList<PriceProfile> profiles)
    {
        string id = TokenPriceStore.LoadProfileId(PriceFileFor(characterName));
        foreach (PriceProfile profile in profiles)
        {
            if (string.Equals(profile.Id, id, StringComparison.Ordinal))
                return profile;
        }
        return profiles[0];
    }

    /// <summary>
    /// 列出磁盘上所有拥有角色目录的角色名（不依赖角色是否激活）。
    /// </summary>
    public static IReadOnlyList<string> ListStoredCharacterNames()
    {
        string characterRoot = Path.Combine(AlifePath.StorageFolderPath, "Character");
        if (Directory.Exists(characterRoot) == false)
            return [];

        return Directory.GetDirectories(characterRoot)
            .Select(Path.GetFileName)
            .Where(name => string.IsNullOrEmpty(name) == false)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>为某角色创建节假日日历（读本地缓存，缺失时在线下载 holiday-cn）。</summary>
    public static HolidayCalendar CreateHolidayCalendar(string characterName)
        => new(HolidayFolderFor(characterName), HolidayDownloader.DownloadAsync);

    /// <summary>为某角色装配统计查询服务（直接读盘，价格配置取该角色的 price.json）。</summary>
    public static TokenStatisticsService CreateService(string characterName, IHolidayCalendar? calendar = null, PriceProfile? priceProfile = null)
    {
        ChatRecordStore store = new(RecordsFolderFor(characterName));
        PriceProfile profile = priceProfile ?? ResolvePriceProfile(characterName, LoadPriceProfiles());
        return new TokenStatisticsService(store, calendar ?? NoHolidayCalendar.Instance, profile);
    }

    /// <summary>该角色是否存在任何记录文件。</summary>
    public static bool HasRecords(string characterName)
    {
        string folder = RecordsFolderFor(characterName);
        if (Directory.Exists(folder) == false)
            return false;
        return Directory.EnumerateFiles(folder, "*.jsonl").Any();
    }
}
