// EnemyIntentDamageArgsTests.cs
// 覆盖怪物意图 Damage 段的取参口径（代码需求清单 P2-10.4）：
// 两元素 `1;<修正值>` = 伤害修正值（与旧卡牌战斗 ParseMonsterDamageTargetMode 同口径）；
// 三元素起 `1;<目标模式>;<修正值>` 取第 3 位；非 Damage 段不参与。
using CardSimulator.Battlefield;
using Xunit;

public class EnemyIntentDamageArgsTests
{
    [Theory]
    [InlineData(new[] { 1, -1 }, -1)]   // 3115 近战劫匪 低攻：5 - 1 = 4
    [InlineData(new[] { 1, 1 }, 1)]     // 3115 近战劫匪 高攻：5 + 1 = 6（F1-006 第 3 敌方回合爆发）
    [InlineData(new[] { 1, -5 }, -5)]   // 3114 借势格挡：18 - 5 = 13（修复前恒为 18）
    [InlineData(new[] { 1, -3 }, -3)]   // 3114 横扫：18 - 3 = 15（修复前恒为 18）
    [InlineData(new[] { 1, 2 }, 2)]   // 两元素：唯一额外参数 = 伤害修正值（例：3113 横扫 `1;2` = 攻 6 + 2 = 8）
    [InlineData(new[] { 1, 0 }, 0)]
    [InlineData(new[] { 1 }, 0)]
    public void GetDamageModifier_TwoElementForm_IsDamageModifier(int[] effect, int expected)
    {
        Assert.Equal(expected, EnemyIntentDamageArgs.GetDamageModifier(effect));
    }

    [Theory]
    [InlineData(new[] { 1, 1, -5 }, -5)] // 三元素：`<模式>;<修正值>`，模式不参与伤害
    [InlineData(new[] { 1, 2, 3 }, 3)]
    [InlineData(new[] { 1, 1, 0 }, 0)]
    public void GetDamageModifier_ThreeElementForm_TakesThirdArgument(int[] effect, int expected)
    {
        Assert.Equal(expected, EnemyIntentDamageArgs.GetDamageModifier(effect));
    }

    [Theory]
    [InlineData(new[] { 2, 1 })]   // 护盾段 `2;<修正值>` 不是 Damage，不参与伤害修正
    [InlineData(new[] { 3, 2, 1, 1 })]
    [InlineData(new[] { 0 })]
    [InlineData(new int[0])]
    [InlineData(null)]
    public void GetDamageModifier_NonDamageOrMissingArgs_IsZero(int[] effect)
    {
        Assert.Equal(0, EnemyIntentDamageArgs.GetDamageModifier(effect));
    }

    [Fact]
    public void ResolveDamageParams_FeedsEffectSystemWithModifierOnly()
    {
        Assert.Equal(new[] { -1 }, EnemyIntentDamageArgs.ResolveDamageParams(new[] { 1, -1 }));
        Assert.Equal(new[] { 6 }, EnemyIntentDamageArgs.ResolveDamageParams(new[] { 1, 2, 6 }));
        Assert.Equal(new[] { 2 }, EnemyIntentDamageArgs.ResolveDamageParams(new[] { 1, 2 }));
        Assert.Empty(EnemyIntentDamageArgs.ResolveDamageParams(new[] { 1 }));
        Assert.Empty(EnemyIntentDamageArgs.ResolveDamageParams(new[] { 1, 0 }));
        Assert.Empty(EnemyIntentDamageArgs.ResolveDamageParams(new[] { 2, 1 }));
        Assert.Empty(EnemyIntentDamageArgs.ResolveDamageParams(null));
    }

    /// <summary>
    /// 两元素 `1;2` 的编码规则（`MonsterValue.csv` 的 `Note` 列曾登记的疑点）：
    /// 唯一额外参数按伤害修正值读（= 攻击力 +2），与旧卡牌战斗一致；要表达「同意图共用目标」必须写三元素 `1;2;0`。
    /// 3003 / 3007 原先用两元素写法表达目标模式，已于 2026-09-28 改写为 `1;2;0`（9 月施工文档 §36），故本用例只锁定规则本身，不绑定具体怪物。
    /// </summary>
    [Fact]
    public void TwoElementForm_TreatsSoleArgumentAsModifier_NotTargetMode()
    {
        Assert.Equal(2, EnemyIntentDamageArgs.GetDamageModifier(new[] { 1, 2 }));
        Assert.Equal(0, EnemyIntentDamageArgs.GetDamageModifier(new[] { 1, 2, 0 }));
    }
}
