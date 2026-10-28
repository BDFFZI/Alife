using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 价格计算（纯函数）。费用 = 未命中输入·单价 + 命中输入·单价 + 输出·单价（元，单价为每百万 tokens）。
/// 默认配置 deepseek-flash 已与官网核实（2026 年，见交接文档 3.5 节）。
/// </summary>
public static class PriceCalculator
{
    public const string DefaultProfileName = "deepseek-flash";

    /// <summary>官网 deepseek-flash 价格（元 / 百万 tokens）：基线=空闲价，高峰为时间覆盖规则。</summary>
    public static PriceProfile CreateDefaultProfile() => PriceProfileStore.CreatePeakProfile();

    /// <summary>
    /// 把一次调用按"输入侧/输出侧"拆分费用（元），用于对话记录里分别展示两端的开销。
    /// </summary>
    public static (double InputCost, double OutputCost) Split(
        UnitPrices prices, int inputTotal, int inputCached, int outputTotal)
    {
        int inputMiss = Math.Max(0, inputTotal - inputCached);
        double inputCost = inputMiss / 1_000_000d * prices.InputMissPerM
                         + inputCached / 1_000_000d * prices.InputHitPerM;
        double outputCost = outputTotal / 1_000_000d * prices.OutputPerM;
        return (inputCost, outputCost);
    }

    /// <summary>
    /// 计算一次调用的费用（元）。
    /// <paramref name="inputTotal"/> 为总输入，其中 <paramref name="inputCached"/> 为缓存命中部分。
    /// </summary>
    public static double Cost(UnitPrices prices, int inputTotal, int inputCached, int outputTotal)
    {
        int inputMiss = Math.Max(0, inputTotal - inputCached);
        return inputMiss / 1_000_000d * prices.InputMissPerM
             + inputCached / 1_000_000d * prices.InputHitPerM
             + outputTotal / 1_000_000d * prices.OutputPerM;
    }

    /// <summary>按北京时间时刻，选用高峰或空闲单价计算费用。</summary>
    public static double CostAt(
        PriceProfile profile,
        DateTime beijingTime,
        IHolidayCalendar calendar,
        int inputTotal,
        int inputCached,
        int outputTotal)
    {
        UnitPrices prices = profile.PricesAt(beijingTime, calendar);
        return Cost(prices, inputTotal, inputCached, outputTotal);
    }


}
