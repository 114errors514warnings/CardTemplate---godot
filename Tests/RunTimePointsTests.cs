// RunTimePointsTests.cs
// 时间点系统（P0-3：精度 0.1、进程只增不减、Schema 2 → 3 迁移）与休息结算
//（[篝火休息与食物](../../README/玩法说明文档/系统规则/营地系统/篝火休息与食物.md) §二 / §四）的纯逻辑单测。
using System.Collections.Generic;
using Xunit;

public class RunTimePointsTests
{
    [Theory]
    [InlineData(0f, 1)]
    [InlineData(3.9f, 1)]
    [InlineData(4f, 2)]
    [InlineData(8.1f, 3)]
    public void DayIndex_FollowsFourPointBoundaries(float total, int expected) =>
        Assert.Equal(expected, RunTimePoints.DayIndex(total));

    [Fact]
    public void Quantize_SnapsToTenthsAndRejectsNegative()
    {
        Assert.Equal(0.3f, RunTimePoints.Quantize(0.30000001f), 5); // 累加漂移被对齐
        Assert.Equal(0.3f, RunTimePoints.Quantize(0.26f), 5);      // 21 → 四舍五入到 0.1 步长
        Assert.Equal(0f, RunTimePoints.Quantize(-1f));              // 进程不可为负
    }

    [Fact]
    public void RemainingToday_TracksDayBudget()
    {
        Assert.Equal(4f, RunTimePoints.RemainingToday(0f), 5);
        Assert.Equal(1f, RunTimePoints.RemainingToday(3f), 5);
        Assert.Equal(4f, RunTimePoints.RemainingToday(4f), 5); // 每满 4 点进入新的一天 → 满额
        Assert.Equal(2.7f, RunTimePoints.RemainingToday(5.3f), 5);
    }

    [Fact]
    public void TrySpend_AccumulatesInTenths_AndRejectsOverdraftAndNegativeWrites()
    {
        var state = new RunMapStateSave();
        Assert.True(state.TrySpendTimePoints(RunTimePoints.MoveCost, out _));
        Assert.Equal(0.3f, state.TimePoints, 5);
        Assert.Equal(3.7f, state.RemainingToday, 5);
        Assert.False(state.PendingRestDay); // 没跨日界 → 不待休息

        // 时间点只能消耗、不能回复：负向写入整笔拒绝，进程不动。
        Assert.False(state.TryAddTimePoints(-0.1f, out string negative));
        Assert.Contains("不能回复", negative);
        Assert.Equal(0.3f, state.TimePoints, 5);

        // 当天剩余 0.2 < 移动 0.3：整笔拒绝（不允许透支）。
        var tight = new RunMapStateSave { TimePoints = 3.8f };
        Assert.False(tight.CanSpendTimePoints(RunTimePoints.MoveCost));
        Assert.False(tight.TrySpendTimePoints(RunTimePoints.MoveCost, out string overdraft));
        Assert.Contains("时间点不足", overdraft);
        Assert.Equal(3.8f, tight.TimePoints, 5);
        Assert.Equal(0.2f, tight.RemainingToday, 5);
    }

    [Fact]
    public void CrossingDayBoundary_FlagsPendingRestAndRecordsFinishedDayRemaining()
    {
        var state = new RunMapStateSave { TimePoints = 3.7f };
        Assert.True(state.TrySpendTimePoints(RunTimePoints.MoveCost, out _)); // 3.7 → 4.0：这一天耗尽
        Assert.Equal(4f, state.TimePoints, 5);
        Assert.True(state.PendingRestDay);
        Assert.Equal(0.3f, state.RestRemainingTimePoints, 5); // "这一天"的剩余 = 跨日界前剩余
        Assert.Equal(4f, state.RemainingToday, 5);            // 新一天的满额（但必须先休息）

        // 已待休息时再进营地（主动结束当天同一条入口）：不覆盖那一刻记下的剩余。
        state.BeginRestDay();
        Assert.Equal(0.3f, state.RestRemainingTimePoints, 5);
    }

    [Fact]
    public void AdvanceToNextDay_IsMonotonicAndVoidsRemaining()
    {
        var state = new RunMapStateSave { TimePoints = 1.4f };
        state.BeginRestDay();
        Assert.Equal(2.6f, state.RestRemainingTimePoints, 5); // 1.4 → 当天剩余 2.6
        state.AdvanceToNextDay();
        Assert.Equal(4f, state.TimePoints, 5);               // 补齐到次日边界（只增不减）
        Assert.Equal(2, state.CurrentDay);
        Assert.Equal(4f, state.RemainingToday, 5);
    }

    [Fact]
    public void RestHealRatio_MatchesDesignFormula()
    {
        Assert.Equal(0.10f, RunTimePoints.RestHealRatio(0f, 0), 5);  // 耗尽 → 基础 10%
        Assert.Equal(0.20f, RunTimePoints.RestHealRatio(2f, 0), 5);  // 剩一半 → +10%
        Assert.Equal(0.30f, RunTimePoints.RestHealRatio(4f, 0), 5);  // 剩满 → +20%
        Assert.Equal(0.40f, RunTimePoints.RestHealRatio(4f, 5), 5);  // 5 点饱食度 ×2%
        Assert.Equal(0.50f, RunTimePoints.RestHealRatio(4f, 99), 5); // 饱食度上限 10 点 → 封顶 +20%
    }

