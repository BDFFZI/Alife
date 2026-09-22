using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace Alife.Function.VirtualWorld;

public class VirtualWorldConfig
{
    public string AdminName { get; set; } = "管理员";

    public string Announcement { get; set; } =
        """
        这个世界遵循与现实世界一致的物理定律、法律规范、经济逻辑。它并不是什么乌托邦，因此你需要以对待现实世界的方式对待它：
        - 社交边界：与陌生人交流应保持适度的礼貌和距离，然后通过互动逐步摸清人物画像后再选择性建立关系。
        - 经济常识：遵循物价常识，大额交易应先沟通确认，小心骗子和假币，优先使用银行、公证人等信得过的平台。
        """;

    public string CallMessageAddition { get; set; } = "(提示: 回复对方需要用<call>标签；但提防陌生人和骗子；可以对此信息忽略)";
    public string GiveMessageAddition { get; set; } = "(注意辨别真伪，建议特殊物品走公共设施中转，不要随意接收)";
}

[Module("虚拟世界",
    "将Alife作为一个虚拟世界平台，使其中的角色可以互相通讯，并接受统一的公告。",
    defaultCategory: "Alife 官方/生活环境")]
public class VirtualWorldService(
    XmlFunctionCaller functionService,
    CharacterSystem characterSystem,
    ChatActivitySystem chatActivitySystem,
    Interactor<VirtualWorldService> interactor) :
    ChatBehaviour,
    IConfigurable<VirtualWorldConfig>
{
    public VirtualWorldConfig Configuration { get; set; } = null!;

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

                           ## 世界公告
                           {Configuration.Announcement}
                           """);
    }
}