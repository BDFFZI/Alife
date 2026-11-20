using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Alife.Function.TableGame;

namespace Alife.Function.Xiangqi;

public class XiangqiTable : GameTable
{
    const int MaxPlies = 200;

    public static XiangqiTable Instance { get; } = new();

    public XiangqiBoard Board { get; private set; } = XiangqiBoard.CreateStart();
    public XiangqiSide Turn { get; private set; }
    public XiangqiMove? LastMove { get; private set; }
    public XiangqiSide? Winner { get; private set; }
    public bool IsDraw { get; private set; }
    public bool IsOver => Winner != null || IsDraw;
    public IReadOnlyList<XiangqiMove> LegalMoves => IsOver ? [] : Board.LegalMoves(Turn);
    public IReadOnlyList<string> Log => log;

    public static string SideName(XiangqiSide side)
    {
        return side == XiangqiSide.Red ? "红方" : "黑方";
    }
    public string? Move(int index, XiangqiMove move)
    {
        if (IsOver)
            return "牌局已经结束";
        if (index != SeatIndexOf(Turn))
            return "还没轮到你";
        if (LegalMoves.Contains(move) == false)
            return "这步棋不合法";

        XiangqiSide mover = Turn;
        string pieceName = XiangqiBoard.Name(Board.PieceAt(move.FromFile, move.FromRank)!.Value);
        XiangqiPiece? captured = Board.PieceAt(move.ToFile, move.ToRank);
        Board = Board.After(move);
        LastMove = move;
        plies++;
        Turn = Opposite(mover);
        StringBuilder logLine = new();
        logLine.Append($"{Seats[index].Name}（{SideName(mover)}）{pieceName} {XiangqiBoard.Format(move)}");
        if (captured is { } target)
            logLine.Append($"，吃{SideName(target.Side)}{XiangqiBoard.Name(target)}");
        if (Board.IsInCheck(Turn))
            logLine.Append("，将军");
        AddLog(logLine.ToString());
        if (Board.LegalMoves(Turn).Count == 0)
        {
            Winner = mover;
            AddLog($"{SideName(Turn)}无棋可走，{SideName(mover)}获胜");
            NotifyAll($"{SideName(Turn)}无棋可走，{SideName(mover)}获胜，对局结束。");
        }
        else if (plies >= MaxPlies)
        {
            IsDraw = true;
            AddLog($"走满 {MaxPlies} 步未分胜负，和棋");
            NotifyAll($"走满 {MaxPlies} 步未分胜负，和棋，对局结束。");
        }

        Commit();
        return null;
    }

    protected override TableSeat? ActiveSeat => IsOver ? null : Seats[SeatIndexOf(Turn)];
    protected override string Rules => """
        【中国象棋规则】
        这是一局中国象棋，开局随机决定红方或黑方先手。轮到你时你会收到当前棋盘和全部合法走法。
        - 棋盘：列为 a 到 i，行为 0 到 9。红方在下方（第 0 行，帅在 e0），黑方在上方（第 9 行，将在 e9）。
        - 棋子：红方为 帅 仕 相 马 车 炮 兵，黑方为 将 士 象 马 车 炮 卒。
        - 炮：不吃子时像车一样沿直线移动；吃子时，起点和目标之间必须恰好隔一个棋子。不能吃路径上的第一个棋子，但可以隔着它吃后面的棋子。
        - 走法用起点加终点表示，例如 h2e2 表示把 h2 的炮移到 e2。
        - 每回合只能走一步，且必须从合法走法列表中选择。走棋后自己的将帅不能被对方将军，两个将帅不能在同一直线上直接照面。
        - 轮到你无棋可走（被将死或无子可动）即判负。走满 200 步未分胜负则和棋。
        - 棋盘文本上下两侧是列坐标 a-i，左侧是行坐标 0-9，・ 表示空位。
        回复时可以正常说话，但落子必须使用 <XiangqiMove>走法</XiangqiMove> 标签，例如 <XiangqiMove>h2e2</XiangqiMove>。
        """;
    protected override string StartNotice => $"象棋开局：{Seats[0].Name}（红方）对 {Seats[1].Name}（黑方）。当前轮到{Seats[SeatIndexOf(Turn)].Name}（{SideName(Turn)}）行动；请等待系统发出轮到你的棋盘提示，不要启动陪玩或提前落子。";
    protected override string StopNotice => "象棋对局结束。";

    protected override void OnStart()
    {
        Board = XiangqiBoard.CreateStart();
        Turn = Random.Shared.Next(2) == 0 ? XiangqiSide.Red : XiangqiSide.Black;
        LastMove = null;
        Winner = null;
        IsDraw = false;
        plies = 0;
        log.Clear();
    }
    protected override string DescribeTurn(TableSeat seat)
    {
        StringBuilder builder = new();
        builder.AppendLine($"轮到你了：{seat.Name}，执{SideName(Turn)}。棋盘（红方在下方，黑方在上方）：");
        builder.AppendLine("```text");
        builder.AppendLine(Board.RenderCompact().TrimEnd());
        builder.AppendLine("```");
        if (log.Count > 0)
            builder.AppendLine($"上一步：{log[^1]}");
        XiangqiSide opponent = Turn == XiangqiSide.Red ? XiangqiSide.Black : XiangqiSide.Red;
        builder.AppendLine($"你执{SideName(Turn)}，位于{(Turn == XiangqiSide.Black ? "上方" : "下方")}；对方执{SideName(opponent)}。");
        builder.AppendLine($"你方棋子：{Board.DescribePieces(Turn)}");
        builder.AppendLine($"对方棋子：{Board.DescribePieces(opponent)}");
        if (Board.IsInCheck(Turn))
            builder.AppendLine("你的将帅正被将军，本步必须解将。");
        builder.AppendLine("落子前先判断将帅安全、对方主要攻击线和己方关键子，并明确本步目的（防守、吃子、将军、出动大子或控制要道）；不要只因为某步合法就随手走。");
        builder.AppendLine("分析炮线时看完整条线：炮不能吃路径上的第一个棋子，但可以隔着它吃后面的目标。");
        IReadOnlyList<XiangqiMove> legalMoves = LegalMoves;
        builder.AppendLine($"当前合法走法共 {legalMoves.Count} 种。");
        builder.Append("请回复 <XiangqiMove>走法</XiangqiMove> 落子，例如 <XiangqiMove>h2e2</XiangqiMove>。");
        return builder.ToString();
    }

    static int SeatIndexOf(XiangqiSide side)
    {
        return side == XiangqiSide.Red ? 0 : 1;
    }
    static XiangqiSide Opposite(XiangqiSide side)
    {
        return side == XiangqiSide.Red ? XiangqiSide.Black : XiangqiSide.Red;
    }
    void AddLog(string line)
    {
        log.Add(line);
    }

    int plies;
    readonly List<string> log = [];
}
