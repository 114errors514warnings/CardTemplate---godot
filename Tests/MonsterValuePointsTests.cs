// MonsterValuePointsTests.cs
// 覆盖奖励折损口径（代码需求清单 P2-10.3 / 单位数值平衡标准 §2.4）：
// 综合价值点数 = 面板 HP + K × 意图加权价值（K = 1.0），比例 = 击败者价值 ÷ 部署者价值（分母含逃跑者）。
// 算例逐行取自 F1-006 §4.1 的折损档位表。
using System.Collections.Generic;
using Xunit;

public class MonsterValuePointsTests
{
    // 数据镜像：Monster.csv 的 MAX_HP + MonsterValue.csv 的意图价值（3115 近战劫匪 / 3116 远程劫匪）。
    private static readonly Dictionary<int, int[]> IntentTable = new Dictionary<int, int[]>
    {
        [3115] = new[] { 4, 6, 0 },
        [3116] = new[] { 3, 4, 0 },
    };

    private static readonly Dictionary<int, int> MaxHpTable = new Dictionary<int, int>
    {
        [3115] = 35,
        [3116] = 16,
    };

    private static double Value(int monsterId) => MonsterValuePoints.GetValuePoints(MaxHpTable[monsterId], IntentTable[monsterId]);

    [Fact]
    public void GetWeightedIntentValue_DividesByIntentCount_IncludingZeroEffectIntent()
    {
        Assert.Equal(3.3333, MonsterValuePoints.GetWeightedIntentValue(new[] { 4, 6, 0 }), 4);
        Assert.Equal(2.5, MonsterValuePoints.GetWeightedIntentValue(new[] { 3, 2 }), 4);
        Assert.Equal(0, MonsterValuePoints.GetWeightedIntentValue(new int[0]));
        Assert.Equal(0, MonsterValuePoints.GetWeightedIntentValue(null));
    }

    [Fact]
    public void GetValuePoints_AddsPanelHpAndIntentValue_WithKEqualsOne()
    {
        Assert.Equal(1.0, MonsterValuePoints.IntentWeightK);
        Assert.Equal(38.3333, MonsterValuePoints.GetValuePoints(35, new[] { 4, 6, 0 }), 4);
        Assert.Equal(18.3333, MonsterValuePoints.GetValuePoints(16, new[] { 3, 4, 0 }), 4);
        Assert.Equal(35, MonsterValuePoints.GetValuePoints(35, null), 4);
    }

    [Theory]
    [InlineData(2, 3, 3)] // 全歼 131.67 → 100% → 3 份
    [InlineData(2, 2, 3)] // 113.33 → 86.1% → 3 份
    [InlineData(2, 1, 3)] // 95.00 → 72.2% → 3 份
    [InlineData(1, 3, 3)] // 93.33 → 70.9%（贴阈值，仍全额）
    [InlineData(2, 0, 2)] // 76.67 → 58.2% → 2 份
    [InlineData(1, 2, 2)] // 75.00 → 57.0% → 2 份
    [InlineData(1, 1, 1)] // 56.67 → 43.0% → 1 份
    [InlineData(0, 3, 1)] // 55.00 → 41.8% → 1 份
    [InlineData(0, 0, 1)] // 全体离场 → 0%，至少保留 1 份
    public void GetCardRewardCount_MatchesF1_006LossTable(int defeatedMelee, int defeatedRanged, int expectedCount)
    {
        List<int> deployed = new List<int> { 3115, 3115, 3116, 3116, 3116 };
        List<int> defeated = new List<int>();
        for (int i = 0; i < defeatedMelee; i++) defeated.Add(3115);
        for (int i = 0; i < defeatedRanged; i++) defeated.Add(3116);

        double ratio = MonsterValuePoints.GetDefeatRatio(deployed, defeated, Value);

        Assert.Equal(expectedCount, MonsterValuePoints.GetCardRewardCount(ratio));
    }

    [Fact]
    public void GetDefeatRatio_F1_006_OneMeleeTwoRanged_IsFiftySevenPercent()
    {
        double ratio = MonsterValuePoints.GetDefeatRatio(new List<int> { 3115, 3115, 3116, 3116, 3116 }, new List<int> { 3115, 3116, 3116 }, Value);

        Assert.Equal(0.5696, ratio, 4);
    }

