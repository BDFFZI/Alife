using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 全局价格配置库（所有角色共享）：Storage\TokenStatistics\price-profiles.json。
/// 内置两个默认配置：
/// - deepseek-flash：基线价 + 一条"高峰"覆盖规则；
/// - deepseek-flash（全谷）：只有基线价。
/// 角色只保存"选用了哪个配置 id"，见 <see cref="TokenPriceStore"/>。
/// </summary>
public static class PriceProfileStore
{
    public const string OffPeakOnlyId = "deepseek-flash-offpeak";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>内置默认配置：官方峰谷价（基线=空闲价，高峰为覆盖规则）。</summary>
    public static PriceProfile CreatePeakProfile() => new()
    {
        Id = PriceCalculator.DefaultProfileName,
        Name = PriceCalculator.DefaultProfileName,
        IsDefault = true,
        Baseline = new UnitPrices { InputHitPerM = 0.02, InputMissPerM = 1, OutputPerM = 4 },
        Overrides =
        [
            new PriceRule {
                Name = "高峰",
                WorkdaysOnly = true,
                Windows = [new TimeWindow(9 * 60, 12 * 60), new TimeWindow(14 * 60, 18 * 60)],
                Prices = new UnitPrices { InputHitPerM = 0.04, InputMissPerM = 2, OutputPerM = 8 },
            },
        ],
    };

    /// <summary>内置默认配置：只有基线价（大多数模型就是这样）。</summary>
    public static PriceProfile CreateOffPeakProfile() => new()
    {
        Id = OffPeakOnlyId,
        Name = "deepseek-flash（全谷）",
        IsDefault = true,
        Baseline = new UnitPrices { InputHitPerM = 0.02, InputMissPerM = 1, OutputPerM = 4 },
        Overrides = [],
    };

    /// <summary>内置默认配置列表（每次新建，调用方可随意修改）。</summary>
    public static List<PriceProfile> CreateDefaults() => [CreatePeakProfile(), CreateOffPeakProfile()];

    /// <summary>读取全局配置库；文件缺失/损坏时返回内置默认值，并兼容旧的 peak/offPeak 格式。</summary>
    public static List<PriceProfile> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                List<PriceProfile>? loaded = JsonSerializer.Deserialize<List<PriceProfile>>(json, Options);
                if (loaded is { Count: > 0 })
                {
                    foreach (PriceProfile profile in loaded)
                    {
                        Normalize(profile, json);
                    }

                    // 保证两个内置配置始终存在
                    foreach (PriceProfile builtin in CreateDefaults())
                    {
                        if (loaded.Any(item => string.Equals(item.Id, builtin.Id, StringComparison.Ordinal)) == false)
                            loaded.Insert(0, builtin);
                    }
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // 读坏了用默认库，不要让统计页打不开
        }

        return CreateDefaults();
    }

    /// <summary>补全字段，并把旧格式（peak/offPeak/peakWindows）迁移成 基线 + 覆盖规则。</summary>
    static void Normalize(PriceProfile profile, string rawJson)
    {
        profile.Baseline ??= new UnitPrices();
        profile.Overrides ??= [];

        if (string.IsNullOrWhiteSpace(profile.Id))
            profile.Id = Guid.NewGuid().ToString("N")[..8];
        if (string.IsNullOrWhiteSpace(profile.Name))
            profile.Name = profile.Id;

        // 旧格式迁移：json 里出现 peak/offPeak 且没有 baseline
        using JsonDocument document = JsonDocument.Parse(rawJson);
        bool hasBaseline = false;
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (string.Equals(element.GetProperty("id").GetString(), profile.Id, StringComparison.Ordinal) == false)
                continue;
            hasBaseline = element.TryGetProperty("baseline", out _);
            if (hasBaseline == false)
            {
                UnitPrices baseline = TryReadPrices(element, "offPeak") ?? new UnitPrices();
                UnitPrices peak = TryReadPrices(element, "peak") ?? baseline;
                List<TimeWindow> windows = TryReadWindows(element) ?? [];

                profile.Baseline = baseline;

                // 旧格式里 peakWindows 为空 = 旧版"全谷"配置，不该凭空造一条高峰规则
                profile.Overrides = windows.Count == 0
                    ? []
                    : [
                        new PriceRule {
                            Name = "高峰",
                            WorkdaysOnly = true,
                            Windows = windows,
                            Prices = peak,
                        },
                    ];
            }
            break;
        }

        foreach (PriceRule rule in profile.Overrides)
        {
            rule.Prices ??= new UnitPrices();
            rule.Windows ??= [];
            if (string.IsNullOrWhiteSpace(rule.Name))
                rule.Name = "覆盖";
        }
    }

    static UnitPrices? TryReadPrices(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement value) == false)
            return null;
        if (value.ValueKind != JsonValueKind.Object)
            return null;

        return new UnitPrices {
            InputHitPerM = ReadDouble(value, "inputHitPerM"),
            InputMissPerM = ReadDouble(value, "inputMissPerM"),
            OutputPerM = ReadDouble(value, "outputPerM"),
        };
    }

    static List<TimeWindow>? TryReadWindows(JsonElement element)
    {
        if (element.TryGetProperty("peakWindows", out JsonElement windows) == false)
            return null;
        if (windows.ValueKind != JsonValueKind.Array)
            return null;

        List<TimeWindow> list = [];
        foreach (JsonElement item in windows.EnumerateArray())
        {
            double start = ReadDouble(item, "startMinute");
            double end = ReadDouble(item, "endMinute");
            if (end > start)
                list.Add(new TimeWindow((int)start, (int)end));
        }
        return list;
    }

    static double ReadDouble(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement value) == false)
            return 0;
        return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
    }

    /// <summary>把配置序列化成字符串（用于判断编辑后是否有未保存改动）。</summary>
    public static string Serialize(IReadOnlyList<PriceProfile> profiles)
        => JsonSerializer.Serialize(profiles, Options);

    /// <summary>写回全局配置库。</summary>
    public static bool Save(string path, IReadOnlyList<PriceProfile> profiles)
    {
        try
        {
            string? folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder) == false)
                Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonSerializer.Serialize(profiles, Options));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
