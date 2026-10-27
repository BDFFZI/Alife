
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alife.Framework;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// Token 统计模块（角色级 ChatBehaviour）。
/// 随角色激活由 ConstructContainer 自动构造，无需注册到任何 DI 容器（"走系统"）。
/// OnStart 时本角色所有模块已 Awake，模型（实现 ITokenUser，作为 ChatBot 依赖）已在容器中，
/// 故在此订阅其 TokenUsed 事件，把每轮开销交给内部 <see cref="TokenStatisticsService"/> 聚合；
/// OnDestroy 精确退订，保证插件热重载/角色重激活不重复订阅。
/// 统计 UI（系统菜单页）经 ChatActivitySystem 遍历各角色、取出本模块实例读 Service 展示。
/// </summary>
[Module(
    "Token 统计",
    "记录本角色每轮对话的 token 开销、命中率与花费，按自然日聚合，可在系统管理中心查看。",
    defaultCategory: "Alife 官方/系统工具"
)]
public class TokenStatisticsModule(StorageSystem storageSystem) : ChatBehaviour
{
    // 已订阅的模型实例 → 其事件委托，用于 OnDestroy 精确退订
    readonly Dictionary<ITokenUser, Action<TokenUsageContext>> subscribedHandlers = new();

    /// <summary>本角色的统计聚合服务，OnStart 后非空。供系统菜单 UI 读取。</summary>
    public TokenStatisticsService? Service { get; private set; }

    protected override Task OnStart()
    {
        // 按角色存储键隔离持久化，多角色互不覆盖
        Service = new TokenStatisticsService(storageSystem, Character.StorageKey);

        // 订阅本角色当前已存在的所有 ITokenUser（模型通常在此时已构造）
        foreach (object instance in ChatActivity.Container.Instances)
        {
            if (instance is ITokenUser tokenUser)
                Subscribe(tokenUser);
        }

        // 兜时序：模型若晚于本模块构造（懒构造/热重载重建），挂容器事件补订阅
        ChatActivity.Container.InstanceCreated += OnInstanceCreated;

        return Task.CompletedTask;
    }

    Task OnInstanceCreated(object instance)
    {
        if (instance is ITokenUser tokenUser)
            Subscribe(tokenUser);
        return Task.CompletedTask;
    }

    void Subscribe(ITokenUser tokenUser)
    {
        if (subscribedHandlers.ContainsKey(tokenUser))
            return;
        Action<TokenUsageContext> handler = context => Service?.Record(context);
        tokenUser.TokenUsed += handler;
        subscribedHandlers[tokenUser] = handler;
    }

    protected override Task OnDestroy()
    {
        // 回收：退订容器事件 + 退订所有模型事件，避免热重载重复订阅（指南第4条要求）
        ChatActivity.Container.InstanceCreated -= OnInstanceCreated;
        foreach ((ITokenUser tokenUser, Action<TokenUsageContext> handler) in subscribedHandlers)
            tokenUser.TokenUsed -= handler;
        subscribedHandlers.Clear();
        return Task.CompletedTask;
    }
}
