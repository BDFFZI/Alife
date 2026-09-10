using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Alife.Plugin.LanguageModelRouter;

/// <summary>单个渠道组配置</summary>
public class GroupChannel
{
    public string GroupName { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string? ReasoningEffort { get; set; }
    public string? ExtraHeaders { get; set; }
    public string? ExtraBody { get; set; }
    public string? ExtraBodyNotThinking { get; set; } // 非思考模式请求体（对齐官方 extraBodyNotThinking，默认 {"thinking":{"type":"disabled"}}）

    /// <summary>是否具备可用渠道（Endpoint 与 ApiKey 均非空）</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);
}

public class LanguageModelRouterConfig
{
    public const int MinGroups = 1;
    public const int MaxGroups = 12;

    /// <summary>密文标记前缀（区分 DPAPI 密文与旧明文配置）</summary>
    public const string ProtectedPrefix = "dpapi:v1:";

    /// <summary>DPAPI 熵值：绑定插件身份，防止其他程序直接解密本插件密文</summary>
    static readonly byte[] SecretEntropy =
        Encoding.UTF8.GetBytes("Alife.Plugin.LanguageModelRouter.ApiKey.v1");

    /// <summary>动态渠道组列表（列表顺序即容灾顺序，第 1 个即主组）</summary>
    public List<GroupChannel> Groups { get; set; } = new();

    // === 旧版固定字段（v4.0.0 及之前，仅用于迁移旧配置，新配置不再写入）===
    public string GroupName1 { get; set; } = "";
    public string Endpoint1 { get; set; } = "";
    public string ModelId1 { get; set; } = "";
    public string ApiKey1 { get; set; } = "";
    public string? ReasoningEffort1 { get; set; }
    public string? ExtraHeaders1 { get; set; }
    public string? ExtraBody1 { get; set; }

    public string GroupName2 { get; set; } = "";
    public string? Endpoint2 { get; set; }
    public string? ModelId2 { get; set; }
    public string? ApiKey2 { get; set; }
    public string? ReasoningEffort2 { get; set; }
    public string? ExtraHeaders2 { get; set; }
    public string? ExtraBody2 { get; set; }

    public string GroupName3 { get; set; } = "";
    public string? Endpoint3 { get; set; }
    public string? ModelId3 { get; set; }
    public string? ApiKey3 { get; set; }
    public string? ReasoningEffort3 { get; set; }
    public string? ExtraHeaders3 { get; set; }
    public string? ExtraBody3 { get; set; }

    public string GroupName4 { get; set; } = "";
    public string? Endpoint4 { get; set; }
    public string? ModelId4 { get; set; }
    public string? ApiKey4 { get; set; }
    public string? ReasoningEffort4 { get; set; }
    public string? ExtraHeaders4 { get; set; }
    public string? ExtraBody4 { get; set; }

    // === 旧版排序（仅迁移用）===
    public int[] GroupOrder { get; set; } = new[] { 0, 1, 2, 3 };

    // === 容灾设置 ===
    public string? ErrorKeywords { get; set; } // 逗号分隔
    public int RetryDelayMs { get; set; } = 1000;
    public int RequestTimeoutMs { get; set; } = 30000; // 单次渠道请求超时（毫秒）：渠道挂起/无响应时按此超时视为失败并触发容灾，避免长时间卡住
    public int StreamIdleTimeoutMs { get; set; } = 0; // SSE 流空闲超时（毫秒）：已建立连接但长时间未收到任何数据（含 keep-alive 注释行）视为异常，0=关闭
    public bool PriorityMainChannel { get; set; } = false; // 优先主渠道：每次请求先试主渠道，全程静默容灾
    public bool ShowThinkingChain { get; set; } = true; // 是否将 reasoning/thinking 转为可见思维链

