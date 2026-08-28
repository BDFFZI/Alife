using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.GameCompanion.Screen;

namespace Alife.Function.GameCompanion.Collectors;

/// <summary>
/// 颜色开关采样器：检测区域内如有像素匹配任一颜色代称（误差范围内），
/// <see cref="Value"/> 输出 "true"；无匹配时输出 null（不推送）。
/// 状态变化时标记更新时间供防抖。
/// </summary>
public sealed class ColorSwitchCollector(ColorSwitchConfig config) : CollectorBase
{
    bool matched;
    string? debugColor; // 点模式下的当前像素颜色

    static ColorSwitchCollector()
    {
        CollectorRegistry.Register<ColorSwitchConfig>(
            "颜色开关",
            cfg => new ColorSwitchCollector(cfg),
            cfg => cfg.ColorOptions is { Count: > 0 },
            ui: """
                <div class="t-specific-row"><label>检测形状</label><select data-shape="Region"></select></div>
                <div class="t-specific-row"><label>检测区域</label><span data-region="Region"></span></div>
                <div class="t-specific-row"><label>目标颜色</label><div data-colors="ColorOptions"></div></div>
                """);
    }

    public override string Name => config.Name;
    public override double DebounceSeconds => config.DebounceSeconds;
    // 恒为布尔状态："true"（匹配）/ "false"（不匹配）
    public override string? Value => matched ? "true" : "false";
    // 点模式 DebugValue 恒显示当前像素颜色；其他模式显示布尔状态
    public override string? DebugValue => config.Region.IsPoint ? debugColor : Value;

    public override Task Update(GameContext ctx, CancellationToken ct)
    {
        List<Color>? pixels = ctx.Frame?.GetShapePixels(config.Region);
        bool nowMatched = false;
        if (pixels is not null && pixels.Count > 0)
        {
            foreach (Color pixel in pixels)
            {
                if (ColorMatchHelper.MatchColor(pixel, config.ColorOptions) is not null)
                {
                    nowMatched = true;
                    break;
                }
            }
            // 点模式：DebugValue 恒显示采样到的第一个像素颜色
            if (config.Region.IsPoint)
                debugColor = $"#{pixels[0].R:X2}{pixels[0].G:X2}{pixels[0].B:X2}";
        }

        if (nowMatched != matched)
        {
            matched = nowMatched;
            
        }
        return Task.CompletedTask;
    }
}