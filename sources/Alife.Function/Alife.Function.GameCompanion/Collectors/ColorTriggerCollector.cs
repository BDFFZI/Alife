using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.GameCompanion.Screen;

namespace Alife.Function.GameCompanion.Collectors;

/// <summary>
/// 颜色触发采样器：检测区域内如有像素匹配任一颜色代称（误差范围内）即命中，
/// 命中时累计一个触发时间戳。<see cref="Value"/> 返回本次累计的所有时间戳（逗号分隔，未命中为 null）；
/// 框架推送后 <see cref="Use"/> 清空。
/// </summary>
public sealed class ColorTriggerCollector(ColorTriggerConfig config) : CollectorBase
{
    readonly List<string> triggers = new();
    bool lastHit; // 上一帧是否命中（上升沿检测，Use 后不清空：持续命中不重复触发）
    DateTime lastMissTime = DateTime.MinValue; // 进入未命中状态的时间（防抖从这里算起）
    string? debugColor; // 点模式下的当前像素颜色

    static ColorTriggerCollector()
    {
        CollectorRegistry.Register<ColorTriggerConfig>(
            "颜色触发",
            cfg => new ColorTriggerCollector(cfg),
            cfg => cfg.ColorOptions is { Count: > 0 },
            ui: """
                <div class="t-specific-row"><label>检测形状</label><select data-shape="Region"></select></div>
                <div class="t-specific-row"><label>检测区域</label><span data-region="Region"></span></div>
                <div class="t-specific-row"><label>目标颜色</label><div data-colors="ColorOptions"></div></div>
                """);
    }

    public override string Name => config.Name;
    public override double DebounceSeconds => config.DebounceSeconds;
    public override string? Value => triggers.Count == 0 ? null : string.Join(",", triggers);
    // 点模式 DebugValue 恒显示当前像素颜色；其他模式显示触发时间戳
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
            // 点模式：DebugValue 恒显示采样到的第一个像素颜色
            if (config.Region.IsPoint)
                debugColor = $"#{pixels[0].R:X2}{pixels[0].G:X2}{pixels[0].B:X2}";
        }

        DateTime now = DateTime.UtcNow;
        // 从命中进入未命中的时刻记录防抖起点
        if (!hit && lastHit)
            lastMissTime = now;

        // 上升沿（未命中→命中）且「离开已满防抖时间」才触发：
        // 持续命中不重复触发；离开满防抖后才重新出现才会再次触发
        if (hit && !lastHit)
        {
            if ((now - lastMissTime).TotalSeconds >= Math.Max(0, DebounceSeconds))
                triggers.Add(DateTime.Now.ToString("HH:mm:ss"));
        }
        lastHit = hit;
        return Task.CompletedTask;
    }
}