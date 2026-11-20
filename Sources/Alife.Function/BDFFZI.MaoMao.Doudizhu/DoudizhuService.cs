using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace BDFFZI.MaoMao.Doudizhu;

[Module("斗地主",
    "让角色可以与用户或其他角色同桌斗地主，轮到自己时通过工具叫地主、出牌或不出。",
    defaultCategory: "真央的小工具")]
public class DoudizhuService(XmlFunctionCaller functionCaller) : ChatBehaviour
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
