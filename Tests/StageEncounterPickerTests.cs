// StageEncounterPickerTests.cs
// 覆盖 Stage 遭遇选取纯逻辑：难度规则优先 / 无规则加权随机 / 空表返回 null。
using System;
using System.Collections.Generic;
using Xunit;

public class StageEncounterPickerTests
{
    private static StageEncounterRow Row(string name, StageDifficulty difficulty, int dropTableId, int weight = 1, string monsterIds = "3001")
    {
        string[] parts = monsterIds.Split('|', StringSplitOptions.RemoveEmptyEntries);
        int[] ids = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            ids[i] = int.Parse(parts[i]);
        }

        return new StageEncounterRow
        {
            Name = name,
            Difficulty = difficulty,
            DropTableId = dropTableId,
            Weight = weight,
            MonsterIds = ids,
        };
    }

    [Fact]
    public void ResolveNormalCombatDifficulty_ByEncounterCount_FollowsRule()
    {
        Assert.Equal(StageDifficulty.Low, StageEncounterPicker.ResolveNormalCombatDifficultyByEncounterCount(0));
        Assert.Equal(StageDifficulty.Low, StageEncounterPicker.ResolveNormalCombatDifficultyByEncounterCount(2));
        Assert.Equal(StageDifficulty.Mid, StageEncounterPicker.ResolveNormalCombatDifficultyByEncounterCount(3));
        Assert.Equal(StageDifficulty.Mid, StageEncounterPicker.ResolveNormalCombatDifficultyByEncounterCount(4));
        Assert.Equal(StageDifficulty.High, StageEncounterPicker.ResolveNormalCombatDifficultyByEncounterCount(5));
        Assert.Equal(StageDifficulty.High, StageEncounterPicker.ResolveNormalCombatDifficultyByEncounterCount(100));
    }

    [Fact]
    public void Pick_WithDifficultyRule_PrefersMatchingTier()
    {
        List<StageEncounterRow> rows = new List<StageEncounterRow>
        {
            Row("低", StageDifficulty.Low, 1),
            Row("中", StageDifficulty.Mid, 2),
            Row("高", StageDifficulty.High, 3),
        };

        Random rng = new Random(42);
        for (int i = 0; i < 30; i++)
        {
            StageEncounterRow picked = StageEncounterPicker.Pick(rows, StageDifficulty.High, rng);
            Assert.NotNull(picked);
            Assert.Equal(StageDifficulty.High, picked.Difficulty);
        }
    }

    [Fact]
    public void Pick_NoRule_WeightedRandomRespectsWeight()
    {
        List<StageEncounterRow> rows = new List<StageEncounterRow>
        {
            Row("A", StageDifficulty.Any, 1, weight: 9),
            Row("B", StageDifficulty.Any, 2, weight: 1),
        };

        Random rng = new Random(7);
        int countA = 0;
        int total = 200;
        for (int i = 0; i < total; i++)
        {
            StageEncounterRow picked = StageEncounterPicker.Pick(rows, null, rng);
            if (picked.Name == "A")
            {
                countA++;
            }
        }

        // 9:1 权重，200 次里 A 应显著占优
        Assert.True(countA > total * 0.8, $"权重随机异常：A 命中 {countA}/{total}");
    }

    [Fact]
    public void Pick_EmptyRows_ReturnsNull()
    {
        Random rng = new Random(1);
        Assert.Null(StageEncounterPicker.Pick(new List<StageEncounterRow>(), StageDifficulty.Low, rng));
        Assert.Null(StageEncounterPicker.Pick(null, null, rng));
    }

    [Fact]
    public void Pick_CombatTypeWithoutMonsters_IsTreatedAsNoConfig()
    {
        StageEncounterRow emptyMonsterRow = new StageEncounterRow
        {
            Name = "空怪行",
            Difficulty = StageDifficulty.Low,
            Weight = 1,
            MonsterIds = Array.Empty<int>(),
        };

        Random rng = new Random(5);
        Assert.Null(StageEncounterPicker.Pick(new List<StageEncounterRow> { emptyMonsterRow }, StageDifficulty.Low, rng, requireUsableForCombat: true));
    }
}
