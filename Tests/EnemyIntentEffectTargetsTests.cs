// EnemyIntentEffectTargetsTests.cs
// 覆盖怪物意图效果段的目标类型落地（代码需求清单 P2-25，验收 ⑤：6 个 `EffectTargetType` 分支 + AllyRange 半径）。
// 效果段写法 `<EffectType>;<EffectTargetType>;<参数…>`：目标按「施法方 ↔ 其余单位」的阵营口径解析，
// 施法怪物出 `AllEnemies` 打的是全体角色（不是「随机一名角色」），`Self` 只打自己。
using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;
using CardSimulator.Battlefield;
using Xunit;

public class EnemyIntentEffectTargetsTests
{
    /// <summary>假战场：施法怪 id 11 在 (0,0)、近旁友方怪 id 12 在 (0,1)、远处友方怪 id 13 在 (0,4)（半径 3 外）、
    /// 存活角色 id 1/2/3、已阵亡角色 id 4（HP 0，占位但绝不入选）。</summary>
    private sealed class Fixture
    {
        public BattleOccupancyService Occupancy { get; }
        public BattleUnitPlacement Source { get; }
        public BattleUnitPlacement FarAlly { get; }
        public BattleUnitPlacement DeadPlayer { get; }

        public Fixture()
        {
            var cells = new List<BattleCell>();
            for (int q = -5; q <= 5; q++)
                for (int r = Math.Max(-5, -q - 5); r <= Math.Min(5, -q + 5); r++)
                    cells.Add(new BattleCell(new AxialHex(q, r)));
            Occupancy = new BattleOccupancyService(new BattleBoard(cells));
            Source = Place(11, BattlefieldRole.Enemy, new AxialHex(0, 0));
            Place(12, BattlefieldRole.Enemy, new AxialHex(0, 1));
            FarAlly = Place(13, BattlefieldRole.Enemy, new AxialHex(0, 4));
            Place(1, BattlefieldRole.Player, new AxialHex(1, 0));
            Place(2, BattlefieldRole.Player, new AxialHex(2, 0));
            Place(3, BattlefieldRole.Player, new AxialHex(-3, 0));
            DeadPlayer = Place(4, BattlefieldRole.Player, new AxialHex(0, 2));
            DeadPlayer.Unit.HP = 0;
        }

        private BattleUnitPlacement Place(int id, BattlefieldRole role, AxialHex coord)
        {
            var placement = new BattleUnitPlacement(new TestUnitInstance { UniqueInGameId = id, HP = 10, Max_HP = 10 },
                "unit" + id, role, 0);
            Assert.True(Occupancy.TryPlace(placement, coord, out string error));
            Assert.Empty(error);
            return placement;
        }

        /// <summary>本意图的索敌目标（= 最近角色）。</summary>
        public BattleUnitPlacement Selected => Occupancy.Placements[1];

        public List<int> ResolveIds(EffectTargetType type, EnemyTargetPolicy policy = EnemyTargetPolicy.Nearest,
            int radius = 0) => EnemyIntentEffectTargets
                .Resolve(type, Source, Selected, Occupancy.Placements.Values, policy, radius)
                .Select(x => x.UnitId).ToList();
    }

    [Theory]
    [InlineData(new[] { 3, 0, 2, 2 }, EffectTargetType.Auto)]           // 3118 狂暴压制：`3;0;2;2`
    [InlineData(new[] { 3, 1, 6, 1 }, EffectTargetType.Self)]           // 3102 / 3123「自身加攻」
    [InlineData(new[] { 3, 2, 2, 2 }, EffectTargetType.SelectedTarget)] // 3124「伴侣共鸣」对敌段
    [InlineData(new[] { 3, 3, 1, 3 }, EffectTargetType.AllEnemies)]     // 3125「导师威压（全体）」
    [InlineData(new[] { 3, 4, 6, 1 }, EffectTargetType.AllUnits)]
    [InlineData(new[] { 3, 5, 6, 1 }, EffectTargetType.AllAllies)]      // 3124「伴侣共鸣」友方段
    [InlineData(new[] { 3, 99, 1, 3 }, EffectTargetType.Auto)]          // 未登记值退回 Auto
    [InlineData(new[] { 3 }, EffectTargetType.Auto)]                    // 缺段退回 Auto
    [InlineData(new int[0], EffectTargetType.Auto)]
    [InlineData(null, EffectTargetType.Auto)]
    public void ParseTargetType_ReadsSecondSegmentWithAutoFallback(int[] effect, EffectTargetType expected)
    {
        Assert.Equal(expected, EnemyIntentEffectTargets.ParseTargetType(effect));
    }

    [Theory]
    [InlineData(EffectTargetType.Self, new[] { 11 })]
    [InlineData(EffectTargetType.SelectedTarget, new[] { 1 })]
    [InlineData(EffectTargetType.Auto, new[] { 1 })]
    [InlineData(EffectTargetType.AllEnemies, new[] { 1, 2, 3 })]   // 角色 4 已阵亡，不入选
    [InlineData(EffectTargetType.AllAllies, new[] { 12, 13 })]     // 不含施法者自身
    [InlineData(EffectTargetType.AllUnits, new[] { 12, 13, 1, 2, 3, 11 })]
    public void Resolve_SixEffectTargetTypes_SelectFieldUnits(EffectTargetType type, int[] expectedIds)
    {
        Assert.Equal(expectedIds, new Fixture().ResolveIds(type));
    }

