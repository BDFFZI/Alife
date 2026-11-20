using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Alife.Function.GameCompanion;

/// <summary>
/// 陪玩配置的编辑器交互 DTO（编辑器一次性读写全部游戏配置）。
/// 磁盘上实际按目录存储：Storage/GameCompanion/ 下每个游戏一个 JSON。
/// Games 采用 JObject 保留原始字段（含 GameName），以便写入时识别文件名。
/// </summary>
public class CompanionConfigBundle
{
    /// <summary>所有已配置的游戏陪玩方案（含 GameName 字段）。</summary>
    public List<JObject> Games { get; set; } = new();
}