// SettlementRewardPresenterTests.cs
// 覆盖战斗结算界面交互案（2026-09-27 定稿）里可纯逻辑验证的部分：
// 物品 Tab 去重键兼容、卡牌份（按槽位 / 折损取消整份 / 候选只取该角色卡池）、文案与放弃日志（§7.4）。
using System;
using System.Collections.Generic;
using Xunit;

[Collection("ItemNameResolverState")]
public class SettlementRewardPresenterTests
{
    /// <summary>
    /// 物品显示名是**全局注册表**（`LoadingSystem` 加载配表时填充）。本类断言的是「未注册 → `未定义材料(ID)`」兜底，
    /// 因此每个测试前先清空，避免与其它测试类的注册互扰；同时与同样读写该注册表的测试类共用 Collection 串行执行。
    /// </summary>
    public SettlementRewardPresenterTests() => ItemNameResolver.Clear();

    private static List<DropTableEntry> Entries()
    {
        return new List<DropTableEntry>
        {
            new DropTableEntry { DropTableId = 2001, Category = DropCategory.Card, RewardParam = 0, Amount = 3 },
            new DropTableEntry { DropTableId = 2001, Category = DropCategory.Gold, RewardParam = 0, Amount = 50 },
            new DropTableEntry { DropTableId = 2002, Category = DropCategory.Gold, RewardParam = 0, Amount = 120 },
            new DropTableEntry { DropTableId = 2002, Category = DropCategory.Material, RewardParam = 101, Amount = 2 },
        };
    }

    private static List<int> Sorted(List<int> values)
    {
        List<int> copy = new List<int>(values);
        copy.Sort();
        return copy;
    }

    [Fact]
    public void BuildItemTabs_KeepsRowIndexIncludingCardRow_SoOldClaimKeysStillMatch()
    {
        RunSaveData run = new RunSaveData { SettlementDropTableId = 2001 };

        List<SettlementItemTab> tabs = SettlementRewardPresenter.BuildItemTabs(run, Entries());

        Assert.Single(tabs);
        Assert.Equal(1, tabs[0].RowIndex);             // Card 行占行号 0 → 金币行仍是原行号 1
        Assert.Equal("1:Gold:0:50", tabs[0].ClaimKey); // 与旧存档去重键格式一致（旧档已领取项不会重复发放）
        Assert.Equal("金币 +50", tabs[0].Text);
        Assert.False(tabs[0].Claimed);
    }

    [Fact]
    public void BuildItemTabs_ExcludesCardRows_AndMarksClaimedRows()
    {
        RunSaveData run = new RunSaveData { SettlementDropTableId = 2002 };
        run.SettlementClaimedRewardKeys.Add("1:Material:101:2");

        List<SettlementItemTab> tabs = SettlementRewardPresenter.BuildItemTabs(run, Entries());

        Assert.Equal(2, tabs.Count);
        Assert.Equal("金币 +120", tabs[0].Text);
        Assert.Equal("材料 未定义材料(101) ×2", tabs[1].Text); // 未注册名字时按 `未定义材料(ID)` 兜底（P1-4）；注册名场景见 ItemTableFileTests
        Assert.True(tabs[1].Claimed);
        Assert.Equal(1, SettlementRewardPresenter.CountUnclaimedItems(run, Entries()));
    }

    [Fact]
    public void BuildCardPools_FullReward_OnePoolPerSlot_FromThatCharactersOwnPool()
    {
        List<SettlementCardSlot> slots = new List<SettlementCardSlot>
        {
            new SettlementCardSlot { SlotIndex = 0, CharacterId = 1002 },
            new SettlementCardSlot { SlotIndex = 1, CharacterId = 1003 },
        };

        List<SettlementCardPoolSave> pools = SettlementRewardPresenter.BuildCardPools(
            slots,
            3,
            characterId => characterId == 1002 ? new List<int> { 1, 2, 3 } : new List<int> { 4, 5 },
            3,
            new Random(1));

        Assert.Equal(2, pools.Count);
        Assert.Equal(0, pools[0].SlotIndex);
        Assert.Equal(1002, pools[0].CharacterId);
        Assert.Equal(new List<int> { 1, 2, 3 }, Sorted(pools[0].CandidateCardIds));
        // 候选只取该份角色自己的卡池：不足 3 张按实际张数，不跨角色补足。
        Assert.Equal(new List<int> { 4, 5 }, Sorted(pools[1].CandidateCardIds));
    }

