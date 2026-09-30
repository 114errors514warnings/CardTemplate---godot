// NormalEncounterCountPerActTests.cs
// 覆盖 P2-11（9 月施工文档 §45）：普通敌袭分档计数**按层（Act）独立**、
// 旧档遗留的全局计数按当前 Act 归入、按层计数可 JSON 往返。
using Xunit;

public class NormalEncounterCountPerActTests
{
    [Fact]
    public void Counts_AreIndependentPerAct()
    {
        RunMapStateSave state = new RunMapStateSave { Act = 1 };
        state.IncrementCurrentNormalEncounterCount();
        state.IncrementCurrentNormalEncounterCount();
        state.IncrementCurrentNormalEncounterCount();
        Assert.Equal(3, state.GetNormalEncounterCount(1));

        state.Act = 2;
        Assert.Equal(0, state.CurrentNormalEncounterCount);   // 进新层从第 1 场重算
        Assert.Equal(3, state.GetNormalEncounterCount(1));    // 上一层计数不受影响
    }

    [Fact]
    public void FourthEncounterInEachAct_ResolvesSameTier()
    {
        RunMapStateSave state = new RunMapStateSave { Act = 1 };
        for (int i = 0; i < 3; i++)   // 已打 3 场 → 接下来就是该层第 4 场
        {
            state.IncrementCurrentNormalEncounterCount();
        }

        Assert.Equal(3, state.CurrentNormalEncounterCount);
        StageDifficulty firstLayerTier = StageEncounterPicker.ResolveNormalCombatDifficultyByEncounterCount(state.CurrentNormalEncounterCount);
        Assert.Equal(StageDifficulty.Mid, firstLayerTier);

        state.Act = 2;
        Assert.Equal(0, state.CurrentNormalEncounterCount);   // 进第二层重新计数
        for (int i = 0; i < 3; i++)
        {
            state.IncrementCurrentNormalEncounterCount();
        }

        Assert.Equal(3, state.CurrentNormalEncounterCount);
        StageDifficulty secondLayerTier = StageEncounterPicker.ResolveNormalCombatDifficultyByEncounterCount(state.CurrentNormalEncounterCount);
        Assert.Equal(firstLayerTier, secondLayerTier);        // 第一层第 4 场与第二层第 4 场同档
        Assert.Equal(StageDifficulty.Mid, secondLayerTier);
    }

    [Fact]
    public void LegacyGlobalIndex_MigratesIntoCurrentAct_Once()
    {
        RunMapStateSave legacy = new RunMapStateSave { Act = 2, NormalEncounterIndex = 4 };
        legacy.MigrateLegacyNormalEncounterCount();

        Assert.Equal(4, legacy.GetNormalEncounterCount(2));   // 旧档按当前 Act 归入
        Assert.Equal(0, legacy.GetNormalEncounterCount(1));
        Assert.Equal(0, legacy.NormalEncounterIndex);         // 迁移后清零

        legacy.MigrateLegacyNormalEncounterCount();           // 幂等：再迁一次不叠加
        Assert.Equal(4, legacy.GetNormalEncounterCount(2));
    }

    [Fact]
    public void Counts_RoundTripThroughJson()
    {
        RunSaveData data = new RunSaveData { MapState = new RunMapStateSave { Act = 2 } };
        data.MapState.IncrementNormalEncounterCount(1);
        data.MapState.IncrementNormalEncounterCount(2);
        data.MapState.IncrementNormalEncounterCount(2);

        RunSaveData restored = RunSaveJson.Deserialize(RunSaveJson.Serialize(data));
        Assert.Equal(1, restored.MapState.GetNormalEncounterCount(1));
        Assert.Equal(2, restored.MapState.GetNormalEncounterCount(2));
    }
}
