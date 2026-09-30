// RunSaveSnapshotTests.cs
// 覆盖 v2 存档：System.Text.Json IncludeFields 后，public 字段（位置/种子/HP/卡级数/状态机字段）
// 都能完整往返，避免“继续游戏回起点 / 数据丢字段”类问题。
using System.Collections.Generic;
using CardSimulator.Battlefield;
using Xunit;

public class RunSaveSnapshotTests
{
    private static RunSaveData BuildSampleData()
    {
        RunSaveData data = new RunSaveData
        {
            SchemaVersion = 2,
            GameMode = RunGameModes.InSettlement,
            Gold = 99,
            Keys = 2,
            MapState = new RunMapStateSave
            {
                Act = 1,
                Seed = 20260904,
                CurrentNodeId = 42,
                NormalEncounterIndex = 3,
                TimePoints = 5,
                VisitedNodeIds = new List<int> { 0, 7, 42 },
            },
        };
        data.CharacterSlots.Add(new RunCharacterSlotSave { CharacterId = 1002, CurrentHp = 23, MaxHp = 30 });
        data.CharacterSlots.Add(new RunCharacterSlotSave { CharacterId = 1002, CurrentHp = 25, MaxHp = 30 });
        data.DeckSlots.Add(new List<RunDeckEntry>
        {
            new RunDeckEntry { CardId = 11002001, PermanentUpgradeLevel = 3 },
            new RunDeckEntry { CardId = 21002002, PermanentUpgradeLevel = 0 },
        });
        data.DeckSlots.Add(new List<RunDeckEntry>
        {
            new RunDeckEntry { CardId = 10000001, PermanentUpgradeLevel = 1 },
        });
        data.PendingEncounterLayer = "第一层";
        data.PendingEncounterNodeType = (int)MapNodeType.NormalCombat;
        data.PendingEncounterName = "普通敌袭-中等";
        data.PendingMonsterIds = new List<int> { 3003 };
        data.PendingDropTableId = 2001;
        data.PendingSourceNodeType = (int)MapNodeType.Merchant; // 非战斗来源发卡的结算来源类型（§5.7）
        data.SettlementEncounterName = "普通敌袭-中等";
        data.SettlementDropTableId = 2001;
        data.SettlementCandidateCardIds = new List<int> { 11002005, 21002004, 31002006 };
        data.SettlementCardPools.Add(new SettlementCardPoolSave
        {
            SlotIndex = 0,
            CharacterId = 1002,
            CandidateCardIds = new List<int> { 11002005, 21002004, 31002006 },
        });
        data.SettlementCardPools.Add(new SettlementCardPoolSave
        {
            SlotIndex = 2,
            CharacterId = 1003,
            CandidateCardIds = new List<int> { 11002007 },
        });
        data.SettlementCardClaims.Add(new SettlementCardClaimSave { SlotIndex = 0, CardId = 21002004 });
        data.SettlementSourceNodeType = (int)MapNodeType.NormalCombat;
        data.SettlementLossTier = 1;
        data.SettlementValueRatio = 0.5700;
        data.SettlementPanelClosed = true;
        data.SuppressAbandonSettlementConfirm = true;
		data.Materials[101] = 2;
		data.Items[201] = 1;
		data.Equipment[301] = 1;
		data.SettlementClaimedRewardKeys.Add("1:Gold:0:50");
		data.StolenGoldFromMonsters.Add(new StolenGoldEntry { InstanceId = "F1-006-M01", MonsterId = 3115, Amount = 4 });
		data.StolenGoldFromMonsters.Add(new StolenGoldEntry { InstanceId = "F1-006-M02", MonsterId = 3115, Amount = 1 });
        data.PostBattleBattlefield = new RunPostBattleSave
        {
            LevelId = "F1-006",
            MapId = "M-F1-001",
            Round = 4,
            SelectedSlotIndex = 2,
            GroundObjects =
            {
                new RunGroundObjectSave
                {
                    InstanceId = "ground-1", DefinitionId = "石头", Kind = (int)GroundObjectKind.Item,
                    SpatialShape = (int)ItemSpatialShape.Line, ItemMaxRange = 3, ItemLength = 3, DamageAmount = 3,
                    Q = 1, R = -2,
                },
                new RunGroundObjectSave
                {
                    InstanceId = "ground-2", DefinitionId = "尖刺陷阱", Kind = (int)GroundObjectKind.Trap,
                    TriggerMode = (int)EntryTriggerMode.EveryEntry, Q = 0, R = 0,
                },
            },
            Loadouts =
            {
                new RunLoadoutSave
                {
                    LeftHand = new RunGroundObjectSave { InstanceId = "run-equipped-1", DefinitionId = "双手剑", Kind = (int)GroundObjectKind.Equipment, HandsRequired = 2, AttackRange = 1 },
                    Items = { null, new RunGroundObjectSave { InstanceId = "picked-1", DefinitionId = "治疗药剂", Kind = (int)GroundObjectKind.Item } },
                },
            },
        };
        data.PostBattleBattlefield.Units.Add(new RunUnitPlacementSave
        {
            OrderIndex = 0, UnitId = 12, Name = "重剑手", Role = 0, SlotIndex = 0, Q = 2, R = -1,
            Presence = (int)BattlefieldPresence.Active, Hp = 17,
        });
        data.PostBattleBattlefield.Units.Add(new RunUnitPlacementSave
        {
            OrderIndex = 4, UnitId = 21, Name = "堕魔守卫", Role = 1, InstanceKey = "F1-006-M01",
            Q = -2, R = 2, Presence = (int)BattlefieldPresence.Defeated, Hp = 0,
        });
        return data;
    }