    [Fact]
    public void RestResolver_RotationScalesHealByTwoThirds()
    {
        RunSaveData run = BuildRun(30, 40, 50);
        run.MapState.TimePoints = 2f; // 主动结束当天：当天剩余 2 → 基础比例 10% + 20% × 2/4 = 20%
        run.MapState.BeginRestDay();
        Assert.Equal(2f, run.MapState.RestRemainingTimePoints, 5);
        List<int> healed = RunRestResolver.Apply(run, RunWatchMode.Rotation, 0, 0);
        Assert.Equal(new[] { 4, 6, 7 }, healed); // 20% × 2/3：30→4、40→5.33、50→6.67（向上取整）
        Assert.Equal(new[] { 19, 26, 32 }, new[] { run.CharacterSlots[0].CurrentHp, run.CharacterSlots[1].CurrentHp, run.CharacterSlots[2].CurrentHp });
        Assert.Equal(4f, run.MapState.TimePoints, 5); // 推进到次日边界（2 → 4）
        Assert.Equal(2, run.MapState.CurrentDay);
        Assert.False(run.MapState.PendingRestDay);
    }

    [Fact]
    public void RestResolver_SingleWatch_TakesWatcherAndKeepsOthersFull()
    {
        RunSaveData run = BuildRun(30, 30, 30);
        run.MapState.TimePoints = 2f; // 剩余 2 → 基础比例 20%
        run.MapState.BeginRestDay();
        Assert.Equal(6, RunRestResolver.PreviewHeal(run, 0, RunWatchMode.None, 1, 0)); // 30 × 20% = 6（预览与结算同源）
        Assert.Equal(0, RunRestResolver.PreviewHeal(run, 1, RunWatchMode.Single, 1, 0)); // 守夜者不回复
        List<int> healed = RunRestResolver.Apply(run, RunWatchMode.Single, 1, 0);
        Assert.Equal(new[] { 6, 0, 6 }, healed);
        Assert.Equal(new[] { 21, 15, 21 }, new[] { run.CharacterSlots[0].CurrentHp, run.CharacterSlots[1].CurrentHp, run.CharacterSlots[2].CurrentHp });
    }

    [Fact]
    public void RestResolver_CapsAtMaxHp_AndAdvancesToNextDay()
    {
        RunSaveData run = BuildRun(100, 100, 100);
        foreach (RunCharacterSlotSave slot in run.CharacterSlots) slot.CurrentHp = 99; // 只差 1 点 → 回复被最大生命截断
        run.MapState.TimePoints = 3.9f; // 距日界 0.1 → 基础比例 10% + 20% × 0.1/4 = 10.5%
        run.MapState.BeginRestDay();
        List<int> healed = RunRestResolver.Apply(run, RunWatchMode.None, 0, 0);
        Assert.Equal(new[] { 1, 1, 1 }, healed); // 10.5% = 11 点，但只剩 1 点空位
        Assert.Equal(new[] { 100, 100, 100 }, new[] { run.CharacterSlots[0].CurrentHp, run.CharacterSlots[1].CurrentHp, run.CharacterSlots[2].CurrentHp });
        Assert.Equal(4f, run.MapState.TimePoints, 5); // 推进到日界（3.9 → 4）
        Assert.Equal(2, run.MapState.CurrentDay);
        Assert.Equal(4f, run.MapState.RemainingToday, 5);
    }

    [Fact]
    public void Migration_UpgradesLegacyTimePointsToSchema3()
    {
        RunSaveData data = new RunSaveData { SchemaVersion = 2 };
        data.MapState.TimePoints = 5;                // 旧档 int 语义
        data.MapState.RestRemainingTimePoints = -3f; // 异常负值 → 归零
        data.MigrateToCurrentSchema();
        Assert.Equal(RunSaveData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Equal(5f, data.MapState.TimePoints, 5);
        Assert.Equal(0f, data.MapState.RestRemainingTimePoints, 5);
        Assert.Equal(2, data.MapState.CurrentDay);
        Assert.Equal(3f, data.MapState.RemainingToday, 5);
        Assert.False(data.MapState.PendingRestDay); // 5 点不在日界上 → 不产生强制休息

        // 旧档正停在日界（int 语义下很常见）时补一次待休息，否则那一天的耗尽是断的。
        RunSaveData onBoundary = new RunSaveData { SchemaVersion = 2 };
        onBoundary.MapState.TimePoints = 4;
        onBoundary.MigrateToCurrentSchema();
        Assert.True(onBoundary.MapState.PendingRestDay);
        Assert.Equal(0f, onBoundary.MapState.RestRemainingTimePoints, 5);
    }

    [Fact]
    public void Format_IsOneDecimalAndCarriesRoundsForEvents()
    {
        Assert.Equal("0.5", RunTimePoints.Format(0.5f));
        Assert.Equal("3.0", RunTimePoints.Format(3f));
        Assert.Equal("1.0 时间点（10 回合）", RunTimePoints.FormatWithRounds(1f));
        Assert.Equal("第 2 天 · 剩余 3.0 / 4.0", RunTimePoints.FormatDayAndRemaining(5f));
    }

    /// <summary>构造一个本局样本：每名角色半血（留出足够的回复空间，避免被最大生命截断干扰读值）。</summary>
    private static RunSaveData BuildRun(params int[] maxHps)
    {
        var run = new RunSaveData();
        foreach (int maxHp in maxHps)
        {
            run.CharacterSlots.Add(new RunCharacterSlotSave { CharacterId = 1002, MaxHp = maxHp, CurrentHp = maxHp / 2 });
        }

        return run;
    }
}
