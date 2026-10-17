using System.Collections.Generic;
using Newtonsoft.Json;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 屏幕区域（物理像素坐标，原点为显示器左上角）。
/// 用于数据采集（文本 OCR 区域 / 像素点位 / 颜色触发矩形）。
/// 矩形的三角面由可选的 <see cref="Triangle"/> 承载。
/// </summary>
public class ScreenRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>区域是否为空（宽或高非正）。</summary>
    [JsonIgnore]
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>该区域中心点坐标（像素模式读取中心像素）。</summary>
    [JsonIgnore]
    public (int X, int Y) Center
    {
        get
        {
            int cx = Width > 0 ? X + Width / 2 : X;
            int cy = Height > 0 ? Y + Height / 2 : Y;
            return (cx, cy);
        }
    }

    /// <summary>是否为三角形模式（启用时用 <see cref="Triangle"/> 的 3 个顶点采样）。</summary>
    public bool IsTriangle { get; set; }

    /// <summary>是否为点模式（启用时只采样 (X,Y) 单个像素，忽略宽高）。</summary>
    public bool IsPoint { get; set; }

    /// <summary>三角形顶点（IsTriangle=true 时生效，物理像素坐标）。</summary>
    public List<ScreenPoint>? Triangle { get; set; }

    /// <summary>三角形顶点（缺省时返回 3 个空点）。</summary>
    [JsonIgnore]
    public List<ScreenPoint> TrianglePoints =>
        (Triangle != null && Triangle.Count >= 3) ? Triangle
        : new List<ScreenPoint> { new(), new(), new() };

    /// <summary>是否为扇面模式（启用时用圆心/半径/角度采样，X/Y 视为圆心）。</summary>
    public bool IsSector { get; set; }

    /// <summary>扇面半径（物理像素）。</summary>
    public int Radius { get; set; } = 100;

    /// <summary>扇面起始角（度，0=向右，顺时针为正）。</summary>
    public double StartAngle { get; set; }

    /// <summary>扇面扫过角（度，>0 且 ≤360）。</summary>
    public double SweepAngle { get; set; } = 90;

    public override string ToString() => $"{X},{Y},{Width},{Height}";
}

/// <summary>屏幕坐标点（物理像素）。</summary>
public class ScreenPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}