    [Fact]
    public void GetDefeatRatio_KeepsFleeingEnemiesInDenominator()
    {
        // 只击败 1 只近战劫匪，另两只远程逃跑：分母仍含逃跑者的价值。
        double ratio = MonsterValuePoints.GetDefeatRatio(new List<int> { 3115, 3116, 3116 }, new List<int> { 3115 }, Value);

        Assert.Equal(0.5111, ratio, 4);
    }

    [Fact]
    public void GetDefeatRatio_NoDeployedValue_IsTreatedAsFullReward()
    {
        Assert.Equal(1.0, MonsterValuePoints.GetLossRatio(0, 0));
        Assert.Equal(1.0, MonsterValuePoints.GetDefeatRatio(new List<int>(), new List<int>(), Value));
        Assert.Equal(1.0, MonsterValuePoints.GetDefeatRatio(null, null, Value));
    }

    [Fact]
    public void GetLossTier_UsesStrictUpperAndInclusiveMiddleThresholds()
    {
        Assert.Equal(0, MonsterValuePoints.GetLossTier(0.7001));
        Assert.Equal(1, MonsterValuePoints.GetLossTier(0.7000));
        Assert.Equal(1, MonsterValuePoints.GetLossTier(0.5000));
        Assert.Equal(2, MonsterValuePoints.GetLossTier(0.4999));
        Assert.Equal(0, MonsterValuePoints.GetLossTier(1.0));
    }

    [Fact]
    public void GetCardRewardCountFromTier_IsTheInverseOfGetLossTier()
    {
        Assert.Equal(3, MonsterValuePoints.GetCardRewardCountFromTier(0));
        Assert.Equal(2, MonsterValuePoints.GetCardRewardCountFromTier(1));
        Assert.Equal(1, MonsterValuePoints.GetCardRewardCountFromTier(2));
        Assert.Equal(1, MonsterValuePoints.GetCardRewardCountFromTier(9));
    }

    // ── 折损分流：生存关豁免（§7.4） / 爪牙不计入分子分母（§7.4 / §7.5） ──

    [Theory]
    [InlineData("Survival", true)]
    [InlineData("survival", true)]
    [InlineData(" Survival ", true)]
    [InlineData("NormalCombat", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSurvivalLevelType_MatchesOnlySurvival(string levelType, bool expected)
    {
        Assert.Equal(expected, MonsterValuePoints.IsSurvivalLevelType(levelType));
    }

    [Fact]
    public void GetDefeatRatio_IgnoresMinionsInBothNumeratorAndDenominator()
    {
        // 全歼非爪牙 + 2 只存活爪牙：爪牙既不加分子也不加分母 → 仍是全额 3 份。
        List<MonsterValuePoints.EnemyValueSample> samples = new List<MonsterValuePoints.EnemyValueSample>
        {
            new MonsterValuePoints.EnemyValueSample(3115, true),
            new MonsterValuePoints.EnemyValueSample(3116, true),
            new MonsterValuePoints.EnemyValueSample(3116, false, true),
            new MonsterValuePoints.EnemyValueSample(3116, false, true),
        };

        double ratio = MonsterValuePoints.GetDefeatRatio(samples, Value);

        Assert.Equal(1.0, ratio);
        Assert.Equal(3, MonsterValuePoints.GetCardRewardCount(ratio));
    }

    [Fact]
    public void GetDefeatRatio_OnlyMinionsOrNoSamples_IsTreatedAsFullReward()
    {
        List<MonsterValuePoints.EnemyValueSample> onlyMinions = new List<MonsterValuePoints.EnemyValueSample>
        {
            new MonsterValuePoints.EnemyValueSample(3116, false, true),
        };

        Assert.Equal(1.0, MonsterValuePoints.GetDefeatRatio(onlyMinions, Value)); // 没有可计奖单位 → 不折损
        Assert.Equal(1.0, MonsterValuePoints.GetDefeatRatio((List<MonsterValuePoints.EnemyValueSample>)null, Value));
    }

    [Fact]
    public void GetDefeatRatio_SampleOverload_MatchesIdListOverload()
    {
        List<MonsterValuePoints.EnemyValueSample> samples = new List<MonsterValuePoints.EnemyValueSample>
        {
            new MonsterValuePoints.EnemyValueSample(3115, true),
            new MonsterValuePoints.EnemyValueSample(3115, false),
            new MonsterValuePoints.EnemyValueSample(3116, false),
        };

        Assert.Equal(
            MonsterValuePoints.GetDefeatRatio(new List<int> { 3115, 3115, 3116 }, new List<int> { 3115 }, Value),
            MonsterValuePoints.GetDefeatRatio(samples, Value),
            4);
    }
}
