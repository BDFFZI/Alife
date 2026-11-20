using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Alife.Function.TableGame;

namespace Alife.Function.Doudizhu;

public enum DoudizhuPhase
{
    Bidding,
    Playing,
    Over,
}

public class DoudizhuTable : GameTable
{
    public static DoudizhuTable Instance { get; } = new();

    public DoudizhuPhase Phase { get; private set; }
    public int LandlordIndex { get; private set; } = -1;
    public int LastPlayerIndex { get; private set; } = -1;
    public int WinnerIndex { get; private set; } = -1;
    public IReadOnlyList<int> Bottom => bottom;
    public IReadOnlyList<int> LastCards => lastCards;
    public IReadOnlyList<string> Log => log;

    public IReadOnlyList<int> GetHand(int index)
    {
        return hands[index];
    }

    public string? Call(int index)
    {
        if (Phase != DoudizhuPhase.Bidding)
            return "当前不是叫地主阶段";
        if (index != turnIndex)
            return "还没轮到你";

        LandlordIndex = index;
        Phase = DoudizhuPhase.Playing;
        hands[index].AddRange(bottom);
        hands[index].Sort();
        AddLog($"{Seats[index].Name} 叫地主，获得底牌 {DoudizhuCards.Format(bottom)}");
        turnIndex = index;
        Commit();
        return null;
    }
    public string? Pass(int index)
    {
        if (Phase == DoudizhuPhase.Over)
            return "牌局已经结束";
        if (index != turnIndex)
            return "还没轮到你";
        if (Phase == DoudizhuPhase.Bidding)
        {
            bidDeclines++;
            AddLog($"{Seats[index].Name} 不叫");
            if (bidDeclines == Seats.Count)
            {
                Deal();
                AddLog("三家都不叫，重新发牌");
            }
            turnIndex = Next(index);
            Commit();
            return null;
        }
        if (lastCombo == null)
            return "你是本轮首家，必须出牌";

        passesInRow++;
        AddLog($"{Seats[index].Name} 不出");
        if (passesInRow == Seats.Count - 1)
        {
            lastCombo = null;
            lastCards = [];
            passesInRow = 0;
        }
        turnIndex = Next(index);
        Commit();
        return null;
    }
    public string? Play(int index, List<int> cards)
    {
        if (Phase != DoudizhuPhase.Playing)
            return "当前不是出牌阶段";
        if (index != turnIndex)
            return "还没轮到你";

        List<int> remain = [.. hands[index]];
        foreach (int card in cards)
        {
            if (remain.Remove(card) == false)
                return "你手里没有这些牌";
        }

        DoudizhuCombo? combo = DoudizhuCards.Classify(cards);
        if (combo == null)
            return "这不是合法的牌型";
        if (lastCombo != null && DoudizhuCards.Beats(combo, lastCombo) == false)
            return "压不住上家的牌，请出更大的同牌型，或不出";

        hands[index] = remain;
        lastCombo = combo;
        lastCards = [.. cards.OrderBy(card => card)];
        LastPlayerIndex = index;
        passesInRow = 0;
        AddLog($"{Seats[index].Name} 出 {DoudizhuCards.Format(cards)}");
        if (remain.Count == 0)
        {
            Phase = DoudizhuPhase.Over;
            WinnerIndex = index;
            AddLog($"{Seats[index].Name} 出完手牌，{(index == LandlordIndex ? "地主" : "农民")}方获胜");
            NotifyAll($"{Seats[index].Name} 出完手牌，{(index == LandlordIndex ? "地主" : "农民")}方获胜，牌局结束。");
        }
        else
        {
            turnIndex = Next(index);
        }

        Commit();
        return null;
    }

