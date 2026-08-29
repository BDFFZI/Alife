using System;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 命名的颜色枚举项：名称 + 十六进制颜色值 + 允许误差。
/// 用于像素颜色采集时，把采集到的像素匹配到"最接近的枚举名"。
/// </summary>
public class NamedColor
{
    /// <summary>枚举名，如 "红色"、"敌方蓝色"。</summary>
    public string Name { get; set; } = "";

    /// <summary>十六进制颜色值，如 "#FF0000"。</summary>
    public string Hex { get; set; } = "#FF0000";

    /// <summary>允许误差（RGB 欧氏距离上限，0~255）。默认 30。</summary>
    public int Tolerance { get; set; } = 30;

    /// <summary>解析十六进制颜色为 RGB。解析失败返回 (255,255,255)。</summary>
    public (byte R, byte G, byte B) ToRgb()
    {
        string hex = Hex?.TrimStart('#') ?? "";
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int value))
        {
            return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
        return (255, 255, 255);
    }
}