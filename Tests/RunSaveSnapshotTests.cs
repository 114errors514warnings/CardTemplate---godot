// RunSaveSnapshotTests.cs
// 覆盖 v2 存档：System.Text.Json IncludeFields 后，public 字段（位置/种子/HP/卡级数/状态机字段）
// 都能完整往返，避免“继续游戏回起点 / 数据丢字段”类问题。
using System.Collections.Generic;
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
        data.SettlementEncounterName = "普通敌袭-中等";
        data.SettlementDropTableId = 2001;
        data.SettlementCandidateCardIds = new List<int> { 11002005, 21002004, 31002006 };
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
        Assert.Equal(2001, restored.SettlementDropTableId);
        Assert.Equal(3, restored.SettlementCandidateCardIds.Count);
        Assert.Equal(11002005, restored.SettlementCandidateCardIds[0]);
        Assert.Equal(42, restored.MapState.VisitedNodeIds[2]);
    }

    [Fact]
    public void Deserialize_EmptyOrNullJson_ReturnsNull()
    {
        Assert.Null(RunSaveJson.Deserialize(null));
        Assert.Null(RunSaveJson.Deserialize(""));
    }
}
