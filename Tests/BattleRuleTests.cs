using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator.Battlefield;
using Xunit;

/// <summary>关卡级战斗规则：枚举解析、关卡变量列、以及劫匪规则的选择时机。</summary>
public class BattleRuleTests
{
    // ── 枚举与关卡变量列 ──

    [Fact]
    public void Registry_Parse_EmptyOrDefault_MeansNoRule()
    {
        Assert.Empty(BattleRuleRegistry.Parse("", "test"));
        Assert.Empty(BattleRuleRegistry.Parse("   ", "test"));
        Assert.Empty(BattleRuleRegistry.Parse("Default", "test"));
        Assert.Equal(new[] { BattleRuleKind.BanditEscalation }, BattleRuleRegistry.Parse("BanditEscalation", "test"));
        Assert.Equal(new[] { BattleRuleKind.BanditEscalation }, BattleRuleRegistry.Parse("banditescalation", "test"));
    }

    [Fact]
    public void Registry_Parse_UnknownName_Throws()
    {
        Assert.Throws<ArgumentException>(() => BattleRuleRegistry.Parse("NoSuchRule", "F1-006"));
    }

    [Fact]
    public void Registry_Create_SkipsDefaultAndBuildsImplementations()
    {
        Assert.Empty(BattleRuleRegistry.Create(new[] { BattleRuleKind.Default }));
        IReadOnlyList<IBattleRule> rules = BattleRuleRegistry.Create(new[] { BattleRuleKind.BanditEscalation, BattleRuleKind.BanditEscalation });
        Assert.Single(rules);
        Assert.Equal(BattleRuleKind.BanditEscalation, rules[0].Kind);
    }

    [Fact]
    public void LevelCatalog_Parse_ReadsBattleRuleColumnAndRequiresConsistency()
    {
        string[] rows =
        {
            "M-F1-001,2001,NormalCombat,Mid,F1-X-M01,Monster,3115,-4,0,1;0,,,BanditEscalation",
            "M-F1-001,2001,NormalCombat,Mid,F1-X-M02,Monster,3116,-3,-1,,State=Steal:1,,BanditEscalation",
        };
        BattleLevelConfig config = BattleLevelCatalog.Parse("F1-X", rows);
        Assert.Equal(new[] { BattleRuleKind.BanditEscalation }, config.Rules);

        // 缺列（旧关卡）→ 无规则，不受影响。
        string[] legacy = { "M-F1-001,2001,NormalCombat,Low,F1-Y-M01,Monster,3101,-4,0,,," };
        Assert.Empty(BattleLevelCatalog.Parse("F1-Y", legacy).Rules);

        // 行间不一致 / 未知规则名 → 报错。
        string[] inconsistent = rows.Concat(new[] { "M-F1-001,2001,NormalCombat,Mid,F1-X-M03,Monster,3115,0,0,,,," }).ToArray();
        Assert.Throws<ArgumentException>(() => BattleLevelCatalog.Parse("F1-X", inconsistent));
        string[] unknown = { "M-F1-001,2001,NormalCombat,Mid,F1-Z-M01,Monster,3115,0,0,,,,NoSuchRule" };
        Assert.Throws<ArgumentException>(() => BattleLevelCatalog.Parse("F1-Z", unknown));
    }

    [Fact]
    public void LevelCatalog_ApplyMonstersTo_CarriesRulesIntoBattleDefinition()
    {
        var definition = new BattleMapDefinition();
        var level = new BattleLevelConfig
        {
            LevelId = "F1-X",
            Rules = new List<BattleRuleKind> { BattleRuleKind.BanditEscalation },
            Objects = new List<BattleLevelObject> { new("F1-X-M01", "Monster", "3115", -2, 0) },
        };

        BattleLevelCatalog.ApplyMonstersTo(definition, level);

        Assert.Equal(new[] { BattleRuleKind.BanditEscalation }, definition.Rules);
        Assert.Equal(new[] { 3115 }, definition.MonsterIds);
    }

    // ── 劫匪规则的意图选择 ──

    private static IntentSelectionContext Context(int enemyId, int batch, int hits, Random random, int[] candidates = null) => new()
    {
        Round = Math.Max(1, batch - 1),
        IntentBatch = batch,
        EnemyId = enemyId,
        Candidates = candidates ?? new[] { 0, 1, 2 },
        HitPlayerCount = hits,
        Random = random,
    };

