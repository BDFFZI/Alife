using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.GameCompanion.Screen;

namespace Alife.Function.GameCompanion.Collectors;

/// <summary>
/// 颜色内容采样器：检测区域内如有像素匹配任一颜色代称（误差范围内），
/// <see cref="Value"/> 返回最接近匹配的颜色名称；无匹配时返回 null。
/// <see cref="DebugValue"/> 返回十六进制色值。值变化时标记更新时间供防抖。
/// </summary>
public sealed class ColorContentCollector(ColorContentConfig config) : CollectorBase
{
    string? value;
    string? debugValue;

    static ColorContentCollector()
    {
        CollectorRegistry.Register<ColorContentConfig>(
            "颜色内容",
            cfg => new ColorContentCollector(cfg),
            ui: """
                <div class="t-specific-row"><label>检测形状</label><select data-shape="Region"></select></div>
                <div class="t-specific-row"><label>检测区域</label><span data-region="Region"></span></div>
                <div class="t-specific-row"><label>颜色值</label><div data-colors="ColorOptions"></div></div>
                """);
    }

    public override string Name => config.Name;
    public override double DebounceSeconds => config.DebounceSeconds;
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

        // 点模式下 DebugValue 恒显示采样到的颜色值；区域模式仅在匹配到颜色时显示
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