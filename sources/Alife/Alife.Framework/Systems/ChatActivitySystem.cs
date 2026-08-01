using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alife.Framework;

public class ChatActivitySystem
{
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
    public event Action<ChatActivity>? Destroying;

    /// <summary>
    /// 活动销毁并移出全局统计后
    /// </summary>
    public event Action<ChatActivity>? Destroyed;

    public IEnumerable<ChatActivity> GetAllChatActivities()
    {
        return activities.Values;
    }

    public bool IsActivated(Character character)
    {
        return activities.ContainsKey(character.Name);
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
            Progress<(string, float)> progress = new(tuple => { ActivatingProcess?.Invoke(character, tuple); });

            Activating?.Invoke(character);
            ChatActivity chatActivity = new(character, configurationSystem, moduleSystem, characterSystem, appendObjects);
            await chatActivity.Awake(progress);
            ActivatingCreated?.Invoke(chatActivity);
            await chatActivity.Start(progress);
            activities.Add(character.Name, chatActivity);
            Activated?.Invoke(chatActivity);
            return chatActivity;
        }
        catch (Exception ex)
        {
            ActivationFailed?.Invoke(character, ex);
            return null;
        }
    }

    /// <summary>
    /// 销毁角色。UI 应通过订阅 Destroying/Destroyed 事件来感知流程。
    /// </summary>
    public async Task Deactivate(Character character)
    {
        if (!activities.TryGetValue(character.Name, out ChatActivity? chatActivity))
            return;

        Destroying?.Invoke(chatActivity);
        await chatActivity.Destroy();
        activities.Remove(character.Name);
        Destroyed?.Invoke(chatActivity);
    }

    public ChatActivitySystem(
        StorageSystem storageSystem,
        ConfigurationSystem configurationSystem,
        CharacterSystem characterSystem,
        PluginSystem pluginSystem,
        ModuleSystem moduleSystem)
    {
        appendObjects = [
            storageSystem,
            configurationSystem,
            characterSystem,
            pluginSystem,
            moduleSystem,
            this
        ];

        this.moduleSystem = moduleSystem;
        this.configurationSystem = configurationSystem;
        this.characterSystem = characterSystem;
    }

    readonly ModuleSystem moduleSystem;
    readonly ConfigurationSystem configurationSystem;
    readonly CharacterSystem characterSystem;
    readonly object[] appendObjects;
    readonly Dictionary<string, ChatActivity> activities = new();
}