    [Fact]
    public void BuildCardPools_ThreeSlotsSameCharacter_KeepsOneSharePerSlot()
    {
        List<SettlementCardSlot> slots = new List<SettlementCardSlot>
        {
            new SettlementCardSlot { SlotIndex = 0, CharacterId = 1002 },
            new SettlementCardSlot { SlotIndex = 1, CharacterId = 1002 },
            new SettlementCardSlot { SlotIndex = 2, CharacterId = 1002 },
        };

        List<SettlementCardPoolSave> pools = SettlementRewardPresenter.BuildCardPools(slots, 3, _ => new List<int> { 1, 2, 3 }, 3, new Random(2));

        Assert.Equal(3, pools.Count);
        Assert.Equal(new List<int> { 0, 1, 2 }, new List<int> { pools[0].SlotIndex, pools[1].SlotIndex, pools[2].SlotIndex });
        foreach (SettlementCardPoolSave pool in pools)
        {
            Assert.Equal(3, pool.CandidateCardIds.Count);
        }
    }

    [Fact]
    public void BuildCardPools_Loss_DropsWholeShares_NotCandidatesInsideAShare()
    {
        List<SettlementCardSlot> slots = new List<SettlementCardSlot>
        {
            new SettlementCardSlot { SlotIndex = 0, CharacterId = 1002 },
            new SettlementCardSlot { SlotIndex = 1, CharacterId = 1003 },
            new SettlementCardSlot { SlotIndex = 2, CharacterId = 1004 },
        };

        List<SettlementCardPoolSave> pools = SettlementRewardPresenter.BuildCardPools(slots, 1, _ => new List<int> { 1, 2, 3 }, 3, new Random(7));

        // 折损取消的是整个「份」：只剩 1 份，但该份的三选一仍是 3 张候选。
        Assert.Single(pools);
        Assert.Equal(3, pools[0].CandidateCardIds.Count);
    }

    [Fact]
    public void CountUnclaimed_AddsItemTabsAndUnclaimedShares()
    {
        RunSaveData run = new RunSaveData { SettlementDropTableId = 2001 };
        run.SettlementCardPools.Add(new SettlementCardPoolSave { SlotIndex = 0, CharacterId = 1002 });
        run.SettlementCardPools.Add(new SettlementCardPoolSave { SlotIndex = 1, CharacterId = 1003 });

        Assert.Equal(3, SettlementRewardPresenter.CountUnclaimed(run, Entries()));
        run.SettlementCardClaims.Add(new SettlementCardClaimSave { SlotIndex = 0, CardId = 11 });
        Assert.Equal(2, SettlementRewardPresenter.CountUnclaimed(run, Entries()));
        Assert.Equal(1, SettlementRewardPresenter.CountUnclaimedCards(run));
    }

    [Fact]
    public void FormatLossLine_MatchesInteractionDocExamples()
    {
        Assert.Equal("卡牌奖励减少：击败 57.0%（≤ 70%）→ 卡牌奖励 2 份", SettlementRewardPresenter.FormatLossLine(0.5696, 2));
        Assert.Equal("卡牌奖励减少：击败 43.0%（< 50%）→ 卡牌奖励 1 份", SettlementRewardPresenter.FormatLossLine(0.4304, 1));
        Assert.Equal(string.Empty, SettlementRewardPresenter.FormatLossLine(1.0, 3));
    }

    [Fact]
    public void CardTabText_UsesCharacterDisplayName()
    {
        Assert.Equal("将一张牌添加到你的牌组。· 重剑手2", SettlementRewardPresenter.GetCardTabText("重剑手2"));
    }

