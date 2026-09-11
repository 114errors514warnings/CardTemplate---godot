// WarriorCardSpatialTests.cs
// 覆盖勇士专属战场卡牌的空间规格解析与 BattleRangeResolver 形状。
using System.Collections.Generic;
using System.Linq;
using CardSimulator.Battlefield;
using Xunit;

public class WarriorCardSpatialTests
{
    [Fact]
    public void ParseFields_ParsesSingleCard()
    {
        string[] fields = "11001001,破空斩,Attack,1,Damage,对目标格敌人额外造成2点伤害,2;2,,,Single,Range=1,".Split(',');
        CardSpatialSpec spec = CardSpatialSpec.ParseFields(fields);
        Assert.Equal(11001001, spec.CardId);
        Assert.Equal(CardSpatialShape.Single, spec.Shape);
        Assert.Equal(1, spec.MaxRange);
    }

    [Fact]
    public void ParseFields_ParsesBurstArgs()
    {
        string[] fields = "11001002,横扫,Attack,2,Damage,范围伤害,2;1,,,Burst,Range=1;Radius=1,".Split(',');
        CardSpatialSpec spec = CardSpatialSpec.ParseFields(fields);
        Assert.Equal(CardSpatialShape.Burst, spec.Shape);
        Assert.Equal(1, spec.Radius);
        Assert.Equal(1, spec.MaxRange);
    }

    [Fact]
    public void Neighbors_ReturnsSixCells()
    {
        List<AxialHex> neighbors = BattleRangeResolver.Neighbors(new AxialHex(0, 0)).ToList();
        Assert.Equal(6, neighbors.Count);
        Assert.All(neighbors, n => Assert.Equal(1, BattleRangeResolver.Distance(n, new AxialHex(0, 0))));
    }

    [Fact]
    public void CellsWithinRange_Radius1_HasSeven()
    {
        HashSet<AxialHex> cells = BattleRangeResolver.CellsWithinRange(new AxialHex(0, 0), 1);
        Assert.Equal(7, cells.Count);
    }

    [Fact]
    public void Burst_AffectsCenterAndNeighbors()
    {
        var spec = new CardSpatialSpec { CardId = 11001002, Shape = CardSpatialShape.Burst, Radius = 1 };
        HashSet<AxialHex> affected = BattleRangeResolver.ResolveAffectedCells(new AxialHex(0, 0), new AxialHex(2, 0), spec);
        Assert.Contains(new AxialHex(2, 0), affected);
        Assert.Contains(new AxialHex(3, 0), affected); // 目标格的东邻
        Assert.Equal(7, affected.Count);
    }

    [Fact]
    public void Line_AffectsAlongChosenDirection()
    {
        var spec = new CardSpatialSpec { CardId = 11001003, Shape = CardSpatialShape.Line, Length = 2 };
        // 施法者 (0,0) → 目标东邻 (1,0)：直线应含 (1,0) 与 (2,0)
        HashSet<AxialHex> affected = BattleRangeResolver.ResolveAffectedCells(new AxialHex(0, 0), new AxialHex(1, 0), spec);
        Assert.Contains(new AxialHex(1, 0), affected);
        Assert.Contains(new AxialHex(2, 0), affected);
        Assert.Equal(2, affected.Count);
    }

    [Fact]
    public void ParseFields_ParsesFanCard()
    {
        string[] fields = "11001002,横扫,Attack,2,Damage,扇形伤害,2;1,,,Fan,Range=1,".Split(',');
        CardSpatialSpec spec = CardSpatialSpec.ParseFields(fields);
        Assert.Equal(CardSpatialShape.Fan, spec.Shape);
        Assert.Equal(1, spec.MaxRange);
    }

    [Fact]
    public void Fan_AffectsOnlySelectedDirectionAndItsTwoAdjacentNeighbors()
    {
        var spec = new CardSpatialSpec { CardId = 11001002, Shape = CardSpatialShape.Fan, MaxRange = 1 };
        HashSet<AxialHex> affected = BattleRangeResolver.ResolveAffectedCells(new AxialHex(0, 0), new AxialHex(0, 1), spec);
        Assert.Equal(3, affected.Count);
        Assert.Contains(new AxialHex(0, 1), affected);
        Assert.Contains(new AxialHex(1, 0), affected);
        Assert.Contains(new AxialHex(-1, 1), affected);
        Assert.DoesNotContain(new AxialHex(0, 0), affected);
        Assert.DoesNotContain(new AxialHex(0, -1), affected);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(0, -1)]
    [InlineData(-1, 0)]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    public void PickLineDirection_ResolvesEveryAxialNeighborIncludingVerticalScreenDirections(int q, int r)
    {
        AxialHex direction = new AxialHex(q, r);
        Assert.Equal(direction, BattleRangeResolver.PickLineDirection(new AxialHex(0, 0), direction));
        Assert.Equal(direction, BattleRangeResolver.PickLineDirection(new AxialHex(0, 0), new AxialHex(q * 3, r * 3)));
    }

    [Fact]
    public void SelfMoveAndTrap_ReturnSingleTargetForAffected()
    {
        var self = new CardSpatialSpec { CardId = 21001004, Shape = CardSpatialShape.SelfMove, MaxRange = 2 };
        var trap = new CardSpatialSpec { CardId = 21001005, Shape = CardSpatialShape.Trap, MaxRange = 1, TrapId = "test_trap" };
        HashSet<AxialHex> selfAffected = BattleRangeResolver.ResolveAffectedCells(new AxialHex(0, 0), new AxialHex(1, 0), self);
        HashSet<AxialHex> trapAffected = BattleRangeResolver.ResolveAffectedCells(new AxialHex(0, 0), new AxialHex(1, 0), trap);
        Assert.Single(selfAffected);
        Assert.Contains(new AxialHex(1, 0), trapAffected);
    }
}