    [Fact]
    public void RoundTrip_PreservesPositionAndAllPublicFields()
    {
        RunSaveData data = BuildSampleData();
        string json = RunSaveJson.Serialize(data);
        Assert.False(string.IsNullOrWhiteSpace(json));

        RunSaveData restored = RunSaveJson.Deserialize(json);
        Assert.NotNull(restored);
        Assert.Equal(data.GameMode, restored.GameMode);
        Assert.Equal(42, restored.MapState.CurrentNodeId);
        Assert.Equal(20260904, restored.MapState.Seed);
        Assert.Equal(99, restored.Gold);
        Assert.Equal(2, restored.Keys);
        Assert.Equal(3, restored.MapState.NormalEncounterIndex);
        Assert.Equal(2, restored.CharacterSlots.Count);
        Assert.Equal(23, restored.CharacterSlots[0].CurrentHp);
        Assert.Equal(30, restored.CharacterSlots[0].MaxHp);
    }

    [Fact]
    public void RoundTrip_PreservesStolenGoldLedgerPerMonsterInstance()
    {
        RunSaveData restored = RunSaveJson.Deserialize(RunSaveJson.Serialize(BuildSampleData()));

        Assert.Equal(2, restored.StolenGoldFromMonsters.Count);
        Assert.Equal("F1-006-M01", restored.StolenGoldFromMonsters[0].InstanceId);
        Assert.Equal(3115, restored.StolenGoldFromMonsters[0].MonsterId);
        Assert.Equal(4, restored.StolenGoldFromMonsters[0].Amount);
        Assert.Equal("F1-006-M02", restored.StolenGoldFromMonsters[1].InstanceId);
        Assert.Equal(1, restored.StolenGoldFromMonsters[1].Amount);
    }

    [Fact]
    public void RoundTrip_PreservesDeckEntriesWithUpgradeLevels()
    {
        RunSaveData data = BuildSampleData();
        RunSaveData restored = RunSaveJson.Deserialize(RunSaveJson.Serialize(data));

        Assert.Equal(2, restored.DeckSlots.Count);
        Assert.Equal(2, restored.DeckSlots[0].Count);
        Assert.Equal(11002001, restored.DeckSlots[0][0].CardId);
        Assert.Equal(3, restored.DeckSlots[0][0].PermanentUpgradeLevel);
        Assert.Equal(1, restored.DeckSlots[1][0].PermanentUpgradeLevel);
    }

