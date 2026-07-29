using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alife.Framework;

public class ChatActivitySystem
{
    /// <summary>角色激活进度更新（taskDescription, progressValue）</summary>
    public event Action<Character>? Activating;
    public event Action<Character, (string Step, float Progress)>? ActivatingProcess;
    public event Action<Character, Exception>? ActivationFailed;
    public event Action<ChatActivity>? ActivatingCreated;
    public event Action<ChatActivity>? Activated;
    public event Action<ChatActivity>? Destroying;
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
            Progress<(string, float)> progress = new(tuple => {
                ActivatingProcess?.Invoke(character, tuple);
            });

            Activating?.Invoke(character);
            ChatActivity chatActivity = new(
                character, configurationSystem, moduleSystem,
                appendObjects.ToArray()
            );
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
        CharacterSystem characterSystem,
        ConfigurationSystem configurationSystem,
        ModuleSystem moduleSystem,
        StorageSystem storageSystem)
    {
        appendObjects.Add(characterSystem);
        appendObjects.Add(configurationSystem);
        appendObjects.Add(moduleSystem);
        appendObjects.Add(storageSystem);
        appendObjects.Add(this);
        this.moduleSystem = moduleSystem;
        this.configurationSystem = configurationSystem;
    }
    
    readonly ModuleSystem moduleSystem;
    readonly ConfigurationSystem configurationSystem;
    readonly List<object> appendObjects = new();
    readonly Dictionary<string, ChatActivity> activities = new();
}
