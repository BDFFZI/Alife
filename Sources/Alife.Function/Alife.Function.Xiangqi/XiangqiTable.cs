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
        string moveText = logLine.ToString();
        AddLog(moveText);
        if (Board.LegalMoves(Turn).Count == 0)
        {
            Winner = mover;
            string result = $"{SideName(Turn)}无棋可走，{SideName(mover)}获胜";
            AddLog(result);
            NotifyAll(DescribeEnd(result, moveText));
        }
        else if (plies >= MaxPlies)
        {
            IsDraw = true;
            string result = $"走满 {MaxPlies} 步未分胜负，和棋";
            AddLog(result);
            NotifyAll(DescribeEnd(result, moveText));
        }

        Commit();
        return null;
    }
    public string? Surrender(int index)
    {
        if (IsPlaying == false)
            return "对局尚未开始";
        if (IsOver)
            return "对局已经结束";
        if (index < 0 || index >= Seats.Count)
            return "你当前不在棋局中";

        XiangqiSide loser = index == 0 ? XiangqiSide.Red : XiangqiSide.Black;
        XiangqiSide winner = Opposite(loser);
        Winner = winner;
        string lastMoveText = log.Count == 0 ? "尚未走子" : log[^1];
        string result = $"{Seats[index].Name}（{SideName(loser)}）投降，{SideName(winner)}获胜";
        AddLog(result);
        NotifyAll(DescribeEnd(result, lastMoveText));
        Commit();
        return null;
    }

    protected override TableSeat? ActiveSeat => IsOver ? null : Seats[SeatIndexOf(Turn)];
    protected override string Rules => """
        【中国象棋规则】
        开局随机决定红方或黑方先手。红方在下方（第 0 行，帅在 e0），黑方在上方（第 9 行，将在 e9）；列为 a-i，行为 0-9。
        棋子：红方为 帅 仕 相 马 车 炮 兵，黑方为 将 士 象 马 车 炮 卒。
        炮不吃子时像车一样沿直线移动；吃子时，起点和目标之间必须恰好隔一个棋子，且不能吃路径上的第一个棋子。
        走法用起点加终点表示，例如 h2e2。每回合只能走一步，且必须从合法走法中选择；走棋后自己的将帅不能被将军，两个将帅不能在同一直线上直接照面。
        每次落子先判断将帅安全、对方主要攻击线、己方关键子与本步目的（防守、吃子、将军、出动大子或控制要道），不要只因合法就随手走；分析炮线要看完整条线。
        被将死或无子可动判负；走满 200 步未分胜负则和棋。局面无法挽回时可投降，使用 <XiangqiSurrender/>。
        棋盘顶部是列坐标，左侧是行坐标，・ 表示空位。
        可以正常说话，但落子必须使用 <XiangqiMove>走法</XiangqiMove>，例如 <XiangqiMove>h2e2</XiangqiMove>。
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
        builder.AppendLine($"轮到你了：{seat.Name}，执{SideName(Turn)}。棋盘：");
        builder.AppendLine("```text");
        builder.AppendLine(Board.RenderCompact().TrimEnd());
        builder.AppendLine("```");
        if (log.Count > 0)
            builder.AppendLine($"上一步：{log[^1]}");
        builder.AppendLine($"你方棋子：{Board.DescribePieces(Turn)}");
        if (seat.IsBriefed == false)
            builder.AppendLine($"对方棋子：{Board.DescribePieces(Opposite(Turn))}");
        if (Board.IsInCheck(Turn))
            builder.AppendLine("你的将帅正被将军，本步必须解将。");
        builder.AppendLine("先检查将帅安全、炮线和关键子，再落子。");
        IReadOnlyList<XiangqiMove> legalMoves = LegalMoves;
        builder.AppendLine($"合法走法：{legalMoves.Count} 种。");
        builder.Append("回复 <XiangqiMove>起点终点</XiangqiMove>，如 <XiangqiMove>h2e2</XiangqiMove>。");
        return builder.ToString();
    }
    string DescribeEnd(string result, string lastMoveText)
    {
        StringBuilder builder = new();
        builder.AppendLine($"【对局结束】{result}。");
        builder.AppendLine("最终棋盘（红方在下方，黑方在上方）：");
        builder.AppendLine("```text");
        builder.AppendLine(Board.RenderCompact().TrimEnd());
        builder.AppendLine("```");
        builder.AppendLine($"最后一步：{lastMoveText}");
        builder.AppendLine($"红方：{Board.DescribePieces(XiangqiSide.Red)}");
        builder.AppendLine($"黑方：{Board.DescribePieces(XiangqiSide.Black)}");
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