    // === 智能思考/非思考切换（对齐官方 OpenAILanguageModel 机制）===
    public bool SmartThinkingEnabled { get; set; } = false; // 独立开关：启用后默认非思考，AI 需要深度思考时自动切回思考
    public bool IgnorePersistentThinking { get; set; } = false; // 忽略框架"隐式功能激活中"类永久占用：只响应当次任务真正需要思考的信号（如即将调用工具、函数出错等），
                                                                // 使历史中调用过工具的角色日常闲聊也能走非思考（默认关闭，保持官方行为）
    public string? ThinkingTriggerKeywords { get; set; } // 自定义关键词触发思考（逗号分隔）：用户消息命中任一关键词时强制走思考模式，
                                                         // 即使在默认非思考模式下也会临时切回思考（适合需要深度推理的场景）

    // === 运行时状态（每个桌宠独立，随配置持久化）===
    public int ForcedGroupIndex { get; set; } = -1; // -1=自动容灾, >=0=强制使用 Groups 索引
    public bool AutoFailoverEnabled { get; set; } = true; // 是否启用自动容灾切换

    // === 安全设置 ===
    public bool EncryptApiKeys { get; set; } = true; // 是否加密 API Key 存储：开启=Windows DPAPI 密文（仅当前系统用户可解密）；关闭=明文保存于配置文件（与官方语言模型插件一致）
    public bool IgnoreSslCertificate { get; set; } = true; // 忽略 TLS 证书校验错误：默认开启以兼容自签名/证书异常的渠道；关闭后走系统证书校验，可防中间人攻击（自签名证书渠道将无法连接）

    /// <summary>
    /// 确保 Groups 可用：迁移旧扁平字段、保证至少 1 组、截断上限。
    /// 旧配置（Endpoint1~4 等）在首次加载时按旧 GroupOrder 顺序迁移为 Groups。
    /// </summary>
    public void EnsureGroups()
    {
        Groups ??= new List<GroupChannel>();

        if (Groups.Count == 0 && HasLegacyConfig())
            Groups = MigrateFromLegacy();

        if (Groups.Count == 0)
            Groups.Add(new GroupChannel());

        if (Groups.Count > MaxGroups)
            Groups = Groups.Take(MaxGroups).ToList();

        // 修正强制锁定索引：越界、指向已删除组、或指向未配置组时复位为自动容灾，
        // 避免 UI 显示"强制锁定某组"但实际请求仍从主组开始的不一致
        if (ForcedGroupIndex >= Groups.Count || (ForcedGroupIndex >= 0 && !Groups[ForcedGroupIndex].IsConfigured))
            ForcedGroupIndex = -1;

        EnsureApiKeysProtected();
    }

    bool HasLegacyConfig()
        => !string.IsNullOrWhiteSpace(Endpoint1)
        || !string.IsNullOrWhiteSpace(Endpoint2)
        || !string.IsNullOrWhiteSpace(Endpoint3)
        || !string.IsNullOrWhiteSpace(Endpoint4)
        || !string.IsNullOrWhiteSpace(ApiKey1)
        || !string.IsNullOrWhiteSpace(ApiKey2)
        || !string.IsNullOrWhiteSpace(ApiKey3)
        || !string.IsNullOrWhiteSpace(ApiKey4);

    List<GroupChannel> MigrateFromLegacy()
    {
        var legacy = new List<GroupChannel>
        {
            new() { GroupName = GroupName1, Endpoint = Endpoint1, ModelId = ModelId1, ApiKey = ApiKey1, ReasoningEffort = ReasoningEffort1, ExtraHeaders = ExtraHeaders1, ExtraBody = ExtraBody1 },
            new() { GroupName = GroupName2, Endpoint = Endpoint2 ?? "", ModelId = ModelId2 ?? "", ApiKey = ApiKey2 ?? "", ReasoningEffort = ReasoningEffort2, ExtraHeaders = ExtraHeaders2, ExtraBody = ExtraBody2 },
            new() { GroupName = GroupName3, Endpoint = Endpoint3 ?? "", ModelId = ModelId3 ?? "", ApiKey = ApiKey3 ?? "", ReasoningEffort = ReasoningEffort3, ExtraHeaders = ExtraHeaders3, ExtraBody = ExtraBody3 },
            new() { GroupName = GroupName4, Endpoint = Endpoint4 ?? "", ModelId = ModelId4 ?? "", ApiKey = ApiKey4 ?? "", ReasoningEffort = ReasoningEffort4, ExtraHeaders = ExtraHeaders4, ExtraBody = ExtraBody4 },
        };

        var order = GroupOrder ?? new[] { 0, 1, 2, 3 };
        var used = new HashSet<int>();
        var ordered = new List<GroupChannel>();
        foreach (int v in order)
        {
            if (v >= 0 && v < legacy.Count && used.Add(v))
                ordered.Add(legacy[v]);
        }
        for (int i = 0; i < legacy.Count; i++)
        {
            if (used.Add(i))
                ordered.Add(legacy[i]);
        }
        return ordered;
    }

