using System;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace BDFFZI.MaoMao.Xiangqi;

[Module("中国象棋",
    "让角色可以与用户或其他角色对弈中国象棋，轮到自己时通过工具落子。",
    defaultCategory: "真央的小工具")]
public class XiangqiService(XmlFunctionCaller functionCaller) : ChatBehaviour
{
    [XmlFunction(FunctionMode.Content, "XiangqiMove")]
    public void Move(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        XiangqiMove? move = XiangqiBoard.Parse(context.FullContent);
        if (move == null)
        {
            Report("无法识别走法，请使用起点加终点的格式，例如 h2e2");
            return;
        }

        Report(Seated(index => XiangqiTable.Instance.Move(index, move.Value)));
    }

    protected override Task OnAwake()
    {
        functionCaller.RegisterHandlerWithoutDocument(new XmlHandler(this), cancellationToken: DestroyCancellationToken);
        return Task.CompletedTask;
    }

    string? Seated(Func<int, string?> action)
    {
        int index = XiangqiTable.Instance.IndexOfCharacter(Character);
        return index < 0 ? "你当前不在棋局中" : action(index);
    }
    void Report(string? error)
    {
        if (error != null)
            ChatBot.Poke($"[中国象棋] {error}");
    }
}
