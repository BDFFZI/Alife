using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 一套命名价格配置，模型是「基线价 + 可选的时间覆盖规则」：
/// 默认只有一条基线价（大多数模型都是这样）；某些模型在特定时段更贵，就加一条覆盖规则。
/// 单价单位为「元 / 百万 tokens」（DeepSeek 官方单位）。
/// </summary>
public sealed class PriceProfile
{
    /// <summary>稳定标识（全局配置库里唯一；角色只保存这个 id）。</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>是否为内置配置（内置配置不可删除，可"恢复内置默认"）。</summary>
    public bool IsDefault { get; set; }

    /// <summary>基线单价：没有命中任何覆盖规则时使用。</summary>
    public UnitPrices Baseline { get; set; } = new();

    /// <summary>时间覆盖规则，按顺序匹配，先命中先用。</summary>
    public List<PriceRule> Overrides { get; set; } = [];

    /// <summary>该时刻是否落在某条覆盖规则里。</summary>
    public bool IsOverridden(DateTime beijingTime, IHolidayCalendar calendar)
        => MatchRule(beijingTime, calendar) is not null;

    /// <summary>取某一时刻适用的单价。</summary>
    public UnitPrices PricesAt(DateTime beijingTime, IHolidayCalendar calendar)
        => MatchRule(beijingTime, calendar)?.Prices ?? Baseline;

    /// <summary>命中的覆盖规则（没有则 null）。</summary>
    public PriceRule? MatchRule(DateTime beijingTime, IHolidayCalendar calendar)
    {
        if (Overrides is null || Overrides.Count == 0)
            return null;

        int minuteOfDay = beijingTime.Hour * 60 + beijingTime.Minute;
        bool workday = IsWorkingDay(beijingTime, calendar);

        foreach (PriceRule rule in Overrides)
        {
            if (rule.Windows is null || rule.Windows.Count == 0)
                continue;
            if (rule.WorkdaysOnly && workday == false)
                continue;

            foreach (TimeWindow window in rule.Windows)
            {
                if (minuteOfDay >= window.StartMinute && minuteOfDay < window.EndMinute)
                    return rule;
            }
        }

        return null;
    }

    static bool IsWorkingDay(DateTime beijingTime, IHolidayCalendar calendar)
    {
        // 节假日表的结论（含调休）优先于星期判断
        DateOnly date = DateOnly.FromDateTime(beijingTime);
        HolidayKind kind = calendar.KindOf(date);
        return kind switch
        {
            HolidayKind.Holiday => false,
            HolidayKind.WorkingAdjustment => true,
            _ => beijingTime.DayOfWeek != DayOfWeek.Saturday && beijingTime.DayOfWeek != DayOfWeek.Sunday,
        };
    }
}

/// <summary>一条时间覆盖规则：在某些时段（可选"仅工作日"）使用另一套单价。</summary>
public sealed class PriceRule
{
    /// <summary>规则名（仅用于展示，如"高峰"）。</summary>
    public string Name { get; set; } = "";

    /// <summary>是否只在工作日生效（节假日表优先）。</summary>
    public bool WorkdaysOnly { get; set; } = true;

    /// <summary>覆盖的时段（分钟，左闭右开）。</summary>
    public List<TimeWindow> Windows { get; set; } = [];

    /// <summary>这些时段使用的单价。</summary>
    public UnitPrices Prices { get; set; } = new();
}

/// <summary>某一时段的单价，单位元 / 百万 tokens。</summary>
public sealed class UnitPrices
{
    /// <summary>输入·缓存命中。</summary>
    public double InputHitPerM { get; set; }

    /// <summary>输入·缓存未命中。</summary>
    public double InputMissPerM { get; set; }

    /// <summary>输出。</summary>
    public double OutputPerM { get; set; }
}

/// <summary>一个时段区间，以分钟计（0–1440），左闭右开。</summary>
public readonly record struct TimeWindow(int StartMinute, int EndMinute);

/// <summary>某一天在节假日表中的类型。</summary>
public enum HolidayKind
{
    /// <summary>表中未记录，按星期判断。</summary>
    Unknown,
    /// <summary>法定节假日，放假（即使是周一至周五也不是工作日）。</summary>
    Holiday,
    /// <summary>调休上班（即使是周末也是工作日）。</summary>
    WorkingAdjustment,
}

/// <summary>节假日查询接口，具体数据来源（缓存文件 / 在线下载）由实现决定。</summary>
public interface IHolidayCalendar
{
    HolidayKind KindOf(DateOnly date);
}