    /// <summary>
    /// 2026-10-02 用户口径：领取过的条目**直接从面板列表里消失**（不再显示成「已领取」）。
    /// 面板渲染走 BuildVisibleItemTabs / BuildVisibleCardPools，这里断言「领过的不再出现」。
    /// </summary>
    [Fact]
    public void VisibleLists_DropClaimedEntries()
    {
        RunSaveData run = new RunSaveData { SettlementDropTableId = 2002 };
        run.SettlementCardPools.Add(new SettlementCardPoolSave { SlotIndex = 0, CharacterId = 1002 });
        run.SettlementCardPools.Add(new SettlementCardPoolSave { SlotIndex = 1, CharacterId = 1003 });

        // 领取前：两条物品 Tab（金币 / 材料）+ 两份卡牌 Tab 都在。
        Assert.Equal(2, SettlementRewardPresenter.BuildVisibleItemTabs(run, Entries()).Count);
        Assert.Equal(2, SettlementRewardPresenter.BuildVisibleCardPools(run).Count);

        // 领掉「材料」这一条与槽位 0 的那一份 → 两个列表里都不再出现它们。
        run.SettlementClaimedRewardKeys.Add("1:Material:101:2");
        run.SettlementCardClaims.Add(new SettlementCardClaimSave { SlotIndex = 0, CardId = 11 });

        List<SettlementItemTab> visibleItems = SettlementRewardPresenter.BuildVisibleItemTabs(run, Entries());
        Assert.Single(visibleItems);
        Assert.Equal("金币 +120", visibleItems[0].Text);

        List<SettlementCardPoolSave> visiblePools = SettlementRewardPresenter.BuildVisibleCardPools(run);
        Assert.Single(visiblePools);
        Assert.Equal(1, visiblePools[0].SlotIndex);

        // 浮窗计数与列表口径一致（未领取项数 = 还看得见的 Tab 数）。
        Assert.Equal(1, SettlementRewardPresenter.CountUnclaimedItems(run, Entries()));
        Assert.Equal(1, SettlementRewardPresenter.CountUnclaimedCards(run));
        Assert.Equal(2, SettlementRewardPresenter.CountUnclaimed(run, Entries()));
    }

    [Fact]
    public void FormatAbandonLog_WritesOneLineWithKeywordDetailsAndMode()
    {
        string log = SettlementRewardPresenter.FormatAbandonLog(
            "劫匪团伙",
            SettlementRewardPresenter.GetSourceNodeTypeText(MapNodeType.NormalCombat),
            new List<string> { "金币 +14", "道具 2001 ×2", "卡牌（重剑手2）未领" },
            SettlementRewardPresenter.AbandonModeDialog);

        Assert.Equal("[结算] SETTLEMENT_ABANDONED: 劫匪团伙（普通敌袭）| 未领取 3 项 | 金币 +14；道具 2001 ×2；卡牌（重剑手2）未领 | 弹窗确认", log);
        Assert.Contains("SETTLEMENT_ABANDONED", log);
    }

    [Fact]
    public void GetSourceNodeTypeText_MapsBattleAndNonCombatSources()
    {
        Assert.Equal("普通敌袭", SettlementRewardPresenter.GetSourceNodeTypeText(MapNodeType.NormalCombat));
        Assert.Equal("高危敌袭", SettlementRewardPresenter.GetSourceNodeTypeText(MapNodeType.HighRiskCombat));
        Assert.Equal("商人", SettlementRewardPresenter.GetSourceNodeTypeText(MapNodeType.Merchant));
        Assert.Equal("未知", SettlementRewardPresenter.GetSourceNodeTypeText(MapNodeType.Empty));
    }

    [Fact]
    public void BuildUnclaimedDetails_UsesSlotDisplayNamesAndSkipsClaimedEntries()
    {
        RunSaveData run = new RunSaveData { SettlementDropTableId = 2002, SettlementEncounterName = "劫匪团伙" };
        run.SettlementCardPools.Add(new SettlementCardPoolSave { SlotIndex = 2, CharacterId = 1002 });

        List<string> details = SettlementRewardPresenter.BuildUnclaimedDetails(run, Entries(), slotIndex => slotIndex == 2 ? "重剑手2" : "角色 ?");

        Assert.Equal(new List<string> { "金币 +120", "材料 未定义材料(101) ×2", "卡牌（重剑手2）未领" }, details);

        run.SettlementCardClaims.Add(new SettlementCardClaimSave { SlotIndex = 2, CardId = 1002 });
        Assert.Equal(2, SettlementRewardPresenter.BuildUnclaimedDetails(run, Entries(), slotIndex => "重剑手").Count);
    }

    [Fact]
    public void LegacyPool_WithoutSlotIndex_FallsBackToCharacterId()
    {
        SettlementCardPoolSave legacy = new SettlementCardPoolSave { SlotIndex = SettlementRewardPresenter.LegacySlotIndex, CharacterId = 1002 };

        Assert.Equal("角色 1002", SettlementRewardPresenter.GetSlotDisplayName(legacy, _ => "角色 ?"));
    }
}
