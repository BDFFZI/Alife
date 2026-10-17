using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 颜色触发采样器：检测区域内如有像素匹配任一颜色代称（误差范围内）即命中，
/// 命中时累计一个触发时间戳。<see cref="Value"/> 返回本次累计的所有时间戳（逗号分隔，未命中为 null）；
/// 框架推送后 <see cref="Use"/> 清空。
/// </summary>
[Collector(typeof(ColorTriggerConfig), "颜色触发",
    Ui = """
        <div class="t-specific-row"><label>检测形状</label><select data-shape="Region"></select></div>
        <div class="t-specific-row"><label>检测区域</label><span data-region="Region"></span></div>
        <div class="t-specific-row"><label>目标颜色</label><div data-colors="ColorOptions"></div></div>
        """)]
public sealed class ColorTriggerCollector(ColorTriggerConfig config) : CollectorBase
{
    readonly List<string> triggers = new();
    bool lastHit;
    DateTime lastMissTime = DateTime.MinValue;
    string? debugColor;

    public override CollectConfigBase Config => config;
    public override string? Value => triggers.Count == 0 ? null : string.Join(",", triggers);
    public override string? DebugValue => config.Region.IsPoint ? debugColor : Value;

    public override void Use() => triggers.Clear();

    public override Task Update(GameContext ctx, CancellationToken ct)
    {
        List<Color>? pixels = ctx.Frame?.GetShapePixels(config.Region);
        bool hit = false;
        if (pixels is not null && pixels.Count > 0)
        {
            foreach (Color pixel in pixels)
            {
                if (ColorMatchHelper.MatchColor(pixel, config.ColorOptions) is not null)
                {
                    hit = true;
                    break;
                }
            }
            if (config.Region.IsPoint)
                debugColor = $"#{pixels[0].R:X2}{pixels[0].G:X2}{pixels[0].B:X2}";
        }

        DateTime now = DateTime.UtcNow;
        if (!hit && lastHit)
            lastMissTime = now;

        if (hit && !lastHit)
        {
            if ((now - lastMissTime).TotalSeconds >= Math.Max(0, config.DebounceSeconds))
                triggers.Add(DateTime.Now.ToString("HH:mm:ss"));
        }
        lastHit = hit;
        return Task.CompletedTask;
    }
}