    [Fact]
    public void Resolve_AllEnemies_HitsEveryPlayerNotOnlyTheNearestOne()
    {
        var fixture = new Fixture();
        List<int> targets = fixture.ResolveIds(EffectTargetType.AllEnemies);
        Assert.Equal(3, targets.Count);                        // 修复前只打 1 人
        Assert.DoesNotContain(fixture.Source.UnitId, targets); // 且不会打回怪物自己
    }

    [Fact]
    public void Resolve_Self_LandsOnTheMonsterItself()
    {
        var fixture = new Fixture();
        List<int> ids = EnemyIntentEffectTargets.Resolve(EffectTargetType.Self, fixture.Source, fixture.Selected,
            fixture.Occupancy.Placements.Values).Select(x => x.UnitId).ToList();
        Assert.Equal(new[] { 11 }, ids);
    }

    [Theory]
    [InlineData(0, new int[0])]   // 半径 0 = 只剩自身（调用方按需补自己）
    [InlineData(1, new[] { 12 })]
    [InlineData(3, new[] { 12 })] // 远处友方在 (0,4) → 距离 4，半径 3 外
    [InlineData(4, new[] { 12, 13 })]
    public void Resolve_AllyRangePolicy_AppliesRadiusToAlliesOnly(int radius, int[] expectedIds)
    {
        Assert.Equal(expectedIds, new Fixture().ResolveIds(EffectTargetType.AllAllies,
            EnemyTargetPolicy.AllyRange, radius));
    }

    [Fact]
    public void Resolve_AllyRangePolicy_DoesNotShrinkEnemyTargets()
    {
        // 半径只约束友方侧：`3;3;…`（全体角色）+ `AllyRange` 仍是 3 名角色，避免改坏既有行。
        Assert.Equal(new[] { 1, 2, 3 }, new Fixture().ResolveIds(EffectTargetType.AllEnemies,
            EnemyTargetPolicy.AllyRange, 0));
    }

    [Fact]
    public void Resolve_CompanionResonanceShape_BuffsSelfAndNearbyAllyOnly()
    {
        // 3124「伴侣共鸣」`3;1;6;1|3;5;6;1`（P2-25 验收 ③）：段 1 只给女方自己、段 2 只给半径 3 内友方。
        var fixture = new Fixture();
        int[][] intention = { new[] { 3, 1, 6, 1 }, new[] { 3, 5, 6, 1 } };
        var perSegment = intention.Select(segment => fixture.ResolveIds(
            EnemyIntentEffectTargets.ParseTargetType(segment), EnemyTargetPolicy.AllyRange, 3)).ToList();
        Assert.Equal(new[] { 11 }, perSegment[0]);
        Assert.Equal(new[] { 12 }, perSegment[1]);
        Assert.All(perSegment.SelectMany(x => x), id => Assert.True(id >= 11, $"目标 {id} 不应是角色"));
    }

    [Fact]
    public void Resolve_DeadUnits_AreNeverTargets()
    {
        var fixture = new Fixture();
        fixture.Source.Unit.HP = 0;
        Assert.Empty(fixture.ResolveIds(EffectTargetType.Self));   // 施法者已死 → 无目标
        Assert.Equal(new[] { 1, 2, 3 }, fixture.ResolveIds(EffectTargetType.AllEnemies));
    }

    [Fact]
    public void Resolve_MissingSelectedTarget_FallsBackToSource()
    {
        // 无索敌目标时（例如目标不可达）：`Auto` / `SelectedTarget` 退回施法者自身，与卡牌口径一致。
        var fixture = new Fixture();
        List<int> ids = EnemyIntentEffectTargets.Resolve(EffectTargetType.Auto, fixture.Source, null,
            fixture.Occupancy.Placements.Values).Select(x => x.UnitId).ToList();
        Assert.Equal(new[] { 11 }, ids);
    }

    [Fact]
    public void Resolve_UnknownEffectTargetTypeValue_FallsBackToAuto()
    {
        // 直接传越界值（不经过解析）：`TryResolve` 的 default 分支 = Auto = 索敌目标。
        Assert.Equal(new[] { 1 }, new Fixture().ResolveIds((EffectTargetType)99));
    }

    [Fact]
    public void Resolve_NullSourceOrEmptyField_IsEmpty()
    {
        var fixture = new Fixture();
        Assert.Empty(EnemyIntentEffectTargets.Resolve(EffectTargetType.Self, null, null,
            fixture.Occupancy.Placements.Values));
        Assert.Empty(EnemyIntentEffectTargets.Resolve(EffectTargetType.AllAllies, fixture.Source, null, null));
        Assert.Empty(EnemyIntentEffectTargets.Resolve(EffectTargetType.AllEnemies, fixture.Source, null,
            Array.Empty<BattleUnitPlacement>()));
    }
}
