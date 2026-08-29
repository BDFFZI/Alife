using System.Collections.Generic;
using Alife.Function.GameCompanion.Collector;
using Newtonsoft.Json;

namespace Alife.Function.GameCompanion;

/// <summary>
/// 单个游戏的陪玩配置：采样项。
/// 每游戏一个 JSON 文件，文件名即游戏名（存储键，配置内容不保存名称）。
/// </summary>
public class GameConfig
{
    /// <summary>
    /// 游戏名称（运行时身份）。不随配置持久化，由 Store 在读取时按文件名注入。
    /// </summary>
    [JsonIgnore]
    public string GameName { get; set; } = "";

    /// <summary>
    /// 采样项配置列表（多态）：以「{Name, Sampler, Config}」包装持久化，
    /// 运行时还原为具体配置子类，见 <see cref="CollectorConfigListConverter"/>。
    /// </summary>
    [JsonConverter(typeof(CollectorConfigListConverter))]
    public List<CollectConfigBase> Collectors { get; set; } = new();

    /// <summary>本游戏配置的基准屏幕宽度（物理像素）。0=尚未记录。</summary>
    public int BaseScreenWidth { get; set; }

    /// <summary>本游戏配置的基准屏幕高度（物理像素）。0=尚未记录。</summary>
    public int BaseScreenHeight { get; set; }
}