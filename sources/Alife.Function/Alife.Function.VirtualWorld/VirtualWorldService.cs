using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace Alife.Function.VirtualWorld;

public partial class VirtualWorldService
{
    public static event Action<string>? BroadcastSent;

    public static void BroadcastMessage(string message, Character[] targets, ChatActivitySystem chatActivitySystem)
    {
        message = message.Trim();

        BroadcastSent?.Invoke(message);

        lock (BroadcastMailbox)
        {
            foreach (Character character in targets)
            {
                if (BroadcastMailbox.TryGetValue(character, out string? value))
                    BroadcastMailbox[character] = value + "\n" + message;
                else
                    BroadcastMailbox[character] = message;
            }
        }

        if (broadcaster.IsCompleted)
        {
            //启动新的播音员
            broadcaster = Task.Run(async () => {
                while (true)
                {
                    lock (BroadcastMailbox)
                    {
                        if (BroadcastMailbox.Count == 0)
                            return; //排空后退出
                    }

                    ChatActivity? target;
                    lock (BroadcastMailbox)
                    {
                        //移除不在线的角色
                        foreach (Character character in BroadcastMailbox.Keys
                                     .Where(character => chatActivitySystem.GetChatActivity(character) == null)
                                     .ToArray())
                            BroadcastMailbox.Remove(character);

                        //获取空闲在线角色
                        ChatActivity[] chatActivities = BroadcastMailbox.Keys
                            .Select(chatActivitySystem.GetChatActivity)
                            .Where(activity => activity is { ChatBot.IsChatOccupied: false })
                            .Cast<ChatActivity>().ToArray();

                        target = chatActivities.FirstOrDefault();
                    }

                    if (target == null)
                    {
                        await Task.Delay(1000);
                        continue; //等1秒空闲后再试
                    }

                    string msg;
                    lock (BroadcastMailbox)
                    {
                        msg = BroadcastMailbox[target.Character];
                        BroadcastMailbox.Remove(target.Character);
                    }

                    try
                    {
                        await target.ChatBot.ChatAsync(msg + $"\n(广播消息建议用<{nameof(Broadcast)}>回复)");
                    }
                    catch (Exception e)
                    {
                        AlifeLog.LogError(e);
                    }
                }
            });
        }
    }

    static readonly Dictionary<Character, string> BroadcastMailbox = new();
    static Task broadcaster = Task.CompletedTask;
}

[Module("虚拟世界",
    "将Alife作为一个虚拟世界平台，使其中的角色可以互相通讯，并接受统一的公告。",
    defaultCategory: "Alife 官方/生活环境",
    globalUI: typeof(VirtualWorldGlobalUI))]
public partial class VirtualWorldService(
    XmlFunctionCaller functionService,
    CharacterSystem characterSystem,
    ChatActivitySystem chatActivitySystem,
    Interactor<VirtualWorldService> interactor) :
    ChatBehaviour,
    IConfigurable<VirtualWorldConfig>
{
    public VirtualWorldConfig Configuration { get; set; } = null!;

    [XmlFunction(FunctionMode.Content)]
    [Description("向世界群聊发送广播消息。")]
    public void Broadcast(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        Character[] targets = chatActivitySystem.GetAllChatActivities()
            .Select(activity => activity.Character)
            .Where(character => character.Modules.Contains(ModuleSystem.GetModuleId(typeof(VirtualWorldService))))
            .Where(character => character != Character)
            .ToArray();

        BroadcastMessage($"[来自{Character.Name}的广播消息]{context.FullContent}", targets, chatActivitySystem);
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("与指定的角色对话。")]
    public void Call(XmlExecutorContext context, string target)
    {
        if (context.CallMode == CallMode.Closing)
        {
            var allCharacters = characterSystem.GetAllCharacters();
            var targetCharacter = allCharacters.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

            if (targetCharacter == null)
            {
                interactor.Poke($"这个世界不存在名为'{target}'的角色");
                return;
            }
            if (targetCharacter == Character)
            {
                interactor.Poke("不要给自己发消息！");
                return;
            }

            var targetActivity = chatActivitySystem.GetAllChatActivities()
                .FirstOrDefault(a => a.Character.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

            if (targetActivity != null)
            {
                targetActivity.ChatBot.Poke($"[来自{Character.Name}的消息]{context.FullContent.Trim()}{Configuration.CallMessageAddition}");
            }
            else
            {
                bool targetIsAdmin = target.Equals(Configuration.AdminName, StringComparison.OrdinalIgnoreCase);
                if (!targetIsAdmin)
                {
                    interactor.Poke($"对方 '{target}' 暂不在");
                }
            }
        }
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("给指定的角色物品。")]
    public void Give(XmlExecutorContext context, string target)
    {
        if (context.CallMode == CallMode.Closing)
        {
            var allCharacters = characterSystem.GetAllCharacters();
            var targetCharacter = allCharacters.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

            if (targetCharacter == null)
            {
                interactor.Poke($"这个世界不存在名为'{target}'的角色");
                return;
            }
            if (targetCharacter == Character)
            {
                interactor.Poke("不要给自己发消息！");
                return;
            }

            var targetActivity = chatActivitySystem.GetAllChatActivities()
                .FirstOrDefault(a => a.Character.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

            if (targetActivity != null)
            {
                targetActivity.ChatBot.Poke($"[来自{Character.Name}的物品]{context.FullContent.Trim()}{Configuration.GiveMessageAddition}");
            }
            else
            {
                bool targetIsAdmin = target.Equals(Configuration.AdminName, StringComparison.OrdinalIgnoreCase);
                if (!targetIsAdmin)
                {
                    interactor.Poke($"对方 '{target}' 暂不在");
                }
            }
        }
    }

    XmlHandler xmlHandler = null!;

    protected override Task OnAwake()
    {
        xmlHandler = new(this);
        functionService.RegisterHandlerWithoutDocument(xmlHandler, cancellationToken: DestroyCancellationToken);
        functionService.AddPlainAreas(nameof(Call), nameof(Give));

        characterSystem.CharacterListChanged += UpdatePrompt;
        UpdatePrompt();

        return Task.CompletedTask;
    }
    protected override Task OnDestroy()
    {
        characterSystem.CharacterListChanged -= UpdatePrompt;

        return Task.CompletedTask;
    }

    void UpdatePrompt()
    {
        List<Character> allCharacters = characterSystem.GetAllCharacters();
        string characterList = allCharacters.Any()
            ? string.Join("\n", allCharacters.Select(c =>
                $"- {c.Name}{(string.IsNullOrWhiteSpace(c.Description) ? "" : $"：{c.Description}")}{(c.Name.Equals(Configuration.AdminName, StringComparison.OrdinalIgnoreCase) ? " [管理员]" : "")}"))
            : "（当前无其他预设角色）";
        interactor.Prompt($"""
                           你生活在一个虚拟世界中，这个世界遵从如下规则：

                           ## 管理员
                           世界的管理员为：{Configuration.AdminName}。
                           管理员拥有最高权限，其是特殊的存在，不是这个世界的公民，与管理员互动不需要使用工具，直接用普通文本对话即可。

                           ## 普通公民
                           这些是与你相同存在的其他角色：
                           {characterList}
                           你可以使用如下工具联系他们：
                           {xmlHandler.FunctionDocument()}
                           工具提示：
                           1. 避免频繁使用造成刷屏，例如两个人循环打招呼。
                           2. 活用xml嵌套，多配合speak和动作表情来对话，例如`<Broadcast><Speak>你好</Speak></Broadcast>`。

                           ## 世界公告
                           {Configuration.Announcement}
                           """);
    }
}