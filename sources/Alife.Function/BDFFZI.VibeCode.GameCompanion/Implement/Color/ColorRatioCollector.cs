using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using BDFFZI.VibeCode.GameCompanion;
using BDFFZI.VibeCode.GameCompanion;
using BDFFZI.VibeCode.GameCompanion;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 颜色比值采样器：在两个参考颜色之间计算当前颜色的比值（0~1）。
/// <see cref="Value"/> 返回比值字符串（如 "0.75"），变化超过阈值才更新（防抖）。
/// <see cref="DebugValue"/> 返回当前采样颜色的十六进制值。
/// </summary>
[Collector(typeof(ColorRatioConfig), "颜色比值",
    Ui = """
        <div class="t-specific-row"><label>检测形状</label><select data-shape="Region"></select></div>
        <div class="t-specific-row"><label>检测区域</label><span data-region="Region"></span></div>
        <div class="t-specific-row"><label>起点颜色</label><span data-colorhex="ColorZero"></span></div>
        <div class="t-specific-row"><label>终点颜色</label><span data-colorhex="ColorOne"></span></div>
        <div class="t-specific-row"><label>变化阈值</label><input type="number" data-cfg="ChangeThreshold" min="0" max="1" step="0.01" value="0.05" /></div>
        """)]
public sealed class ColorRatioCollector(ColorRatioConfig config) : CollectorBase
{
    string? value;
    string? debugValue;
    double lastRatio = double.NaN;

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

        // 多像素取平均
        int rSum = 0, gSum = 0, bSum = 0;
        foreach (Color pixel in pixels)
        {
            rSum += pixel.R;
            gSum += pixel.G;
            bSum += pixel.B;
        }
        int count = pixels.Count;
        double r = rSum / (double)count;
        double g = gSum / (double)count;
        double b = bSum / (double)count;

        debugValue = $"#{(byte)r:X2}{(byte)g:X2}{(byte)b:X2}";

        // 解析参考颜色
        var (zR, zG, zB) = ParseHex(config.ColorZero);
        var (oR, oG, oB) = ParseHex(config.ColorOne);

        // RGB 欧氏距离
        double dZero = Math.Sqrt(Square(r - zR) + Square(g - zG) + Square(b - zB));
        double dOne = Math.Sqrt(Square(r - oR) + Square(g - oG) + Square(b - oB));

        // 计算比值
        double ratio;
        double denom = dZero + dOne;
        if (denom < 1e-9)
            ratio = 0.5; // 两个颜色相同，取中间值
        else
            ratio = dZero / denom;

        ratio = Math.Clamp(ratio, 0, 1);

        // 防抖：变化超过阈值才更新
        if (double.IsNaN(lastRatio) || Math.Abs(ratio - lastRatio) >= config.ChangeThreshold)
        {
            value = ratio.ToString("F2");
            lastRatio = ratio;
        }

        return Task.CompletedTask;
    }

    static double Square(double x) => x * x;

    static (byte R, byte G, byte B) ParseHex(string? hex)
    {
        string h = hex?.TrimStart('#') ?? "";
        if (h.Length == 6 && int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out int v))
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return (0, 0, 0);
    }
}
