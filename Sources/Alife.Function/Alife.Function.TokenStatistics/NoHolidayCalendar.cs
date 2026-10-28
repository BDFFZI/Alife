using System;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 无节假日数据时的兜底日历：所有日期都返回 Unknown，高峰判定退化为"周一至周五"。
/// 节假日数据可用后，由 <see cref="HolidayCalendar"/> 替换。
/// </summary>
public sealed class NoHolidayCalendar : IHolidayCalendar
{
    public static readonly NoHolidayCalendar Instance = new();

    NoHolidayCalendar()
    {
    }

    public HolidayKind KindOf(DateOnly date) => HolidayKind.Unknown;
}