    [Fact]
    public void BanditRule_EarlyBatchesUseLowAttack_ThresholdBatchForcesHighAttack()
    {
        var rule = new BanditEscalationRule();
        var random = new Random(1234);

        IntentSelectionContext first = Context(1, batch: 1, hits: 0, random);
        IntentSelectionContext second = Context(1, batch: 2, hits: 0, random);
        IntentSelectionContext third = Context(1, batch: 3, hits: 0, random);
        Assert.True(rule.TrySelectIntention(first));
        Assert.True(rule.TrySelectIntention(second));
        Assert.True(rule.TrySelectIntention(third));
        Assert.Equal(BanditEscalationRule.LowIntentIndex, first.SelectedIndex);
        Assert.Equal(BanditEscalationRule.LowIntentIndex, second.SelectedIndex);
        Assert.Equal(BanditEscalationRule.HighIntentIndex, third.SelectedIndex);
    }

    [Fact]
    public void BanditRule_AfterThresholdBatch_MixesLowAndHigh()
    {
        var rule = new BanditEscalationRule();
        var random = new Random(1234);
        var seen = new HashSet<int>();
        for (int i = 0; i < 12; i++)
        {
            IntentSelectionContext context = Context(1, batch: 4 + i, hits: 0, random);
            Assert.True(rule.TrySelectIntention(context));
            seen.Add(context.SelectedIndex);
        }
        Assert.Equal(new[] { 0, 1 }, seen.OrderBy(x => x));
    }

    [Fact]
    public void BanditRule_HitThreshold_TriggersFleeAndLocksIt()
    {
        var rule = new BanditEscalationRule();
        var random = new Random(1234);
        // 命中未达阈值：仍走攻击列。
        IntentSelectionContext beforeTrigger = Context(7, batch: 1, hits: BanditEscalationRule.FleeTriggerHits - 1, random);
        Assert.True(rule.TrySelectIntention(beforeTrigger));
        Assert.NotEqual(BanditEscalationRule.FleeIntentIndex, beforeTrigger.SelectedIndex);

        // 达阈值后应在有限批次内切逃跑（同一随机流，等同实战）。
        int fleeingBatch = -1;
        for (int batch = 2; batch <= 40 && fleeingBatch < 0; batch++)
        {
            IntentSelectionContext context = Context(7, batch, hits: BanditEscalationRule.FleeTriggerHits, random);
            Assert.True(rule.TrySelectIntention(context));
            if (context.SelectedIndex == BanditEscalationRule.FleeIntentIndex) fleeingBatch = batch;
        }
        Assert.True(fleeingBatch > 0, "达阈值后应在有限批次内切逃跑");

        // 锁定：之后每批次都是逃跑。
        for (int batch = fleeingBatch + 1; batch <= fleeingBatch + 3; batch++)
        {
            IntentSelectionContext context = Context(7, batch, hits: 0, random);
            Assert.True(rule.TrySelectIntention(context));
            Assert.Equal(BanditEscalationRule.FleeIntentIndex, context.SelectedIndex);
        }
    }

    [Fact]
    public void BanditRule_Contagion_AppliesFromNextBatchToAllBandits()
    {
        var rule = new BanditEscalationRule();
        var random = new Random(1234);
        int fleeingBatch = -1;
        for (int batch = 1; batch <= 20 && fleeingBatch < 0; batch++)
        {
            IntentSelectionContext a = Context(1, batch, hits: BanditEscalationRule.FleeTriggerHits, random);
            rule.TrySelectIntention(a);
            IntentSelectionContext b = Context(2, batch, hits: 0, random);
            rule.TrySelectIntention(b);
            if (a.SelectedIndex == BanditEscalationRule.FleeIntentIndex)
            {
                fleeingBatch = batch;
                // 同批次内另一只仍走攻击列（传染只在下一批次生效）。
                Assert.NotEqual(BanditEscalationRule.FleeIntentIndex, b.SelectedIndex);
            }
        }
        Assert.True(fleeingBatch > 0, "应有单位进入逃跑");

        IntentSelectionContext next = Context(2, fleeingBatch + 1, hits: 0, random);
        Assert.True(rule.TrySelectIntention(next));
        Assert.Equal(BanditEscalationRule.FleeIntentIndex, next.SelectedIndex);
    }

    [Fact]
    public void BanditRule_DoesNotInterveneWithoutThreeIntentColumns()
    {
        var rule = new BanditEscalationRule();
        IntentSelectionContext context = Context(1, batch: 5, hits: 9, new Random(1234), candidates: new[] { 0, 1 });
        Assert.False(rule.TrySelectIntention(context));
        Assert.Equal(-1, context.SelectedIndex);
    }
}
