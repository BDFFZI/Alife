using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.TableGame;

namespace Alife.Function.Xiangqi;

[Module("中国象棋",
    "让角色可以与用户或其他角色对弈中国象棋，轮到自己时通过工具落子。",
    defaultCategory: "Alife 官方/创意娱乐")]
public class XiangqiService(XmlFunctionCaller functionCaller, ChatActivitySystem chatActivitySystem) : ChatBehaviour
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

        int index = XiangqiTable.Instance.IndexOfCharacter(Character);
        if (index < 0)
        {
            Report("你当前不在棋局中");
            return;
        }

        string? error = XiangqiTable.Instance.Move(index, move.Value);
        if (error == "这步棋不合法")
        {
            IReadOnlyList<XiangqiMove> legal = XiangqiTable.Instance.LegalMoves;
            Report($"这步棋不合法。当前合法走法共 {legal.Count} 种：{string.Join("，", legal.Select(XiangqiBoard.Format))}，请从中选择。");
            return;
        }
        Report(error);
    }

    [XmlFunction(FunctionMode.OneShot, "XiangqiSurrender")]
    public void Surrender()
    {
        int index = XiangqiTable.Instance.IndexOfCharacter(Character);
        if (index < 0)
        {
            Report("你当前不在棋局中");
            return;
        }
        Report(XiangqiTable.Instance.Surrender(index));
    }

    [XmlFunction(FunctionMode.Content, "XiangqiStart")]
    public void StartGame(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;
        if (XiangqiTable.Instance.IsPlaying)
        {
            Report("已经有一局象棋正在进行。");
            return;
        }

        string opponentName = context.FullContent.Trim();
        if (opponentName.Length == 0)
        {
            Report("请指定对手，例如 <XiangqiStart>我</XiangqiStart> 或 <XiangqiStart>角色名</XiangqiStart>。");
            return;
        }
        if (opponentName == Character.Name)
        {
            Report("不能和自己对弈。");
            return;
        }

        TableSeat self = new() { Name = Character.Name, Character = Character, ChatBot = ChatBot };
        TableSeat opponent;
        if (opponentName is "我" or "主人" or "人类")
        {
            opponent = new TableSeat { Name = "我" };
        }
        else
        {
            ChatActivity? activity = chatActivitySystem.GetAllChatActivities()
                .FirstOrDefault(activity => activity.Character.Name == opponentName && activity.Container.Instances.OfType<XiangqiService>().Any());
            if (activity == null)
            {
                Report($"找不到已启用象棋功能的角色“{opponentName}”。");
                return;
            }
            opponent = new TableSeat { Name = activity.Character.Name, Character = activity.Character, ChatBot = activity.ChatBot };
        }

        XiangqiTable.Instance.Start([self, opponent]);
    }

    [XmlFunction(FunctionMode.Content, "XiangqiStop")]
    public void StopGame(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;
        if (XiangqiTable.Instance.IndexOfCharacter(Character) < 0)
        {
            Report("你当前不在棋局中。");
            return;
        }
        XiangqiTable.Instance.Stop();
    }

    [XmlFunction(FunctionMode.Content, "XiangqiResume")]
    public void ResumeGame(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;
        if (XiangqiTable.Instance.IndexOfCharacter(Character) < 0)
        {
            Report("你当前不在棋局中。");
            return;
        }
        if (XiangqiTable.Instance.IsPaused == false)
        {
            Report("对局没有暂停。");
            return;
        }
        XiangqiTable.Instance.Resume();
    }

    protected override Task OnAwake()
    {
        functionCaller.RegisterHandlerWithoutDocument(new XmlHandler(this), cancellationToken: DestroyCancellationToken);
        return Task.CompletedTask;
    }

    void Report(string? error)
    {
        if (error != null)
            ChatBot.Poke($"[中国象棋] {error}");
    }
}
