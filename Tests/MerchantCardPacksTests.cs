// MerchantCardPacksTests.cs
// 商人卡包快照（`MerchantCardPacks`）的纯逻辑校验。
// 口径出处：README/施工文档/2026/2026.10/交互/商人交互案.md §4.2（5 个包的分工 + 第 5 包 = 3 张 A + 2 张 S）、
//   §4.3（包内不重复、不上架状态牌）、§5.1（按等级计价）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardSimulator;
using Xunit;

public sealed class MerchantCardPacksTests
{
    private static List<MerchantPackRow> PackRows() =>
        MerchantCatalog.ParseCardPacks(File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "WorldMarket", "MerchantCardPack.csv")));

    private static Dictionary<CardTier, int> Prices() =>
        MerchantCatalog.ParseCardPrices(File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "WorldMarket", "MerchantCardPrice.csv")));

    private static MerchantCardCandidate Card(int id, CardTier tier, bool state = false) =>
        new MerchantCardCandidate { CardId = id, Tier = tier, IsState = state, Name = $"卡{id}" };

    private static List<MerchantCardCandidate> Pool(string prefix, int perTier, bool includeState = false)
    {
        var pool = new List<MerchantCardCandidate>();
        var tiers = new[] { CardTier.D, CardTier.C, CardTier.B, CardTier.A, CardTier.S };
        int id = prefix.GetHashCode() % 1000 * 1000;
        foreach (CardTier tier in tiers)
        {
            for (int i = 0; i < perTier; i++)
            {
                id++;
                pool.Add(Card(id, tier, includeState && i == 0));
            }
        }

        return pool;
    }

    private static List<List<MerchantCardCandidate>> SlotPools() =>
        new List<List<MerchantCardCandidate>> { Pool("s0", 6), Pool("s1", 6), Pool("s2", 6) };

    [Fact]
    public void Five_packs_with_the_designed_slots()
    {
        var packs = MerchantCardPacks.Generate(
            new Random(7), PackRows(), SlotPools(), Pool("g", 6), Prices());

        Assert.Equal(5, packs.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, packs.Select(x => x.PackIndex).ToArray());

        // 包 1–3：角色专属、固定写本槽位。
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal((int)MerchantPackKind.Character, packs[i].Kind);
            Assert.Equal(i, packs[i].OwnerSlot);
            Assert.Equal((int)MerchantOwnerSlotPolicy.Fixed, packs[i].Policy);
        }

        // 包 4：混合（买时选槽位）；包 5：通用（买时选槽位）。
        Assert.Equal((int)MerchantPackKind.Mixed, packs[3].Kind);
        Assert.Equal(-1, packs[3].OwnerSlot);
        Assert.Equal((int)MerchantOwnerSlotPolicy.Chosen, packs[3].Policy);
        Assert.Equal((int)MerchantPackKind.Generic, packs[4].Kind);
        Assert.Equal(-1, packs[4].OwnerSlot);
        Assert.Equal((int)MerchantOwnerSlotPolicy.Chosen, packs[4].Policy);

        Assert.All(packs, pack => Assert.Equal(5, pack.Cards.Count));
        Assert.All(packs, pack => Assert.Equal(5, MerchantCardPacks.RemainingCount(pack)));
    }

    [Fact]
    public void Generic_pack_is_three_A_and_two_S()
    {
        var packs = MerchantCardPacks.Generate(
            new Random(11), PackRows(), SlotPools(), Pool("g", 6), Prices());
        RunMerchantCardPackSave generic = packs.Single(x => x.PackIndex == 5);

        Assert.Equal(3, generic.Cards.Count(c => c.Tier == (int)CardTier.A));
        Assert.Equal(2, generic.Cards.Count(c => c.Tier == (int)CardTier.S));
    }

    [Fact]
    public void Packs_never_contain_state_cards_and_never_repeat_a_card()
    {
        var slotPools = new List<List<MerchantCardCandidate>>
        {
            Pool("s0", 6, includeState: true),
            Pool("s1", 6, includeState: true),
            Pool("s2", 6, includeState: true),
        };

        var packs = MerchantCardPacks.Generate(
            new Random(3), PackRows(), slotPools, Pool("g", 6, includeState: true), Prices());

        foreach (RunMerchantCardPackSave pack in packs)
        {
            Assert.Equal(pack.Cards.Count, pack.Cards.Select(x => x.CardId).Distinct().Count());
        }
    }

    [Fact]
    public void Card_prices_come_from_the_tier_price_table()
    {
        Dictionary<CardTier, int> prices = Prices();
        var packs = MerchantCardPacks.Generate(
            new Random(5), PackRows(), SlotPools(), Pool("g", 6), prices);

        foreach (RunMerchantCardPackSave pack in packs)
        {
            foreach (RunMerchantCardEntrySave card in pack.Cards)
            {
                Assert.Equal(prices[(CardTier)card.Tier], card.Price);
                Assert.False(card.Sold);
                Assert.Equal(-1, card.GrantedSlot);
            }
        }
    }

    [Fact]
    public void Missing_tier_price_keeps_the_card_off_the_shelf()
    {
        var prices = new Dictionary<CardTier, int> { [CardTier.S] = 125 };
        var packs = MerchantCardPacks.Generate(
            new Random(9), PackRows(), SlotPools(), Pool("g", 6), prices);

        // 只有 S 档有价 → 非通用包全部不上架（不静默取别的价），通用包只上 S 档那两张。
        Assert.All(packs.Where(x => x.Kind != (int)MerchantPackKind.Generic), pack => Assert.Empty(pack.Cards));
        Assert.Equal(2, packs.Single(x => x.PackIndex == 5).Cards.Count);
    }

    [Fact]
    public void Same_seed_gives_the_same_snapshot()
    {
        var first = MerchantCardPacks.Generate(new Random(21), PackRows(), SlotPools(), Pool("g", 6), Prices());
        var second = MerchantCardPacks.Generate(new Random(21), PackRows(), SlotPools(), Pool("g", 6), Prices());

        Assert.Equal(
            first.SelectMany(x => x.Cards).Select(x => x.CardId).ToArray(),
            second.SelectMany(x => x.Cards).Select(x => x.CardId).ToArray());
    }

    [Fact]
    public void Empty_pool_leaves_the_pack_empty_without_throwing()
    {
        var packs = MerchantCardPacks.Generate(
            new Random(1), PackRows(),
            new List<List<MerchantCardCandidate>> { new(), new(), new() },
            new List<MerchantCardCandidate>(),
            Prices());

        Assert.Equal(5, packs.Count);
        Assert.All(packs, pack => Assert.Empty(pack.Cards));
    }
}