    [Fact]
    public void RoundTrip_PreservesPendingEncounterAndSettlement()
    {
        RunSaveData data = BuildSampleData();
        RunSaveData restored = RunSaveJson.Deserialize(RunSaveJson.Serialize(data));

        Assert.Equal("第一层", restored.PendingEncounterLayer);
        Assert.Equal((int)MapNodeType.NormalCombat, restored.PendingEncounterNodeType);
        Assert.Equal(new List<int> { 3003 }, restored.PendingMonsterIds);
        Assert.Equal(2001, restored.PendingDropTableId);
        Assert.Equal((int)MapNodeType.Merchant, restored.PendingSourceNodeType);
        Assert.Equal(2001, restored.SettlementDropTableId);
        Assert.Equal(3, restored.SettlementCandidateCardIds.Count);
        Assert.Equal(11002005, restored.SettlementCandidateCardIds[0]);
        Assert.Equal(42, restored.MapState.VisitedNodeIds[2]);
		Assert.Equal(2, restored.Materials[101]);
		Assert.Equal(1, restored.Items[201]);
		Assert.Equal(1, restored.Equipment[301]);
		Assert.Contains("1:Gold:0:50", restored.SettlementClaimedRewardKeys);
		Assert.Equal(2, restored.SettlementCardPools.Count);
		Assert.Equal(0, restored.SettlementCardPools[0].SlotIndex);
		Assert.Equal(1002, restored.SettlementCardPools[0].CharacterId);
		Assert.Equal(new List<int> { 11002005, 21002004, 31002006 }, restored.SettlementCardPools[0].CandidateCardIds);
		Assert.Equal(2, restored.SettlementCardPools[1].SlotIndex);
		Assert.Equal(1003, restored.SettlementCardPools[1].CharacterId);
		Assert.Single(restored.SettlementCardClaims);
		Assert.Equal(0, restored.SettlementCardClaims[0].SlotIndex);
		Assert.Equal(21002004, restored.SettlementCardClaims[0].CardId);
		Assert.Equal((int)MapNodeType.NormalCombat, restored.SettlementSourceNodeType);
		Assert.Equal(1, restored.SettlementLossTier);
		Assert.Equal(0.57, restored.SettlementValueRatio, 4);
		Assert.True(restored.SettlementPanelClosed);
		Assert.True(restored.SuppressAbandonSettlementConfirm);
    }

    [Fact]
    public void RoundTrip_PreservesPostBattleBattlefield()
    {
        RunSaveData restored = RunSaveJson.Deserialize(RunSaveJson.Serialize(BuildSampleData()));

        RunPostBattleSave snapshot = restored.PostBattleBattlefield;
        Assert.NotNull(snapshot);
        Assert.Equal("F1-006", snapshot.LevelId);
        Assert.Equal("M-F1-001", snapshot.MapId);
        Assert.Equal(4, snapshot.Round);
        Assert.Equal(2, snapshot.SelectedSlotIndex);

        Assert.Equal(2, snapshot.Units.Count);
        Assert.Equal(12, snapshot.Units[0].UnitId);
        Assert.Equal(0, snapshot.Units[0].SlotIndex);
        Assert.Equal(2, snapshot.Units[0].Q);
        Assert.Equal(-1, snapshot.Units[0].R);
        Assert.Equal((int)BattlefieldPresence.Active, snapshot.Units[0].Presence);
        Assert.Equal(17, snapshot.Units[0].Hp);
        Assert.Equal("F1-006-M01", snapshot.Units[1].InstanceKey);
        Assert.Equal((int)BattlefieldPresence.Defeated, snapshot.Units[1].Presence);

        Assert.Equal(2, snapshot.GroundObjects.Count);
        Assert.Equal("ground-1", snapshot.GroundObjects[0].InstanceId);
        Assert.Equal((int)ItemSpatialShape.Line, snapshot.GroundObjects[0].SpatialShape);
        Assert.Equal(3, snapshot.GroundObjects[0].DamageAmount);
        Assert.Equal(1, snapshot.GroundObjects[0].Q);
        Assert.Equal(-2, snapshot.GroundObjects[0].R);
        Assert.Equal((int)GroundObjectKind.Trap, snapshot.GroundObjects[1].Kind);
        Assert.Equal((int)EntryTriggerMode.EveryEntry, snapshot.GroundObjects[1].TriggerMode);

        Assert.Single(snapshot.Loadouts);
        Assert.Equal("双手剑", snapshot.Loadouts[0].LeftHand.DefinitionId);
        Assert.Null(snapshot.Loadouts[0].RightHand);
        Assert.Equal(2, snapshot.Loadouts[0].Items.Count);
        Assert.Null(snapshot.Loadouts[0].Items[0]);
        Assert.Equal("picked-1", snapshot.Loadouts[0].Items[1].InstanceId);
    }

    [Fact]
    public void Deserialize_EmptyOrNullJson_ReturnsNull()
    {
        Assert.Null(RunSaveJson.Deserialize(null));
        Assert.Null(RunSaveJson.Deserialize(""));
    }
}
