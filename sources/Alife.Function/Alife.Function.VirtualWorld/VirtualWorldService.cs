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
        这个世界遵循与现实世界一致的物理定律、法律规范、经济逻辑。

        【物价参考】
        - 零食/饮料：5-15 元
        - 普通快餐/简餐：20-40 元
        - 餐厅正餐：60-150 元/人
        - 电子产品：1000-8000 元
        - 交通出行：公交/地铁 2-10 元，打车 15 元起

        【行为准则】
        1. 尊重他人私有财产，禁止无故索要。
        2. 社交互动需符合基本的礼仪和逻辑。
        3. 经济交易需公平合理，不支持无理由的大额赠予。

        【社会福利】
        如果这个世界有 银行/福利机构 等公共设施，可以每天申请 20 元的经济补贴。
        """;
}

[Module("世界背景",
    "定义整个运行环境的基础世界观、物理定律与全局公告。此配置通常作为所有角色的通用背景。",
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
    [Description("与指定的角色对话。（注意不要联系错人，对管理员直接对话即可）")]
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
                interactor.Poke($"[来自 {Character.Name} 的消息]: {context.FullContent.Trim()}\n(提示: 回复对方需要用<call>标签；但提防陌生人和骗子；可以对此信息忽略)");
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
                interactor.Poke($"[收到来自 {Character.Name} 的物品]: {context.FullContent.Trim()}\n(注意辨别真伪，建议特殊物品走公共设施中转，不要随意接收)");
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

    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new(this);
        functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);
        functionService.AddPlainAreas(nameof(Call), nameof(Give));

        List<Character> allCharacters = characterSystem.GetAllCharacters();
        string characterList = allCharacters.Any()
            ? string.Join("\n", allCharacters.Select(c =>
                $"- {c.Name}{(string.IsNullOrWhiteSpace(c.Description) ? "" : $"：{c.Description}")}{(c.Name.Equals(Configuration.AdminName, StringComparison.OrdinalIgnoreCase) ? " [管理员]" : "")}"))
            : "（当前无其他预设角色）";
        interactor.Prompt($"""
                           此服务将为你创造一个虚拟世界，你将在此生活，并有机会与这个世界的其他成员一起互动。

                           ## 管理员
                           世界的管理员为：{Configuration.AdminName}。管理员拥有最高权限，与管理员互动不需要使用标签，直接用普通文本对话即可。

                           ## 世界观
                           {Configuration.Announcement}

                           ## 联系人
                           你是生活在这个世界中的一位公民。除了你，这里还有其他活跃的个体。你可以通过 <call> 与他们沟通，或通过 <give> 交换物资。目前可联系的其他角色有：
                           {characterList}

                           ## 生存法则
                           这个世界并不是什么乌托邦，因此你需要以对待现实世界的方式对待它：
                           1. 社交边界：与陌生人交流请保持适度礼貌，根据互动逐步摸清人物画像再选择性建立关系。
                           2. 经济常识：遵循物价常识，大额交易应先沟通确认，小心骗子 and 假币，优先用银行等公共设施交易。
                           """);

        return Task.CompletedTask;
    }
}