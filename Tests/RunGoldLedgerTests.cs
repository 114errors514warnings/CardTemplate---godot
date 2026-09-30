using System.Collections.Generic;
using Xunit;

/// <summary>怪物窃取金币的账本规则（按实例分开：余额不足扣到归零、同 ID 多只各自记账、结算只返还被击杀实例）。</summary>
public class RunGoldLedgerTests
{
    [Fact]
    public void Steal_EnoughGold_DeductsAndRecordsPerInstance()
    {
        var run = new RunSaveData { Gold = 5 };

        int stolen = RunGoldLedger.Steal(run, "F1-006-M01", 3115, 3);

        Assert.Equal(3, stolen);
        Assert.Equal(2, run.Gold);
        StolenGoldEntry entry = RunGoldLedger.Find(run, "F1-006-M01");
        Assert.NotNull(entry);
        Assert.Equal(3115, entry.MonsterId);
        Assert.Equal(3, entry.Amount);
    }

    [Fact]
    public void Steal_NotEnoughGold_DeductsToZero()
    {
        var run = new RunSaveData { Gold = 2 };

        // 口径（2026-09-26）：余额不足以支付本次窃取额时**扣到归零**（返回实扣额，不是"一分不扣"）。
        Assert.Equal(2, RunGoldLedger.Steal(run, "F1-006-M01", 3115, 3));

        Assert.Equal(0, run.Gold);
        StolenGoldEntry entry = RunGoldLedger.Find(run, "F1-006-M01");
        Assert.NotNull(entry);
        Assert.Equal(2, entry.Amount);                                     // 记账额 = 实扣额
    }

    [Fact]
    public void Steal_ZeroGold_NeverGoesNegative()
    {
        var run = new RunSaveData { Gold = 0 };

        Assert.Equal(0, RunGoldLedger.Steal(run, "F1-006-M01", 3115, 1));
        Assert.Equal(0, run.Gold);
        Assert.Empty(run.StolenGoldFromMonsters);                          // 无实扣 → 不建空条目
    }

    [Fact]
    public void Steal_SameMonsterIdOnTwoInstances_RecordsSeparately()
    {
        var run = new RunSaveData { Gold = 10 };

        RunGoldLedger.Steal(run, "F1-006-M01", 3115, 2);   // 同一 ID 的第一只
        RunGoldLedger.Steal(run, "F1-006-M02", 3115, 3);   // 同一 ID 的第二只
        RunGoldLedger.Steal(run, "F1-006-M01", 3115, 1);   // 第一只再偷一次

        Assert.Equal(4, run.Gold);
        Assert.Equal(2, run.StolenGoldFromMonsters.Count);                 // 两条独立记录，不合并
        Assert.Equal(3, RunGoldLedger.Find(run, "F1-006-M01").Amount);
        Assert.Equal(3, RunGoldLedger.Find(run, "F1-006-M02").Amount);
        Assert.Equal(6, RunGoldLedger.TotalStolen(run));
    }

    [Fact]
    public void RefundDefeated_ReturnsOnlyKilledInstancesAndClearsTheirRecords()
    {
        var run = new RunSaveData { Gold = 10 };
        RunGoldLedger.Steal(run, "F1-006-M01", 3115, 3);   // 被杀
        RunGoldLedger.Steal(run, "F1-006-M02", 3115, 2);   // 存活

        int refunded = RunGoldLedger.RefundDefeated(run, new List<string> { "F1-006-M01", "不存在" });

        Assert.Equal(3, refunded);
        Assert.Equal(8, run.Gold);                                        // 10 − 3 − 2 + 3(返还)
        Assert.Null(RunGoldLedger.Find(run, "F1-006-M01"));                // 被击杀 → 已返还并清账
        Assert.NotNull(RunGoldLedger.Find(run, "F1-006-M02"));              // 存活 → 留在账本
    }

    [Fact]
    public void RefundDefeated_NoKilledInstances_ChangesNothing()
    {
        var run = new RunSaveData { Gold = 10 };
        RunGoldLedger.Steal(run, "F1-006-M01", 3115, 3);

        Assert.Equal(0, RunGoldLedger.RefundDefeated(run, new List<string>()));

        Assert.Equal(7, run.Gold);
        Assert.Equal(3, RunGoldLedger.Find(run, "F1-006-M01").Amount);
    }

    [Fact]
    public void Clear_DropsRemainingRecordsButKeepsGold()
    {
        var run = new RunSaveData { Gold = 10 };
        RunGoldLedger.Steal(run, "F1-006-M01", 3115, 3);

        RunGoldLedger.Clear(run);

        Assert.Empty(run.StolenGoldFromMonsters);
        Assert.Equal(7, run.Gold);
    }
}
