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
        牌面大小：3 < 4 < 5 < 6 < 7 < 8 < 9 < 10 < J < Q < K < A < 2 < 小王 < 大王；花色不影响牌型。出牌时一次报出完整牌组，不要逐张报。
        - 开局随机决定谁先叫地主。叫地主阶段：回复 <DoudizhuCall/> 叫地主，或 <DoudizhuPass/> 不叫。叫地主者获得三张底牌，与另外两家对抗。
        - 出牌阶段：回复 <DoudizhuPlay>牌面</DoudizhuPlay> 出牌，例如 <DoudizhuPlay>3 3 4 4</DoudizhuPlay>；回复 <DoudizhuPass/> 不出（首家不能不出）。
        - 牌型：单张、对子、三张、三带一、三带一对、顺子（5 张以上，不含 2 和王）、连对（3 对以上）、飞机（2 组以上连续三张，可带同数量的不同单张或不同对子）、四带二（两张单牌或一对）、炸弹（四张相同）、王炸（大王加小王）。
        - 出牌必须与上家牌型相同、张数相同且点数更大；炸弹可以压过任何非炸弹牌，炸弹之间比点数，王炸最大。
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
            bool isLandlord = index == LandlordIndex;
            builder.AppendLine($"你的身份：{(isLandlord ? "地主" : "农民")}");
            builder.AppendLine($"底牌：{DoudizhuCards.Format(bottom)}");
            if (isLandlord)
            {
                string opponents = string.Join("、", Seats.Where((_, seatIndex) => seatIndex != index).Select(player => player.Name));
                builder.AppendLine($"你的对手：{opponents}。");
            }
            else
            {
                int teammateIndex = Enumerable.Range(0, Seats.Count).First(seatIndex => seatIndex != index && seatIndex != LandlordIndex);
                builder.AppendLine($"你的农民队友：{Seats[teammateIndex].Name}；地主是 {Seats[LandlordIndex].Name}。");
            }
        }
        builder.AppendLine($"你的手牌（{hands[index].Count} 张）：{DoudizhuCards.Format(hands[index])}");
        builder.AppendLine("各家剩余牌数：" + string.Join("，", Seats.Select((player, playerIndex) => $"{player.Name} {hands[playerIndex].Count} 张")));
        if (Phase == DoudizhuPhase.Playing)
        {
            if (lastCombo == null)
                builder.AppendLine("你是本轮首家，必须出牌。");
            else
            {
                builder.AppendLine($"上家（{Seats[LastPlayerIndex].Name}）出了：{DoudizhuCards.Format(lastCards)}（{ComboName(lastCombo)}）。你需要用同牌型、同张数且更大的牌压过，或不出。");
                if (index != LandlordIndex && LastPlayerIndex != LandlordIndex)
                    builder.AppendLine("上家是你的农民队友。通常不要压队友的牌；只有你能直接走完、能明显接管牌局，或队友的牌会阻碍你走完时才考虑接管。");
            }
            builder.AppendLine("出牌优先级：能一次走完就立即走完；否则优先出低单、低对或低顺子，尽量保留 2、王、炸弹和高对子作为控制牌。");
            builder.AppendLine("不要为了跟一手小牌拆散对子、三张、顺子或炸弹；只有能阻止对手走完或能直接形成胜势时才拆。");
            builder.AppendLine("对手只剩 1 张时避免送单牌，剩 2 张时谨慎送对子；优先用更大的同型牌压制。");
            List<string> controls = [.. hands[index]
                .GroupBy(card => card)
                .Where(group => group.Key >= DoudizhuCards.Ace || group.Count() == 4)
                .OrderByDescending(group => group.Count() == 4 ? 100 : group.Key)
                .Select(group => group.Count() == 4 ? $"{DoudizhuCards.Name(group.Key)}炸弹" : DoudizhuCards.Name(group.Key))];
            if (controls.Count > 0)
                builder.AppendLine($"你的控制牌：{string.Join("、", controls)}。领先清牌时从最难压制的牌组开始；跟牌时用能压过的最小牌。若大小王都在你手里，连续清牌时大王先于小王。");
            if (hands[index].Count <= 3)
                builder.AppendLine("残局提示：先规划完整出牌顺序；能一次出完立即出完，否则先出对手最难压制的牌组，再出剩余牌，不要先出低牌给对手插入机会。");

            IEnumerable<int> opponents = index == LandlordIndex
                ? Enumerable.Range(0, Seats.Count).Where(seatIndex => seatIndex != index)
                : [LandlordIndex];
            foreach (int opponentIndex in opponents)
            {
                if (hands[opponentIndex].Count > 2)
                    continue;
                builder.AppendLine($"注意：{Seats[opponentIndex].Name} 只剩 {hands[opponentIndex].Count} 张，优先压制，避免送牌。");
            }

            if (index != LandlordIndex)
            {
                int teammateIndex = Enumerable.Range(0, Seats.Count).First(seatIndex => seatIndex != index && seatIndex != LandlordIndex);
                if (hands[teammateIndex].Count <= 2)
                    builder.AppendLine($"队友 {Seats[teammateIndex].Name} 只剩 {hands[teammateIndex].Count} 张，优先让队友出牌，不要抢队友的牌。");
            }
        }
        else
        {
            builder.AppendLine("叫地主前先评估手牌：王、2、A、炸弹、长顺和连对是强牌；小单张多且没有王、2、炸弹通常偏弱。强牌积极叫地主，弱牌谨慎不叫。");
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

    static string ComboName(DoudizhuCombo combo)
    {
        return combo.Kind switch
        {
            DoudizhuKind.Single => "单张",
            DoudizhuKind.Pair => "对子",
            DoudizhuKind.Trio => "三张",
            DoudizhuKind.TrioWithSingle => "三带一",
            DoudizhuKind.TrioWithPair => "三带一对",
            DoudizhuKind.Straight => "顺子",
            DoudizhuKind.PairStraight => "连对",
            DoudizhuKind.Plane => "飞机",
            DoudizhuKind.PlaneWithSingles => "飞机带单",
            DoudizhuKind.PlaneWithPairs => "飞机带对",
            DoudizhuKind.FourWithTwo => "四带二",
            DoudizhuKind.Bomb => "炸弹",
            DoudizhuKind.Rocket => "王炸",
            _ => "未知牌型",
        };
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
