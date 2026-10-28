using System;
using System.IO;
using System.Text.Json;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 角色级的价格选择：只保存用到的配置 id，配置本体在全局库（<see cref="PriceProfileStore"/>）。
/// 文件：Storage\Character\<角色>\TokenStatistics\price.json
/// </summary>
public static class TokenPriceStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>角色选择的价格配置 id（文件缺失时用默认配置）。</summary>
    public static string LoadProfileId(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("profileId", out JsonElement id))
                {
                    string? text = id.GetString();
                    if (string.IsNullOrWhiteSpace(text) == false)
                        return text;
                }
            }
        }
        catch (Exception)
        {
            // 旧格式或损坏：回退默认
        }

        return PriceCalculator.DefaultProfileName;
    }

    /// <summary>保存角色选择的价格配置 id。</summary>
    public static bool SaveProfileId(string path, string profileId)
    {
        try
        {
            string? folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder) == false)
                Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonSerializer.Serialize(new { profileId }, Options));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
