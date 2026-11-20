using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.TableGame;

namespace Alife.Function.Doudizhu;

[Module("斗地主",
    "让角色可以与用户或其他角色同桌斗地主，轮到自己时通过工具叫地主、出牌或不出。",
    defaultCategory: "Alife 官方/创意娱乐")]
public class DoudizhuService(XmlFunctionCaller functionCaller, ChatActivitySystem chatActivitySystem) : ChatBehaviour
{
    [XmlFunction(FunctionMode.OneShot, "DoudizhuCall")]
    public void Call()
    {
        Report(Seated(index => DoudizhuTable.Instance.Call(index)));
    }
    [XmlFunction(FunctionMode.OneShot, "DoudizhuPass")]
    public void Pass()
    {
        Report(Seated(index => DoudizhuTable.Instance.Pass(index)));
    }
    [XmlFunction(FunctionMode.Content, "DoudizhuPlay")]
    public void Play(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        List<int>? cards = DoudizhuCards.ParseCards(context.FullContent);
        if (cards == null)
        {
            Report("无法识别牌面，请只使用 3 4 5 6 7 8 9 10 J Q K A 2 小王 大王，并用空格分隔");
            return;
        }

        Report(Seated(index => DoudizhuTable.Instance.Play(index, cards)));
    }

    [XmlFunction(FunctionMode.Content, "DoudizhuStart")]
    public void StartGame(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;
        if (DoudizhuTable.Instance.IsPlaying)
        {
            Report("已经有一局斗地主正在进行。");
            return;
        }

        string[] names = context.FullContent.Split(['，', ',', '、', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (names.Length != 2)
        {
            Report("请指定另外两位玩家，用逗号分隔，例如 <DoudizhuStart>我，角色名</DoudizhuStart>。");
            return;
        }

        List<TableSeat> seats = [new TableSeat { Name = Character.Name, Character = Character, ChatBot = ChatBot }];
        HashSet<string> usedNames = [Character.Name];
        foreach (string name in names)
        {
            if (name is "我" or "主人" or "人类")
            {
                if (usedNames.Add("我") == false)
                {
                    Report($"玩家“{name}”重复。");
                    return;
                }
                seats.Add(new TableSeat { Name = "我" });
                continue;
            }

            if (usedNames.Add(name) == false)
            {
                Report($"玩家“{name}”重复。");
                return;
            }

            ChatActivity? activity = chatActivitySystem.GetAllChatActivities()
                .FirstOrDefault(activity => activity.Character.Name == name && activity.Container.Instances.OfType<DoudizhuService>().Any());
            if (activity == null)
            {
                Report($"找不到已启用斗地主功能的角色“{name}”。");
                return;
            }
            seats.Add(new TableSeat { Name = activity.Character.Name, Character = activity.Character, ChatBot = activity.ChatBot });
        }

        DoudizhuTable.Instance.Start(seats);
    }

    [XmlFunction(FunctionMode.Content, "DoudizhuStop")]
    public void StopGame(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;
        if (DoudizhuTable.Instance.IndexOfCharacter(Character) < 0)
        {
            Report("你当前不在牌局中。");
            return;
        }
        DoudizhuTable.Instance.Stop();
    }

    [XmlFunction(FunctionMode.Content, "DoudizhuResume")]
    public void ResumeGame(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;
        if (DoudizhuTable.Instance.IndexOfCharacter(Character) < 0)
        {
            Report("你当前不在牌局中。");
            return;
        }
        if (DoudizhuTable.Instance.IsPaused == false)
        {
            Report("牌局没有暂停。");
            return;
        }
        DoudizhuTable.Instance.Resume();
    }

    protected override Task OnAwake()
    {
        functionCaller.RegisterHandlerWithoutDocument(new XmlHandler(this), cancellationToken: DestroyCancellationToken);
        return Task.CompletedTask;
    }

    string? Seated(Func<int, string?> action)
    {
        int index = DoudizhuTable.Instance.IndexOfCharacter(Character);
        return index < 0 ? "你当前不在牌局中" : action(index);
    }
    void Report(string? error)
    {
        if (error != null)
            ChatBot.Poke($"[斗地主] {error}");
    }
}
