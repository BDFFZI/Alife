using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alife.Foundation;

namespace Alife.Framework;

public class ChatActivitySystem
{
    public HashSet<object> InjectedObjects => injectedObjects;

    /// <summary>
    /// 开始激活角色
    /// </summary>
    public event Action<Character>? Activating;

    /// <summary>
    /// 活动创建并调用Awake后
    /// </summary>
    public event Action<ChatActivity>? ActivatingCreated;

    /// <summary>
    /// 活动调用Start并正式加入统计，即完成创建后
    /// </summary>
    public event Action<ChatActivity>? Activated;

    /// <summary>
    /// 激活中的进度回调
    /// </summary>
    public event Action<Character, (string Step, float Progress)>? ActivatingProcess;

    /// <summary>
    /// 激活过程发生报错（生命周期事件不会引发该错误）
    /// </summary>
    public event Action<Character, Exception>? ActivationFailed;

    /// <summary>
    /// 活动即将销毁
    /// </summary>
    public event Action<ChatActivity>? Deactivating;

    /// <summary>
    /// 活动销毁并移出全局统计后
    /// </summary>
    public event Action<ChatActivity>? Deactivated;

    public bool IsActivated(Character character)
    {
        return activities.ContainsKey(character.Name);
    }

    public IEnumerable<ChatActivity> GetAllChatActivities()
    {
        return activities.Values;
    }
    public ChatActivity? GetChatActivity(Character character)
    {
        return activities.GetValueOrDefault(character.Name);
    }

    /// <summary>
    /// 激活角色。UI 应通过订阅 Activating/Activated/ActivationFailed 事件来感知流程。
    /// </summary>
    public async Task<ChatActivity?> Activate(Character character)
    {
        try
        {
            if (activities.TryGetValue(character.Name, out var activate))
                return activate;

            ChatActivity chatActivity = new(character, configurationSystem, moduleSystem, characterSystem, injectedObjects.ToArray());
            activities.Add(character.Name, chatActivity);

            Progress<(string, float)> progress = new(tuple => { ActivatingProcess?.Invoke(character, tuple); });

            Activating?.Invoke(character);
            await chatActivity.Awake(progress);
            ActivatingCreated?.Invoke(chatActivity);
            await chatActivity.Start(progress);

            Activated?.Invoke(chatActivity);
            return chatActivity;
        }
        catch (Exception ex)
        {
            activities.Remove(character.Name);

            ActivationFailed?.Invoke(character, ex);
            throw;
        }
    }
    /// <summary>
    /// 销毁角色。UI 应通过订阅 Destroying/Destroyed 事件来感知流程。
    /// </summary>
    public async Task Deactivate(Character character)
    {
        if (!activities.TryGetValue(character.Name, out ChatActivity? chatActivity))
            return;

        Deactivating?.Invoke(chatActivity);

        //单个模块 OnDestroy 卡住不应让「停止角色」永久无响应。
        //ChatBehaviour 内部已有超时，这里是系统级兜底（例如第三方直接构造的 ChatActivity）。
        try
        {
            //留足时间给道别：系统事件模块重写了 30s 宽限期 + 容器 3s 余量。
            //这里取 40s，确保道别能正常生成完；真卡住时仍会超时退出。
            await chatActivity.Destroy().WaitAsync(TimeSpan.FromSeconds(40));
        }
        catch (TimeoutException)
        {
            AlifeLog.LogWarning($"停止角色 {character.Name} 超时（40s），已强制移出活动列表");
        }
        catch (Exception e)
        {
            AlifeLog.LogError($"停止角色 {character.Name} 时出错：" + e.Message);
        }

        //无论成功超时都要移除，否则角色永远停在「正在停止」状态
        activities.Remove(character.Name);
        Deactivated?.Invoke(chatActivity);
    }

    public ChatActivitySystem(
        StorageSystem storageSystem,
        ConfigurationSystem configurationSystem,
        CharacterSystem characterSystem,
        ModuleSystem moduleSystem,
        IServiceProvider serviceProvider)
    {
        injectedObjects = [
            characterSystem,
            this,
            configurationSystem,
            moduleSystem,
            storageSystem,
            serviceProvider
        ];

        this.moduleSystem = moduleSystem;
        this.configurationSystem = configurationSystem;
        this.characterSystem = characterSystem;
    }

    readonly ModuleSystem moduleSystem;
    readonly ConfigurationSystem configurationSystem;
    readonly CharacterSystem characterSystem;
    readonly HashSet<object> injectedObjects;
    readonly Dictionary<string, ChatActivity> activities = new();
}