    protected override TableSeat? ActiveSeat => Phase == DoudizhuPhase.Over ? null : Seats[turnIndex];
    protected override string Rules => """
        【斗地主规则】
        这是一局三人斗地主，轮到你时你会收到牌局状态。牌面用空格分隔，可用牌面为：3 4 5 6 7 8 9 10 J Q K A 2 小王 大王。
        - 开局随机决定谁先叫地主。叫地主阶段：回复 <DoudizhuCall/> 叫地主，或 <DoudizhuPass/> 不叫。叫地主者获得三张底牌，与另外两家对抗。
        - 出牌阶段：回复 <DoudizhuPlay>牌面</DoudizhuPlay> 出牌，例如 <DoudizhuPlay>3 3 4 4</DoudizhuPlay>；回复 <DoudizhuPass/> 不出（首家不能不出）。
        - 牌型：单张、对子、三张、三带一、三带一对、顺子（5 张以上，不含 2 和王）、连对（3 对以上）、飞机（2 组以上连续三张，可带同数量的不同单张或不同对子）、四带二（两张单牌或一对）、炸弹（四张相同）、王炸（大王加小王）。
        - 出牌必须与上家牌型相同、张数相同且点数更大；炸弹和王炸可以压过非炸弹牌。
        - 先出完手牌者获胜。地主先出完则地主方胜；农民任意一方先出完则农民方胜。
        回复时可以正常说话，但牌局指令必须使用上面的标签。
        """;
    protected override string StartNotice => $"斗地主开局：{Seats[0].Name}、{Seats[1].Name}、{Seats[2].Name}。开局随机由{Seats[turnIndex].Name}先叫地主；请等待系统发出轮到你的提示，不要提前行动。";
    protected override string StopNotice => "斗地主牌局结束。";

    protected override void OnStart()
    {
        LandlordIndex = -1;
        LastPlayerIndex = -1;
        WinnerIndex = -1;
        Phase = DoudizhuPhase.Bidding;
        lastCombo = null;
        lastCards = [];
        passesInRow = 0;
        bidDeclines = 0;
        log.Clear();
        turnIndex = Random.Shared.Next(Seats.Count);
        Deal();
    }
    protected override string DescribeTurn(TableSeat seat)
    {
        int index = IndexOfSeat(seat);
        StringBuilder builder = new();
        builder.AppendLine("【牌局状态】");
        builder.AppendLine($"你的座位：{seat.Name}");
        if (Phase == DoudizhuPhase.Bidding)
        {
            builder.AppendLine("当前阶段：叫地主（尚未确定地主）。轮到你决定是否叫地主。");
        }
        else
        {
            builder.AppendLine($"你的身份：{(index == LandlordIndex ? "地主" : "农民")}");
            builder.AppendLine($"底牌：{DoudizhuCards.Format(bottom)}");
        }
        builder.AppendLine($"你的手牌（{hands[index].Count} 张）：{DoudizhuCards.Format(hands[index])}");
        builder.AppendLine("各家剩余牌数：" + string.Join("，", Seats.Select((player, playerIndex) => $"{player.Name} {hands[playerIndex].Count} 张")));
        if (Phase == DoudizhuPhase.Playing)
        {
            if (lastCombo == null)
                builder.AppendLine("你是本轮首家，必须出牌。");
            else
                builder.AppendLine($"上家（{Seats[LastPlayerIndex].Name}）出了：{DoudizhuCards.Format(lastCards)}。你需要压过它，或不出。");
            builder.AppendLine("出牌前先拆分手牌，规划单张、对子、顺子和炸弹；对手只剩少量牌时优先压制，不要为了出小牌拆散关键牌型。");
        }
        else
        {
            builder.AppendLine("叫地主前先评估手牌：大牌、炸弹、连牌和单张压力；手牌强就抢底牌，手牌弱就谨慎。");
        }
        builder.AppendLine("最近动态：");
        foreach (string line in log.TakeLast(4))
            builder.AppendLine(line);
        builder.AppendLine();
        if (Phase == DoudizhuPhase.Bidding)
            builder.Append("请回复 <DoudizhuCall/> 叫地主，或 <DoudizhuPass/> 不叫。");
        else
            builder.Append("请回复 <DoudizhuPlay>牌面</DoudizhuPlay> 出牌，或 <DoudizhuPass/> 不出。");
        return builder.ToString();
    }

    void Deal()
    {
        int[] deck = DoudizhuCards.CreateDeck().ToArray();
        Random.Shared.Shuffle(deck);
        hands = Enumerable.Range(0, Seats.Count)
            .Select(index => deck.Skip(index * 17).Take(17).OrderBy(card => card).ToList())
            .ToArray();
        bottom = [.. deck.Skip(51)];
        bidDeclines = 0;
    }
    void AddLog(string line)
    {
        log.Add(line);
    }
    int Next(int index)
    {
        return (index + 1) % Seats.Count;
    }

    List<int>[] hands = [];
    List<int> bottom = [];
    List<int> lastCards = [];
    DoudizhuCombo? lastCombo;
    int turnIndex;
    int passesInRow;
    int bidDeclines;
    readonly List<string> log = [];
}
