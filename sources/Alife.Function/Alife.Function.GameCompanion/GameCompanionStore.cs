using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alife.Foundation;
using Newtonsoft.Json;

namespace Alife.Function.GameCompanion;

/// <summary>
/// 全局陪玩配置存储（类似 Skills 的目录式做法，不走 IConfigurable 配置系统）。
/// 目录：Storage/GameCompanion/
///   - {游戏名}.json  每个游戏一份配置（含该游戏的采样间隔）
/// 所有角色共享同一份陪玩配置。
/// </summary>
public class GameCompanionStore
{
    /// <summary>陪玩配置根目录。</summary>
    public string RootDirectory { get; }

    public GameCompanionStore()
    {
        RootDirectory = Path.Combine(AlifePath.StorageFolderPath, "GameCompanion");
        // 使用陪玩功能即创建配置目录，保证"定位配置文件"等始终可用
        try { Directory.CreateDirectory(RootDirectory); } catch { }
    }

    /// <summary>列出所有已配置的游戏（按配置文件名，无效文件忽略）。</summary>
    public List<GameConfig> ListGames()
    {
        var games = new List<GameConfig>();
        if (!Directory.Exists(RootDirectory))
            return games;

        foreach (string file in Directory.GetFiles(RootDirectory, "*.json"))
        {
            GameConfig? game = ReadGame(Path.GetFileNameWithoutExtension(file));
            if (game != null)
                games.Add(game);
        }
        return games;
    }

    /// <summary>按名称读取游戏配置（不存在或损坏返回 null）。</summary>
    public GameConfig? GetGame(string gameName)
    {
        return ReadGame(SanitizeFileName(gameName));
    }

    /// <summary>保存游戏配置（按游戏名落盘，不存在则新建）。</summary>
    public void SaveGame(GameConfig game)
    {
        if (string.IsNullOrWhiteSpace(game.GameName))
            return;
        Directory.CreateDirectory(RootDirectory);
        string path = PathFor(game.GameName);
        File.WriteAllText(path, JsonConvert.SerializeObject(game, Formatting.Indented));
    }

    /// <summary>
    /// 以保存清单为准清理未提及的游戏文件（编辑器为全量权威来源，处理重命名/删除）。
    /// </summary>
    public void SyncRemoved(ICollection<string> keptNames)
    {
        if (!Directory.Exists(RootDirectory))
            return;
        var kept = new HashSet<string>(keptNames, StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(RootDirectory, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!kept.Contains(name))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    /// <summary>删除游戏配置。</summary>
    public void DeleteGame(string gameName)
    {
        string path = PathFor(gameName);
        if (File.Exists(path))
            File.Delete(path);
    }

    GameConfig? ReadGame(string fileName)
    {
        try
        {
            string path = Path.Combine(RootDirectory, fileName + ".json");
            if (!File.Exists(path))
                return null;
            GameConfig? game = JsonConvert.DeserializeObject<GameConfig>(File.ReadAllText(path));
            if (game == null)
                return null;
            game.GameName = fileName; // 文件名即游戏名（配置内容不存名称）
            return game;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>将游戏名转换为安全文件名（剔除路径非法字符）。</summary>
    string PathFor(string gameName) => Path.Combine(RootDirectory, SanitizeFileName(gameName) + ".json");

    static string SanitizeFileName(string gameName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string name = gameName ?? "";
        foreach (char c in invalid)
            name = name.Replace(c, '_');
        return name.Trim();
    }
}