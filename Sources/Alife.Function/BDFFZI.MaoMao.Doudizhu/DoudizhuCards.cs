using System;
using System.Collections.Generic;
using System.Linq;

namespace BDFFZI.MaoMao.Doudizhu;

public enum DoudizhuKind
{
    Single,
    Pair,
    Trio,
    TrioWithSingle,
    TrioWithPair,
    Straight,
    PairStraight,
    Plane,
    PlaneWithSingles,
    PlaneWithPairs,
    FourWithTwo,
    Bomb,
    Rocket,
}

/// <summary>
/// 牌型。Value 为比较用的关键点数（连续牌型取最大点数），Length 为牌的张数。
/// </summary>
public record DoudizhuCombo(DoudizhuKind Kind, int Value, int Length);

public static class DoudizhuCards
{
    public const int Ace = 14;
    public const int SmallJoker = 16;
    public const int BigJoker = 17;

    static readonly string[] names = ["3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A", "2", "小王", "大王"];
    static readonly char[] separators = [' ', ',', '，', '\t', '\r', '\n'];

    public static List<int> CreateDeck()
    {
        List<int> deck = [];
        for (int value = 3; value <= BigJoker; value++)
        {
            int copies = value >= SmallJoker ? 1 : 4;
            for (int copy = 0; copy < copies; copy++)
                deck.Add(value);
        }

        return deck;
    }
    public static string Format(IEnumerable<int> cards)
    {
        return string.Join(" ", cards.OrderBy(card => card).Select(card => names[card - 3]));
    }
    public static string Name(int card)
    {
        return names[card - 3];
    }
    public static List<int>? ParseCards(string text)
    {
        List<int> cards = [];
        foreach (string token in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            int index = Array.IndexOf(names, token);
            if (index < 0)
                return null;
            cards.Add(index + 3);
        }

        return cards.Count == 0 ? null : cards;
    }

    public static DoudizhuCombo? Classify(IReadOnlyList<int> cards)
    {
        int[] counts = new int[BigJoker + 1];
        foreach (int card in cards)
            counts[card]++;

        int total = cards.Count;
        List<int> ones = ValuesWithCount(counts, 1);
        List<int> twos = ValuesWithCount(counts, 2);
        List<int> threes = ValuesWithCount(counts, 3);
        List<int> fours = ValuesWithCount(counts, 4);

        if (total == 1)
            return new DoudizhuCombo(DoudizhuKind.Single, cards[0], 1);
        if (total == 2 && counts[SmallJoker] == 1 && counts[BigJoker] == 1)
            return new DoudizhuCombo(DoudizhuKind.Rocket, BigJoker, 2);
        if (total == 2 && twos.Count == 1)
            return new DoudizhuCombo(DoudizhuKind.Pair, twos[0], 2);
        if (total == 3 && threes.Count == 1)
            return new DoudizhuCombo(DoudizhuKind.Trio, threes[0], 3);
        if (total == 4 && fours.Count == 1)
            return new DoudizhuCombo(DoudizhuKind.Bomb, fours[0], 4);
        if (total == 4 && threes.Count == 1 && ones.Count == 1)
            return new DoudizhuCombo(DoudizhuKind.TrioWithSingle, threes[0], 4);
        if (total == 5 && threes.Count == 1 && twos.Count == 1)
            return new DoudizhuCombo(DoudizhuKind.TrioWithPair, threes[0], 5);
        if (total == 6 && fours.Count == 1 && (ones.Count == 2 || twos.Count == 1))
            return new DoudizhuCombo(DoudizhuKind.FourWithTwo, fours[0], 6);
        if (total >= 5 && ones.Count == total && IsRun(ones) && ones[^1] <= Ace)
            return new DoudizhuCombo(DoudizhuKind.Straight, ones[^1], total);
        if (total >= 6 && twos.Count * 2 == total && IsRun(twos) && twos[^1] <= Ace)
            return new DoudizhuCombo(DoudizhuKind.PairStraight, twos[^1], total);

        return ClassifyPlane(counts, total);
    }
    public static bool Beats(DoudizhuCombo current, DoudizhuCombo previous)
    {
        if (current.Kind == DoudizhuKind.Rocket)
            return true;
        if (previous.Kind == DoudizhuKind.Rocket)
            return false;
        if (current.Kind == DoudizhuKind.Bomb)
            return previous.Kind != DoudizhuKind.Bomb || current.Value > previous.Value;
        if (previous.Kind == DoudizhuKind.Bomb)
            return false;

        return current.Kind == previous.Kind && current.Length == previous.Length && current.Value > previous.Value;
    }

    static DoudizhuCombo? ClassifyPlane(int[] counts, int total)
    {
        DoudizhuCombo? best = null;
        for (int start = 3; start <= Ace; start++)
        {
            if (counts[start] < 3)
                continue;

            for (int end = start + 1; end <= Ace && counts[end] >= 3; end++)
            {
                int length = end - start + 1;
                int rest = total - length * 3;
                int[] remain = (int[])counts.Clone();
                for (int value = start; value <= end; value++)
                    remain[value] -= 3;

                DoudizhuKind? kind = rest switch {
                    0 => DoudizhuKind.Plane,
                    _ when rest == length && remain.All(count => count <= 1) => DoudizhuKind.PlaneWithSingles,
                    _ when rest == length * 2 && remain.All(count => count is 0 or 2) => DoudizhuKind.PlaneWithPairs,
                    _ => null,
                };
                if (kind != null && (best == null || end > best.Value))
                    best = new DoudizhuCombo(kind.Value, end, total);
            }
        }

        return best;
    }
    static List<int> ValuesWithCount(int[] counts, int count)
    {
        return Enumerable.Range(3, BigJoker - 2).Where(value => counts[value] == count).ToList();
    }
    static bool IsRun(List<int> values)
    {
        return values.Zip(values.Skip(1)).All(pair => pair.Second == pair.First + 1);
    }
}
