using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Mindustry;

public sealed record MindustryBridgeState(
    bool DataDirectoryExists,
    bool ModInstalled,
    bool ModCurrent,
    bool ConfigReady,
    bool BridgeListening,
    bool Authorized,
    bool InGame,
    string Message,
    string DataDirectory,
    string ConfigPath,
    string GameExecutablePath)
{
    public bool Installed => ModInstalled && ModCurrent && ConfigReady;
    public bool GameExecutableFound => string.IsNullOrWhiteSpace(GameExecutablePath) == false;
    public string GameDirectory => GameExecutableFound ? Path.GetDirectoryName(GameExecutablePath)! : "";
    public bool GameRunning => GameExecutableFound
        && Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExecutablePath)).Length > 0;
}

public sealed class MindustryBridgeInstallation(string pluginDirectory)
{
    const string BridgeFileName = "alife-mindustry.jar";
    const string ConfigFileName = "alife-mindustry.properties";

    public string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mindustry");

    public async Task<MindustryBridgeState> InstallAsync(MindustryConfig configuration, CancellationToken cancellationToken = default)
    {
        if (configuration.TeamId is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(configuration.TeamId), "AI 队伍编号必须在 0 到 255 之间。");

        string dataDirectory = ResolveDataDirectory(configuration.DataDirectory);
        string modsDirectory = Path.Combine(dataDirectory, "mods");
        string modPath = Path.Combine(modsDirectory, BridgeFileName);
        string configPath = Path.Combine(dataDirectory, ConfigFileName);
        Directory.CreateDirectory(modsDirectory);

        string sourcePath = Path.Combine(pluginDirectory, "Resources", "Server", BridgeFileName);
        if (File.Exists(sourcePath) == false)
            throw new FileNotFoundException("插件安装包缺少 Mindustry 桥接 JAR。", sourcePath);
        File.Copy(sourcePath, modPath, true);

        Dictionary<string, string> existingConfig = ReadConfig(configPath);
        existingConfig.TryGetValue("token", out string? existingToken);
        string token = IsValidToken(configuration.Token)
            ? configuration.Token
            : existingToken is not null && IsValidToken(existingToken)
                ? existingToken
                : CreateToken();

        configuration.DataDirectory = dataDirectory;
        configuration.Token = token;
        WriteConfig(configPath, configuration.Port, token, configuration.TeamId);
        return await InspectAsync(configuration, cancellationToken);
    }

    public async Task<MindustryBridgeState> InspectAsync(MindustryConfig configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            return await InspectCoreAsync(configuration, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return CreateState(
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                $"检测失败：{exception.Message}",
                configuration.DataDirectory,
                FindGameExecutable(configuration.GameDirectory, configuration.DataDirectory) ?? "");
        }
    }

