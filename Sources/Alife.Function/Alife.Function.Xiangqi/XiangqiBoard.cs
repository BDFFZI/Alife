using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Alife.Function.Xiangqi;

public enum XiangqiSide
{
    Red,
    Black,
}

public enum XiangqiKind
{
    General,
    Advisor,
    Elephant,
    Horse,
    Chariot,
    Cannon,
    Soldier,
}

public readonly record struct XiangqiPiece(XiangqiSide Side, XiangqiKind Kind);

public readonly record struct XiangqiMove(int FromFile, int FromRank, int ToFile, int ToRank);

public class XiangqiBoard
{
    public const int FileCount = 9;
    public const int RankCount = 10;

    public static XiangqiBoard CreateStart()
    {
        XiangqiPiece?[,] squares = new XiangqiPiece?[FileCount, RankCount];
        for (int file = 0; file < FileCount; file++)
        {
            squares[file, 0] = new XiangqiPiece(XiangqiSide.Red, backRank[file]);
            squares[file, RankCount - 1] = new XiangqiPiece(XiangqiSide.Black, backRank[file]);
        }
        squares[1, 2] = new XiangqiPiece(XiangqiSide.Red, XiangqiKind.Cannon);
        squares[7, 2] = new XiangqiPiece(XiangqiSide.Red, XiangqiKind.Cannon);
        squares[1, 7] = new XiangqiPiece(XiangqiSide.Black, XiangqiKind.Cannon);
        squares[7, 7] = new XiangqiPiece(XiangqiSide.Black, XiangqiKind.Cannon);
        for (int file = 0; file < FileCount; file += 2)
        {
            squares[file, 3] = new XiangqiPiece(XiangqiSide.Red, XiangqiKind.Soldier);
            squares[file, 6] = new XiangqiPiece(XiangqiSide.Black, XiangqiKind.Soldier);
        }

        return new XiangqiBoard(squares);
    }
    public static string Name(XiangqiPiece piece)
    {
        return piece.Side == XiangqiSide.Red ? redNames[(int)piece.Kind] : blackNames[(int)piece.Kind];
    }
    public static string Format(XiangqiMove move)
    {
        return $"{(char)('a' + move.FromFile)}{move.FromRank}{(char)('a' + move.ToFile)}{move.ToRank}";
    }
    public static XiangqiMove? Parse(string text)
    {
        string value = new string(text.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();
        if (value.Length != 4)
            return null;

        XiangqiMove move = new(value[0] - 'a', value[1] - '0', value[2] - 'a', value[3] - '0');
        return IsOnBoard(move.FromFile, move.FromRank) && IsOnBoard(move.ToFile, move.ToRank) ? move : null;
    }

    public XiangqiPiece? PieceAt(int file, int rank)
    {
        return squares[file, rank];
    }
    public XiangqiBoard After(XiangqiMove move)
    {
        XiangqiPiece?[,] next = (XiangqiPiece?[,])squares.Clone();
        next[move.ToFile, move.ToRank] = next[move.FromFile, move.FromRank];
        next[move.FromFile, move.FromRank] = null;
        return new XiangqiBoard(next);
    }
    public List<XiangqiMove> LegalMoves(XiangqiSide side)
    {
        List<XiangqiMove> moves = [];
        for (int fromFile = 0; fromFile < FileCount; fromFile++)
        {
            for (int fromRank = 0; fromRank < RankCount; fromRank++)
            {
                if (squares[fromFile, fromRank] is not { } piece || piece.Side != side)
                    continue;

                for (int toFile = 0; toFile < FileCount; toFile++)
                {
                    for (int toRank = 0; toRank < RankCount; toRank++)
                    {
                        XiangqiMove move = new(fromFile, fromRank, toFile, toRank);
                        if (CanMove(piece, move) && After(move).IsInCheck(side) == false)
                            moves.Add(move);
                    }
                }
            }
        }

        return moves;
    }
    public bool IsInCheck(XiangqiSide side)
    {
        if (GeneralsFace())
            return true;

        (int generalFile, int generalRank) = FindGeneral(side);
        for (int file = 0; file < FileCount; file++)
        {
            for (int rank = 0; rank < RankCount; rank++)
            {
                if (squares[file, rank] is { } piece && piece.Side != side && CanMove(piece, new XiangqiMove(file, rank, generalFile, generalRank)))
                    return true;
            }
        }

        return false;
    }
    public string RenderCompact()
    {
        StringBuilder builder = new();
        builder.Append("   ");
        for (int file = 0; file < FileCount; file++)
            builder.Append((char)('a' + file)).Append("  ");
        builder.AppendLine();
        for (int rank = RankCount - 1; rank >= 0; rank--)
        {
            builder.Append(' ').Append(rank).Append(' ');
            for (int file = 0; file < FileCount; file++)
                builder.Append(squares[file, rank] is { } piece ? Name(piece) : '・').Append(' ');
            builder.Append('\n');
        }
        builder.Append("   ");
        for (int file = 0; file < FileCount; file++)
            builder.Append((char)('a' + file)).Append("  ");

        return builder.ToString();
    }

    public string DescribePieces(XiangqiSide side)
    {
        StringBuilder builder = new();
        for (int rank = RankCount - 1; rank >= 0; rank--)
        {
            for (int file = 0; file < FileCount; file++)
            {
                if (squares[file, rank] is not { } piece || piece.Side != side)
                    continue;

                if (builder.Length > 0)
                    builder.Append('，');
                builder.Append(Name(piece))
                    .Append(' ')
                    .Append((char)('a' + file))
                    .Append(rank);
            }
        }

        return builder.ToString();
    }

    XiangqiBoard(XiangqiPiece?[,] squares)
    {
        this.squares = squares;
    }
    static bool InPalace(XiangqiSide side, int file, int rank)
    {
        int bottomRank = side == XiangqiSide.Red ? 0 : 7;
        return file is >= 3 and <= 5 && rank >= bottomRank && rank <= bottomRank + 2;
    }
    static bool IsOwnHalf(XiangqiSide side, int rank)
    {
        return side == XiangqiSide.Red ? rank <= 4 : rank >= 5;
    }
    static bool IsOnBoard(int file, int rank)
    {
        return file is >= 0 and < FileCount && rank is >= 0 and < RankCount;
    }
    static bool SoldierCanMove(XiangqiSide side, int fromRank, int deltaFile, int deltaRank)
    {
        if (deltaFile == 0)
            return deltaRank == (side == XiangqiSide.Red ? 1 : -1);

        return deltaRank == 0 && Math.Abs(deltaFile) == 1 && IsOwnHalf(side, fromRank) == false;
    }
    bool CanMove(XiangqiPiece piece, XiangqiMove move)
    {
        if (squares[move.ToFile, move.ToRank] is { } target && target.Side == piece.Side)
            return false;

        int deltaFile = move.ToFile - move.FromFile;
        int deltaRank = move.ToRank - move.FromRank;
        int absFile = Math.Abs(deltaFile);
        int absRank = Math.Abs(deltaRank);
        return piece.Kind switch
        {
            XiangqiKind.General => InPalace(piece.Side, move.ToFile, move.ToRank) && absFile + absRank == 1,
            XiangqiKind.Advisor => InPalace(piece.Side, move.ToFile, move.ToRank) && absFile == 1 && absRank == 1,
            XiangqiKind.Elephant => absFile == 2 && absRank == 2 && IsOwnHalf(piece.Side, move.ToRank) && squares[move.FromFile + deltaFile / 2, move.FromRank + deltaRank / 2] == null,
            XiangqiKind.Horse => HorseCanMove(move, deltaFile, deltaRank),
            XiangqiKind.Chariot => (deltaFile == 0 || deltaRank == 0) && CountBetween(move) == 0,
            XiangqiKind.Cannon => (deltaFile == 0 || deltaRank == 0) && CountBetween(move) == (squares[move.ToFile, move.ToRank] == null ? 0 : 1),
            _ => SoldierCanMove(piece.Side, move.FromRank, deltaFile, deltaRank),
        };
    }
    bool HorseCanMove(XiangqiMove move, int deltaFile, int deltaRank)
    {
        if (Math.Abs(deltaFile) == 1 && Math.Abs(deltaRank) == 2)
            return squares[move.FromFile, move.FromRank + deltaRank / 2] == null;

        return Math.Abs(deltaFile) == 2 && Math.Abs(deltaRank) == 1 && squares[move.FromFile + deltaFile / 2, move.FromRank] == null;
    }
    int CountBetween(XiangqiMove move)
    {
        int stepFile = Math.Sign(move.ToFile - move.FromFile);
        int stepRank = Math.Sign(move.ToRank - move.FromRank);
        int count = 0;
        for (int file = move.FromFile + stepFile, rank = move.FromRank + stepRank; file != move.ToFile || rank != move.ToRank; file += stepFile, rank += stepRank)
        {
            if (squares[file, rank] != null)
                count++;
        }

        return count;
    }
    bool GeneralsFace()
    {
        (int redFile, int redRank) = FindGeneral(XiangqiSide.Red);
        (int blackFile, int blackRank) = FindGeneral(XiangqiSide.Black);
        return redFile == blackFile && CountBetween(new XiangqiMove(redFile, redRank, blackFile, blackRank)) == 0;
    }
    (int File, int Rank) FindGeneral(XiangqiSide side)
    {
        XiangqiPiece general = new(side, XiangqiKind.General);
        return Enumerable.Range(0, FileCount)
            .SelectMany(file => Enumerable.Range(0, RankCount).Select(rank => (File: file, Rank: rank)))
            .First(square => squares[square.File, square.Rank] == general);
    }

    static readonly XiangqiKind[] backRank = [XiangqiKind.Chariot, XiangqiKind.Horse, XiangqiKind.Elephant, XiangqiKind.Advisor, XiangqiKind.General, XiangqiKind.Advisor, XiangqiKind.Elephant, XiangqiKind.Horse, XiangqiKind.Chariot];
    static readonly string[] redNames = ["帅", "仕", "相", "马", "车", "炮", "兵"];
    static readonly string[] blackNames = ["将", "士", "象", "马", "车", "炮", "卒"];
    readonly XiangqiPiece?[,] squares;
}
