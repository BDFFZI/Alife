using System.Collections.Generic;
using System.Linq;
using System.Text;
using BDFFZI.MaoMao.TableGame;

namespace BDFFZI.MaoMao.Xiangqi;

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
        Board = Board.After(move);
        LastMove = move;
        plies++;
        Turn = Opposite(mover);
        AddLog($"{Seats[index].Name}（{SideName(mover)}）{pieceName} {XiangqiBoard.Format(move)}{(Board.IsInCheck(Turn) ? "，将军" : "")}");
        if (Board.LegalMoves(Turn).Count == 0)
        {
            Winner = mover;
            AddLog($"{SideName(Turn)}无棋可走，{SideName(mover)}获胜");
        }
        else if (plies >= MaxPlies)
        {
            IsDraw = true;
            AddLog($"走满 {MaxPlies} 步未分胜负，和棋");
        }

        Commit();
        return null;
    }

    protected override TableSeat? ActiveSeat => IsOver ? null : Seats[SeatIndexOf(Turn)];
    protected override string Rules => """
        【中国象棋规则】
        这是一局中国象棋，红方先手。轮到你时你会收到当前棋盘和全部合法走法。
        - 棋盘：列为 a 到 i，行为 0 到 9。红方在下方（第 0 行，帅在 e0），黑方在上方（第 9 行，将在 e9）。
        - 棋子：红方为 帅 仕 相 马 车 炮 兵，黑方为 将 士 象 马 车 炮 卒。
        - 走法用起点加终点表示，例如 h2e2 表示把 h2 的炮移到 e2。
        - 每回合只能走一步，且必须从合法走法列表中选择。走棋后自己的将帅不能被对方将军，两个将帅不能在同一直线上直接照面。
        - 轮到你无棋可走（被将死或无子可动）即判负。走满 200 步未分胜负则和棋。
        回复时可以正常说话，但落子必须使用 <XiangqiMove>走法</XiangqiMove> 标签，例如 <XiangqiMove>h2e2</XiangqiMove>。
        """;

    protected override void OnStart()
    {
        Board = XiangqiBoard.CreateStart();
        Turn = XiangqiSide.Red;
        LastMove = null;
        Winner = null;
        IsDraw = false;
        plies = 0;
        log.Clear();
    }
    protected override string DescribeTurn(TableSeat seat)
    {
        StringBuilder builder = new();
        builder.AppendLine("【棋局状态】");
        builder.AppendLine($"你的座位：{seat.Name}，执{SideName(Turn)}");
        builder.AppendLine("棋盘（红方在下方第 0 行，黑方在上方第 9 行）：");
        builder.AppendLine(Board.Render());
        if (Board.IsInCheck(Turn))
            builder.AppendLine("你的将帅正被将军，本步必须解将。");
        IReadOnlyList<XiangqiMove> moves = LegalMoves;
        builder.AppendLine($"合法走法（{moves.Count} 种）：{string.Join("，", moves.Select(XiangqiBoard.Format))}");
        builder.AppendLine("最近动态：");
        foreach (string line in log.TakeLast(6))
            builder.AppendLine(line);

        builder.AppendLine();
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