    async Task<MindustryBridgeState> InspectCoreAsync(MindustryConfig configuration, CancellationToken cancellationToken)
    {
        string configuredDataDirectory = ResolveDataDirectory(configuration.DataDirectory);
        string? gameExecutablePath = FindGameExecutable(configuration.GameDirectory, configuredDataDirectory);
        string dataDirectory = gameExecutablePath == null
            ? configuredDataDirectory
            : DetectDataDirectory(Path.GetDirectoryName(gameExecutablePath)!, configuredDataDirectory);
        configuration.DataDirectory = dataDirectory;
        string modPath = Path.Combine(dataDirectory, "mods", BridgeFileName);
        string configPath = Path.Combine(dataDirectory, ConfigFileName);
        string sourcePath = Path.Combine(pluginDirectory, "Resources", "Server", BridgeFileName);
        bool dataDirectoryExists = Directory.Exists(dataDirectory);
        if (gameExecutablePath == null)
            return CreateState(false, false, false, false, false, false, false,
                "没有找到 Mindustry 游戏。请选择解压后包含 Mindustry.exe 的游戏根目录。",
                dataDirectory,
                "");
        configuration.GameDirectory = Path.GetDirectoryName(gameExecutablePath)!;

        bool modInstalled = File.Exists(modPath);
        bool modCurrent = modInstalled && File.Exists(sourcePath) && FilesEqual(sourcePath, modPath);
        Dictionary<string, string> fileConfig = ReadConfig(configPath);

        bool teamReady = fileConfig.TryGetValue("team", out string? teamText)
            ? int.TryParse(teamText, out int configTeam) && configTeam == configuration.TeamId
            : configuration.TeamId == 0;
        bool configReady = fileConfig.TryGetValue("port", out string? portText)
            && int.TryParse(portText, out int configPort)
            && configPort == configuration.Port
            && fileConfig.TryGetValue("token", out string? configToken)
            && IsValidToken(configToken)
            && configToken == configuration.Token
            && teamReady;

        if (dataDirectoryExists == false)
            return CreateState(false, false, false, false, false, false, false,
                "已找到游戏，但没有找到 Mod 数据目录。请先启动一次游戏，或选择便携版数据目录。",
                dataDirectory,
                gameExecutablePath);
        if (modInstalled == false)
            return CreateState(true, false, false, configReady, false, false, false,
                "已找到游戏。下一步点击“安装 Mod”，安装完成后启动游戏。",
                dataDirectory,
                gameExecutablePath);
        if (modCurrent == false)
            return CreateState(true, true, false, configReady, false, false, false,
                "桥接 Mod 需要更新，点击“安装 Mod”同步当前插件版本。",
                dataDirectory,
                gameExecutablePath);
        if (configReady == false)
            return CreateState(true, true, true, false, false, false, false,
                "模块参数与游戏配置文件不一致，点击“安装 Mod”重新同步。",
                dataDirectory,
                gameExecutablePath);

        (HttpStatusCode? responseCode, string? gameStatus) = await QueryStatusAsync(
            configuration.Port,
            configuration.Token,
            cancellationToken);
        if (responseCode == null)
            return CreateState(true, true, true, true, false, false, false,
                IsGameRunning(gameExecutablePath)
                    ? "游戏已启动，但桥接尚未响应。请确认 Mod 已启用，必要时重启游戏。"
                    : "Mod 已安装。启动游戏并进入地图后会自动连接。",
                dataDirectory,
                gameExecutablePath);

        if (responseCode != HttpStatusCode.OK)
            return CreateState(true, true, true, true, true, false, false,
                responseCode == HttpStatusCode.Unauthorized
                    ? "端口已有服务响应，但令牌不匹配。请重新安装并在底部保存配置。"
                    : "端口已有服务响应，但没有通过 Mindustry 桥接握手。请检查桥接端口。",
                dataDirectory,
                gameExecutablePath);

        if (gameStatus == null)
            return CreateState(true, true, true, true, true, false, false,
                "端口已有服务响应，但桥接没有返回可读取的状态。请确认端口对应的是 Mindustry 桥接。",
                dataDirectory,
                gameExecutablePath);

        bool inGame = gameStatus.StartsWith("当前关卡", StringComparison.Ordinal);
        string firstLine = gameStatus.Split('\n', 2)[0].Trim();
        return CreateState(true, true, true, true, true, true, inGame,
            inGame ? firstLine : "桥接已连接。进入地图后会自动识别，即可让 AI 开始陪玩。",
            dataDirectory,
            gameExecutablePath);
    }

    public string ResolveDataDirectory(string? configuredDirectory)
    {
        string dataDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? DefaultDataDirectory
            : Environment.ExpandEnvironmentVariables(configuredDirectory.Trim());
        return Path.GetFullPath(dataDirectory);
    }

    public bool IsGameDirectory(string directory)
    {
        return FindGameExecutable(directory, null) != null;
    }

    public string SuggestedDataDirectory(string gameDirectory)
    {
        return DetectDataDirectory(Path.GetFullPath(gameDirectory), DefaultDataDirectory);
    }

