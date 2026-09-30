// EventCardRewardTests.cs
// 覆盖非战斗来源发卡（战斗结算界面交互案 §5.7 / 剧情事件.md 的 `CardAdd`）：
// 效果解析、每份 = 一个槽位、指定卡份候选恒为 1 张、任选份只取该份角色卡池、不做折损。
using System;
using System.Collections.Generic;
using Xunit;

public class EventCardRewardTests
{
    private static RunSaveData Team() => new RunSaveData
    {
        CharacterSlots = new List<RunCharacterSlotSave>
        {
            new RunCharacterSlotSave { CharacterId = 1002 },
            new RunCharacterSlotSave { CharacterId = 1003 },
            new RunCharacterSlotSave { CharacterId = 1004 },
        },
    };

    private static IReadOnlyList<int> Pool(int characterId) => characterId == 1003
        ? new List<int> { 10000001, 10000002, 10000003, 10000004 }
        : new List<int> { 11002001, 11002002 };

    [Fact]
    public void TryParseEffect_AcceptsSlotTargetWithOptionalCardId()
    {
        Assert.True(EventCardReward.TryParseEffect("CardAdd", "Slot", 1, "10000001", out EventCardRewardSpec fixedSpec, out _));
        Assert.Equal(1, fixedSpec.SlotIndex);
        Assert.Equal(10000001, fixedSpec.CardId);

        Assert.True(EventCardReward.TryParseEffect("CardAdd", "Slot", 0, "", out EventCardRewardSpec poolSpec, out _));
        Assert.Equal(0, poolSpec.SlotIndex);
        Assert.Equal(0, poolSpec.CardId); // 未指定具体卡 = 该角色卡池任选

        Assert.True(EventCardReward.TryParseEffect("CardAdd", "Slot", 2, "  ", out EventCardRewardSpec blankSpec, out _));
        Assert.Equal(0, blankSpec.CardId);
    }

    [Theory]
    [InlineData("HpDelta", "Slot", 0, "", "不是发卡效果")]        // 非发卡效果
    [InlineData("CardAdd", "SelectedPlayer", 0, "", "target")]   // target 必须是 Slot
    [InlineData("CardAdd", "Slot", -1, "", "槽位必须")]           // 槽位不能为负
    [InlineData("CardAdd", "Slot", 0, "abc", "卡牌 Id")]          // referenceId 必须是卡牌 Id 或留空
    [InlineData("CardAdd", "Slot", 0, "0", "卡牌 Id")]
    public void TryParseEffect_RejectsInvalidEffects(string type, string target, int value, string referenceId, string errorFragment)
    {
        Assert.False(EventCardReward.TryParseEffect(type, target, value, referenceId, out _, out string error));
        Assert.Contains(errorFragment, error);
    }

    [Fact]
    public void BuildPools_FixedCardPool_HasExactlyThatSingleCandidate()
    {
        List<SettlementCardPoolSave> pools = EventCardReward.BuildPools(
            new List<EventCardRewardSpec> { new EventCardRewardSpec(1, 10000001) },
            Team(), Pool, 3, new Random(1));

        SettlementCardPoolSave pool = Assert.Single(pools);
        Assert.Equal(1, pool.SlotIndex);
        Assert.Equal(1003, pool.CharacterId);
        Assert.Equal(new List<int> { 10000001 }, pool.CandidateCardIds);
    }

    [Fact]
    public void BuildPools_LoosePool_SamplesThreeCardsFromThatSlotCharacterOnly()
    {
        List<SettlementCardPoolSave> pools = EventCardReward.BuildPools(
            new List<EventCardRewardSpec> { new EventCardRewardSpec(1, 0) },
            Team(), Pool, 3, new Random(20260928));

        SettlementCardPoolSave pool = Assert.Single(pools);
        Assert.Equal(3, pool.CandidateCardIds.Count);
        Assert.All(pool.CandidateCardIds, cardId => Assert.Contains(cardId, Pool(1003)));
    }

    [Fact]
    public void BuildPools_SkipsOutOfRangeAndDuplicateSlots()
    {
        List<SettlementCardPoolSave> pools = EventCardReward.BuildPools(
            new List<EventCardRewardSpec>
            {
                new EventCardRewardSpec(0, 0),
                new EventCardRewardSpec(0, 10000002), // 同一槽位第二份：份的身份 = 槽位，只保留第一条
                new EventCardRewardSpec(7, 0),        // 队伍只有 3 个槽位
                new EventCardRewardSpec(2, 0),
            },
            Team(), Pool, 3, new Random(7));

        Assert.Equal(2, pools.Count);
        Assert.Equal(new List<int> { 0, 2 }, pools.ConvertAll(pool => pool.SlotIndex));
    }

    [Fact]
    public void BuildPools_EmptySpecsOrRun_ReturnsEmpty()
    {
        Assert.Empty(EventCardReward.BuildPools(new List<EventCardRewardSpec>(), Team(), Pool, 3, new Random(1)));
        Assert.Empty(EventCardReward.BuildPools(null, Team(), Pool, 3, new Random(1)));
        Assert.Empty(EventCardReward.BuildPools(new List<EventCardRewardSpec> { new EventCardRewardSpec(0, 0) }, null, Pool, 3, new Random(1)));
    }

    [Fact]
    public void BuildRequest_NonBattleSource_NeverDiscountsAndBindsNoDropTable()
    {
        List<SettlementCardPoolSave> pools = new List<SettlementCardPoolSave>
        {
            new SettlementCardPoolSave { SlotIndex = 1, CharacterId = 1003, CandidateCardIds = new List<int> { 10000001 } },
        };

        SettlementStartRequest request = EventCardReward.BuildRequest("余响的符碑", MapNodeType.Merchant, pools);

        Assert.Equal("余响的符碑", request.SourceName);
        Assert.Equal(MapNodeType.Merchant, request.SourceNodeType);
        Assert.Equal(0, request.DropTableId);
        Assert.Same(pools, request.CardPools);
        Assert.Equal(0, request.LossTier);
        Assert.Equal(1.0, request.ValueRatio);
    }
}
