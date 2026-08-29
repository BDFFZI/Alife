using System.Collections.Generic;
using System.Drawing;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>颜色匹配辅助方法，供颜色内容/颜色触发/颜色开关复用。</summary>
static class ColorMatchHelper
{
    /// <summary>在颜色代称列表中查找与指定像素最接近的匹配项（误差范围内）。返回匹配项名称，无匹配返回 null。</summary>
    public static string? MatchColor(System.Drawing.Color pixel, List<NamedColor>? options)
    {
        if (options == null || options.Count == 0)
            return null;
        string? bestName = null;
        long bestDistance = long.MaxValue;
        foreach (var opt in options)
        {
            if (string.IsNullOrWhiteSpace(opt.Name))
                continue;
            var (r, g, b) = opt.ToRgb();
            long dr = r - pixel.R, dg = g - pixel.G, db = b - pixel.B;
            long distance = dr * dr + dg * dg + db * db;
            int tolerance = System.Math.Max(0, opt.Tolerance);
            if (distance <= (long)tolerance * tolerance && distance < bestDistance)
            {
                bestDistance = distance;
                bestName = opt.Name;
            }
        }
        return bestName;
    }
}