    /// <summary>新增一个空组，返回是否成功（达到上限返回 false）</summary>
    public bool AddGroup()
    {
        EnsureGroups();
        if (Groups.Count >= MaxGroups)
            return false;
        Groups.Add(new GroupChannel());
        return true;
    }

    /// <summary>删除指定索引的组（主组不可删，至少保留 1 组），并修正强制锁定索引</summary>
    public bool RemoveGroup(int zeroBasedIndex)
    {
        EnsureGroups();
        if (Groups.Count <= MinGroups)
            return false;
        if (zeroBasedIndex < 0 || zeroBasedIndex >= Groups.Count)
            return false;
        // 不允许删主组（第 1 组）
        if (zeroBasedIndex == 0)
            return false;

        Groups.RemoveAt(zeroBasedIndex);
        if (ForcedGroupIndex == zeroBasedIndex)
            ForcedGroupIndex = -1;
        else if (ForcedGroupIndex > zeroBasedIndex)
            ForcedGroupIndex--;
        return true;
    }

    /// <summary>
    /// 按 EncryptApiKeys 开关统一处理所有 Key，幂等：
    /// - 开启：明文 Key（含旧版扁平字段）原地迁移为当前 Windows 用户可解密的 DPAPI 密文；
    /// - 关闭：密文解密回明文保存（与官方语言模型插件一致），便于跨机器迁移/直观查看。
    /// </summary>
    public void EnsureApiKeysProtected()
    {
        if (Groups != null)
        {
            foreach (var ch in Groups)
                ch.ApiKey = EncryptApiKeys
                    ? (ProtectSecret(ch.ApiKey) ?? "")
                    : (UnprotectSecret(ch.ApiKey) ?? "");
        }

        // 旧版扁平字段迁移进 Groups 后不再使用，但随开关一并处理，避免明文残留或密文无意义
        ApiKey1 = EncryptApiKeys ? (ProtectSecret(ApiKey1) ?? "") : UnprotectSecret(ApiKey1);
        ApiKey2 = EncryptApiKeys ? ProtectSecret(ApiKey2) : UnprotectSecret(ApiKey2);
        ApiKey3 = EncryptApiKeys ? ProtectSecret(ApiKey3) : UnprotectSecret(ApiKey3);
        ApiKey4 = EncryptApiKeys ? ProtectSecret(ApiKey4) : UnprotectSecret(ApiKey4);
    }

    /// <summary>加密机密：空值或已加密（带前缀）原样返回，其余用 DPAPI（CurrentUser）加密。</summary>
    public static string? ProtectSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            return value;

        try
        {
            byte[] encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value), SecretEntropy, DataProtectionScope.CurrentUser);
            return ProtectedPrefix + Convert.ToBase64String(encrypted);
        }
        catch (Exception ex)
        {
            // DPAPI 不可用（如非 Windows 平台）时保持明文可用，不让加密拖垮请求链路
            Console.WriteLine($"[灵枢] API Key 加密失败，暂以明文保存：{ex.Message}");
            return value;
        }
    }

    /// <summary>解密机密：空值返回空串；无前缀视为旧明文配置原样返回（首次迁移前兼容）；
    /// 解密失败（密文来自其他 Windows 用户或已损坏）按未配置处理，避免泄漏或误发。</summary>
    public static string UnprotectSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        if (!value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            return value;

        try
        {
            byte[] encrypted = Convert.FromBase64String(value[ProtectedPrefix.Length..]);
            byte[] plain = ProtectedData.Unprotect(
                encrypted, SecretEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return "";
        }
    }
}
