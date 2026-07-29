using System;
using System.Collections.Generic;

namespace Alife.Platform;

/// <summary>
/// 通过替换链接和配置环境变量来实现镜像下载功能
/// </summary>
public static class AlifeMirror
{
    /// <summary>
    /// URL 替换映射表。Key 为原始 URL 前缀，Value 为镜像 URL 前缀。
    /// </summary>
    public static IReadOnlyDictionary<string, string> MirrorUrlMap { get; private set; }
    /// <summary>
    /// 环境变量配置表。启动 Python 进程时会自动设置这些环境变量。
    /// </summary>
    public static IReadOnlyDictionary<string, string> MirrorEnvironmentVariables { get; private set; }

    /// <summary>
    /// 更新 URL 映射并保存。
    /// </summary>
    public static void SetMirrorUrlMap(Dictionary<string, string> urlMap)
    {
        MirrorUrlMap = urlMap;  
        Save();
    }
    /// <summary>
    /// 更新环境变量配置并保存。
    /// </summary>
    public static void SetMirrorEnvironmentVariables(Dictionary<string, string> envVars)
    {
        MirrorEnvironmentVariables = envVars;
        Save();
    }

    /// <summary>
    /// 根据 MirrorUrlMap 替换 URL 中的匹配字符串。
    /// </summary>
    public static string TransformUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        foreach (var (old, replacement) in MirrorUrlMap)
        {
            url = url.Replace(old, replacement, StringComparison.OrdinalIgnoreCase);
        }

        return url;
    }
    /// <summary>
    /// 设置环境变量，使 Python 进程能够使用镜像源。
    /// </summary>
    public static void SetupEnvironment()
    {
        foreach (var (key, value) in MirrorEnvironmentVariables)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    const string UrlMapKey = "mirror_url_map";
    const string EnvVarsKey = "mirror_env_vars";

    static AlifeMirror()
    {
        MirrorUrlMap = new Dictionary<string, string>();
        MirrorEnvironmentVariables = new Dictionary<string, string>();
        Load();
    }

    /// <summary>
    /// 从配置文件加载镜像配置。
    /// </summary>
    static void Load()
    {
        string urlMapJson = AlifeConfig.GetString(UrlMapKey);
        if (!string.IsNullOrEmpty(urlMapJson))
        {
            try
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(urlMapJson);
                if (loaded is not null)
                    MirrorUrlMap = loaded;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        string envVarsJson = AlifeConfig.GetString(EnvVarsKey);
        if (!string.IsNullOrEmpty(envVarsJson))
        {
            try
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(envVarsJson);
                if (loaded is not null)
                    MirrorEnvironmentVariables = loaded;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    /// <summary>
    /// 保存当前镜像配置到配置文件。
    /// </summary>
    static void Save()
    {
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = false };
        AlifeConfig.SetString(UrlMapKey, System.Text.Json.JsonSerializer.Serialize(MirrorUrlMap, options));
        AlifeConfig.SetString(EnvVarsKey, System.Text.Json.JsonSerializer.Serialize(MirrorEnvironmentVariables, options));
    }
}