    static string DetectDataDirectory(string gameDirectory, string configuredDataDirectory)
    {
        string portableDataDirectory = Path.Combine(gameDirectory, "data");
        if (LooksLikeDataDirectory(portableDataDirectory))
            return Path.GetFullPath(portableDataDirectory);
        if (LooksLikeDataDirectory(configuredDataDirectory))
            return Path.GetFullPath(configuredDataDirectory);
        if (LooksLikeDataDirectory(gameDirectory))
            return Path.GetFullPath(gameDirectory);
        return Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mindustry"));
    }

    static bool LooksLikeDataDirectory(string directory)
    {
        return File.Exists(Path.Combine(directory, "settings.bin"))
            || File.Exists(Path.Combine(directory, "settings_backup.bin"))
            || File.Exists(Path.Combine(directory, "last_log.txt"))
            || Directory.Exists(Path.Combine(directory, "logs"))
            || Directory.Exists(Path.Combine(directory, "saves"));
    }

    static MindustryBridgeState CreateState(
        bool dataDirectoryExists,
        bool modInstalled,
        bool modCurrent,
        bool configReady,
        bool bridgeListening,
        bool authorized,
        bool inGame,
        string message,
        string dataDirectory,
        string gameExecutablePath)
    {
        return new(
            dataDirectoryExists,
            modInstalled,
            modCurrent,
            configReady,
            bridgeListening,
            authorized,
            inGame,
            message,
            dataDirectory,
            Path.Combine(dataDirectory, ConfigFileName),
            gameExecutablePath);
    }

    static string? FindGameExecutable(string? configuredGameDirectory, string? dataDirectory)
    {
        foreach (string? candidate in new[] { configuredGameDirectory, dataDirectory })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string directory;
            try
            {
                directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate.Trim()));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            foreach (string fileName in new[] { "Mindustry.exe", "Mindustry-desktop.jar", "Mindustry.jar" })
            {
                string path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                    return path;
            }
        }
        return null;
    }

    static bool IsGameRunning(string executablePath)
    {
        string processName = Path.GetFileNameWithoutExtension(executablePath);
        return Process.GetProcessesByName(processName).Length > 0;
    }

    static bool FilesEqual(string leftPath, string rightPath)
    {
        if (new FileInfo(leftPath).Length != new FileInfo(rightPath).Length)
            return false;

        using FileStream left = File.OpenRead(leftPath);
        using FileStream right = File.OpenRead(rightPath);
        byte[] leftHash = SHA256.HashData(left);
        byte[] rightHash = SHA256.HashData(right);
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    static Dictionary<string, string> ReadConfig(string path)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path) == false)
            return values;
        foreach (string line in File.ReadAllLines(path))
        {
            string value = line.Trim();
            if (value.Length == 0 || value.StartsWith('#'))
                continue;
            int separator = value.IndexOf('=');
            if (separator <= 0)
                continue;
            values[value[..separator].Trim()] = value[(separator + 1)..].Trim();
        }
        return values;
    }

    static void WriteConfig(string path, int port, string token, int teamId)
    {
        File.WriteAllText(path,
            $"# Alife Mindustry bridge. This file is managed by the Alife module.\nport={port}\ntoken={token}\nteam={teamId}\n");
    }

    static string CreateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    }

    static bool IsValidToken(string? token)
    {
        return token is { Length: >= 24 and <= 128 }
            && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }

    static async Task<(HttpStatusCode? Code, string? Status)> QueryStatusAsync(
        int port,
        string token,
        CancellationToken cancellationToken)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(3) };
        using HttpRequestMessage request = new(HttpMethod.Get, $"http://127.0.0.1:{port}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            string status = await response.Content.ReadAsStringAsync(cancellationToken);
            return (response.StatusCode, status);
        }
        catch (HttpRequestException)
        {
            return (null, null);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested == false)
        {
            return (null, null);
        }
    }
}
