using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.GameCompanion.Collector;
using Alife.Function.GameCompanion.Screen;

namespace Alife.Function.GameCompanion.Implement;

/// <summary>
/// 颜色内容采样器：检测区域内如有像素匹配任一颜色代称（误差范围内），
/// <see cref="Value"/> 返回最接近匹配的颜色名称；无匹配时返回 null。
/// <see cref="DebugValue"/> 返回十六进制色值。值变化时标记更新时间供防抖。
/// </summary>
[Collector(typeof(ColorContentConfig), "颜色内容",
    Ui = """
        <div class="t-specific-row"><label>检测形状</label><select data-shape="Region"></select></div>
        <div class="t-specific-row"><label>检测区域</label><span data-region="Region"></span></div>
        <div class="t-specific-row"><label>颜色值</label><div data-colors="ColorOptions"></div></div>
        """)]
public sealed class ColorContentCollector(ColorContentConfig config) : Collector.CollectorBase
{
    string? value;
    string? debugValue;

    public override CollectConfigBase Config => config;
    public override string? Value => value;
    public override string? DebugValue => debugValue;

    public override Task Update(GameContext ctx, CancellationToken ct)
    {
        List<Color>? pixels = ctx.Frame?.GetShapePixels(config.Region);
        if (pixels is null || pixels.Count == 0)
        {
            value = null;
            debugValue = null;
            return Task.CompletedTask;
        }

        string? bestMatch = null;
        string? bestHex = null;
        foreach (Color pixel in pixels)
        {
            string? match = ColorMatchHelper.MatchColor(pixel, config.ColorOptions);
            if (match is not null)
            {
                bestMatch = match;
                bestHex = $"#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}";
                break;
            }
        }

        if (config.Region.IsPoint)
        {
            debugValue = bestHex ?? $"#{pixels[0].R:X2}{pixels[0].G:X2}{pixels[0].B:X2}";
        }
        if (bestMatch != value)
        {
            value = bestMatch;
            if (!config.Region.IsPoint)
                debugValue = bestHex;
        }
        return Task.CompletedTask;
    }
}
