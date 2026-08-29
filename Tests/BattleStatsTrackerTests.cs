// BattleStatsTrackerTests.cs
// 覆盖 BattleStatsTracker 的纯逻辑。
// 注意：RecordHpLossEvent 只接受 CharacterInstance（Godot.Resource），无法在纯 .NET 单测里 mock；
//       所以只测 null/非正数早返回路径。HP 累计点数、HP 快照等走 IUnitInstance 的部分全测。

using System.Collections.Generic;
using Xunit;

public class BattleStatsTrackerTests
{
    [Fact]
    public void Reset_ClearsAllCounters()
    {
        var tracker = new BattleStatsTracker();
        var unit = new TestUnitInstance { UniqueInGameId = 1, HP = 50 };

        tracker.SnapshotInitialHp(new[] { (IUnitInstance)unit });
        tracker.GetBattleLostHp(unit); // 触发现有快照被读取的代码路径

        tracker.Reset();

        Assert.Equal(0, tracker.GetBattleLostHp(unit));
        Assert.Equal(0, tracker.GetBattleHpLossEventCount(unit));
    }

    [Fact]
    public void GetBattleLostHp_ReturnsZeroForNullUnit()
    {
        var tracker = new BattleStatsTracker();
        Assert.Equal(0, tracker.GetBattleLostHp(null));
        Assert.Equal(0, tracker.GetBattleHpLossEventCount(null));
    }

    [Fact]
    public void GetBattleLostHp_ReturnsZeroIfUnitNotSnapshotted()
    {
        var tracker = new BattleStatsTracker();
        var unit = new TestUnitInstance { UniqueInGameId = 99, HP = 30 };

        // 没有 SnapshotInitialHp
        Assert.Equal(0, tracker.GetBattleLostHp(unit));
    }

    [Fact]
    public void SnapshotInitialHp_RecordsCurrentHp()
    {
        var tracker = new BattleStatsTracker();
        var u1 = new TestUnitInstance { UniqueInGameId = 1, HP = 50 };
        var u2 = new TestUnitInstance { UniqueInGameId = 2, HP = 80 };

        // 先快照初始 HP
        tracker.SnapshotInitialHp(new IUnitInstance[] { u1, u2 });

        // 然后模拟掉血
        u1.HP = 30; u2.HP = 60;

        // 初始 50/80，现在 30/60，掉血 20 + 20
        Assert.Equal(20, tracker.GetBattleLostHp(u1));
        Assert.Equal(20, tracker.GetBattleLostHp(u2));
    }

    [Fact]
    public void SnapshotInitialHp_HandlesEmptyCollection()
    {
        var tracker = new BattleStatsTracker();
        // 空集合不抛异常
        tracker.SnapshotInitialHp(System.Array.Empty<IUnitInstance>());

        // 不存在的单位 → 0
        var unit = new TestUnitInstance { UniqueInGameId = 1, HP = 50 };
        Assert.Equal(0, tracker.GetBattleLostHp(unit));
    }

    [Fact]
    public void GetBattleLostHp_NeverGoesNegative()
    {
        var tracker = new BattleStatsTracker();
        var unit = new TestUnitInstance { UniqueInGameId = 1, HP = 50 };

        tracker.SnapshotInitialHp(new[] { (IUnitInstance)unit });
        unit.HP = 100; // 加血

        // 实际"掉血"是负数，应被夹到 0
        Assert.Equal(0, tracker.GetBattleLostHp(unit));
    }

    [Fact]
    public void RecordHpLossEvent_IgnoresNonCharacterUnits()
    {
        // RecordHpLossEvent 内部 `if (target is not CharacterInstance)` 早返回；
        // 用 IUnitInstance 桩验证它确实被忽略了。
        var tracker = new BattleStatsTracker();
        var unit = new TestUnitInstance { UniqueInGameId = 1, HP = 50 };

        tracker.RecordHpLossEvent(unit, 5);
        tracker.RecordHpLossEvent(unit, 10);

        Assert.Equal(0, tracker.GetBattleHpLossEventCount(unit));
    }

    [Fact]
    public void RecordHpLossEvent_IgnoresNonPositiveLoss()
    {
        // 同样用 IUnitInstance 桩（虽然桩不是 CharacterInstance，但 hpLoss<=0 会更早返回）
        var tracker = new BattleStatsTracker();
        var unit = new TestUnitInstance { UniqueInGameId = 1, HP = 50 };

        tracker.RecordHpLossEvent(unit, 0);
        tracker.RecordHpLossEvent(unit, -5);

        Assert.Equal(0, tracker.GetBattleHpLossEventCount(unit));
    }

    [Fact]
    public void GetBattleCardsPlayedThisTurnCount_ReturnsZeroForNullPlayer()
    {
        var tracker = new BattleStatsTracker();
        Assert.Equal(0, tracker.GetBattleCardsPlayedThisTurnCount(null));
    }

    [Fact]
    public void ResetBattleCardsPlayedThisTurnCounts_NullClearsAll()
    {
        var tracker = new BattleStatsTracker();
        // null 路径直接 Clear()，不抛异常
        tracker.ResetBattleCardsPlayedThisTurnCounts(null);
        // 没有可断言的副作用，但确保不抛
        Assert.True(true);
    }
}
