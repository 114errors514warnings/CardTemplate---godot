using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator.Battlefield;
using Xunit;

public class BattlefieldFoundationTests
{
    private static BattleMapDefinition Definition() => new()
    {
        MapId = "test", Radius = 4, Seed = 1234,
        PlayerSpawnCoords = new() { new() { Q = 0, R = 0 }, new() { Q = 1, R = 0 }, new() { Q = 0, R = 1 } },
        PlayerCharacterIds = new() { 1001, 1002, 1002 }, MonsterIds = new() { 3001, 3002 },
        MinEnemyDistance = 2, RandomObstacleCount = 4,
        RandomItemCount = 10, RandomItemDefinitions = new() { "stone", "potion" }
    };
    private static BattleBoard OpenBoard() => BattleDeploymentService.Generate(Definition()).Board;
    private static (BattleBoard board, BattleOccupancyService occupancy, BattleMovementService movement, BattleUnitPlacement actor) MovementFixture()
    {
        var board = new BattleBoard(new[] { new BattleCell(new(0, 0)), new BattleCell(new(1, 0)), new BattleCell(new(0, 1)), new BattleCell(new(-1, 0)) });
        var occupancy = new BattleOccupancyService(board);
        var actor = new BattleUnitPlacement(new TestUnitInstance { UniqueInGameId = 7, HP = 10, Energy = 3 }, "player", BattlefieldRole.Player, 3);
        Assert.True(occupancy.TryPlace(actor, new(0, 0), out _));
        return (board, occupancy, new BattleMovementService(board, occupancy), actor);
    }

    [Fact]
    public void Layout_RoundTripsPositiveAndNegativeCells()
    {
        for (int q = -15; q <= 15; q++) for (int r = -15; r <= 15; r++)
        {
            var coord = new AxialHex(q, r); var center = BattleHexLayout.Center(coord, 42);
            Assert.Equal(coord, BattleHexLayout.Pick(center.X, center.Y, 42));
        }
    }
    [Fact]
    public void Layout_SixNeighborsShareEdges()
    {
        var origin = new AxialHex(0, 0);
        var neighbors = BattleHexLayout.Neighbors(origin).ToList();
        Assert.Equal(6, neighbors.Distinct().Count());
        foreach (var cell in neighbors)
        {
            Assert.Equal(1, AxialHex.Distance(origin, cell));
            var center = BattleHexLayout.Center(cell, 42);
            Assert.Equal(Math.Sqrt(3) * 42, Math.Sqrt(center.X * center.X + center.Y * center.Y), 8);
        }
    }
    [Fact]
    public void Json_RejectsUnknownMembersAndInvalidEnum()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => BattleMapDefinition.Parse("{\"TypoRadius\":1}"));
        Assert.Throws<System.Text.Json.JsonException>(() => BattleMapDefinition.Parse("{\"CellOverrides\":[{\"Kind\":\"Door\"}]}"));
        Assert.Throws<ArgumentException>(() => BattleMapDefinition.Parse("{}"));
    }
    [Fact]
    public void Json_ActualProjectFixtureLoadsAndGenerates()
    {
        var definition = BattleMapDefinition.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "FoundationMap.json")));
        var map = BattleDeploymentService.Generate(definition);
        Assert.Equal(331, map.Board.Cells.Count);
        Assert.Equal(6, map.EnemyCoords.Count);
        Assert.Equal(2, map.Board.Cells[new AxialHex(-1, 0)].Items.Count);
    }
    [Fact]
    public void Deployment_RepeatsWithoutMutatingDefinition()
    {
        var definition = Definition();
        var a = BattleDeploymentService.Generate(definition); var b = BattleDeploymentService.Generate(definition);
        Assert.Equal(a.EnemyCoords, b.EnemyCoords);
        Assert.Equal(a.Board.Cells.Where(x => x.Value.Kind == BattleCellKind.Obstacle).Select(x => x.Key),
            b.Board.Cells.Where(x => x.Value.Kind == BattleCellKind.Obstacle).Select(x => x.Key));
        Assert.Empty(definition.CellOverrides);
        a.Board.ChangeTerrain(new(0, 0), BattleCellKind.Normal, BattleSurface.Pit, false);
        Assert.True(b.Board.IsWalkable(new(0, 0)));
    }
    [Fact]
    public void Deployment_ThreePlayersPairwiseAdjacentAndEnemiesSeparated()
    {
        var map = BattleDeploymentService.Generate(Definition());
        foreach (var a in map.PlayerCoords) foreach (var b in map.PlayerCoords.Where(x => x != a)) Assert.Equal(1, AxialHex.Distance(a, b));
        Assert.Equal(map.EnemyCoords.Count, map.EnemyCoords.Distinct().Count());
        foreach (var enemy in map.EnemyCoords) foreach (var player in map.PlayerCoords) Assert.True(AxialHex.Distance(enemy, player) >= 2);
    }
    [Fact]
    public void Deployment_ExcludesSealedPocket()
    {
        var definition = Definition(); definition.Radius = 6; definition.RandomObstacleCount = 0;
        var pocket = new AxialHex(3, 0);
        definition.CellOverrides = BattleHexLayout.Neighbors(pocket).Select(x => new BattleCellData { Q = x.Q, R = x.R, Kind = BattleCellKind.Obstacle }).ToList();
        definition.MonsterIds = Enumerable.Repeat(3001, 50).ToList();
        var map = BattleDeploymentService.Generate(definition);
        Assert.True(map.Board.IsWalkable(pocket));
        Assert.DoesNotContain(pocket, map.EnemyCoords);
    }
    [Fact]
    public void Deployment_RejectsEnclosedPlayerAreaAfterBoundedAttempts()
    {
        var definition = Definition(); definition.RandomObstacleCount = 0; definition.MaxGenerationAttempts = 2;
        for (int q = -2; q <= 2; q++) for (int r = -2; r <= 2; r++)
            if (AxialHex.Distance(new(q, r), new(0, 0)) == 2)
                definition.CellOverrides.Add(new BattleCellData { Q = q, R = r, Kind = BattleCellKind.Obstacle });
        Assert.Contains("2 次", Assert.Throws<InvalidOperationException>(() => BattleDeploymentService.Generate(definition)).Message);
    }
    [Fact]
    public void Deployment_RejectsDuplicateOverridesAndExcessObstacles()
    {
        var definition = Definition(); definition.CellOverrides = new() { new() { Q = 3 }, new() { Q = 3 } };
        Assert.Throws<ArgumentException>(() => BattleDeploymentService.Generate(definition));
        definition.CellOverrides.Clear(); definition.RandomObstacleCount = 10000;
        Assert.Throws<ArgumentException>(() => BattleDeploymentService.Generate(definition));
    }
    [Fact]
    public void Objects_PileAcceptsManyInstancesButRejectsTriggerAndDuplicate()
    {
        var board = OpenBoard(); var coord = new AxialHex(0, 0);
        var item = new GroundObject("x", "stone", GroundObjectKind.Item);
        Assert.True(board.TryAddObject(coord, item, out _));
        Assert.True(board.TryAddObject(coord, new("y", "sword", GroundObjectKind.Equipment), out _));
        Assert.False(board.TryAddObject(coord, item, out _));
        Assert.False(board.TryAddObject(coord, new("t", "trap", GroundObjectKind.Trap), out _));
        Assert.True(board.TryRemoveObject(coord, "x", out var removed)); Assert.Same(item, removed);
        Assert.Contains(board.Cells[coord].Items, x => x.InstanceId == "y");
    }
    [Fact]
    public void Movement_CostsOneAndIndicesChangeTogether()
    {
        var (board, occupancy, movement, actor) = MovementFixture();
        Assert.True(movement.TryMove(7, new(1, 0), out _));
        Assert.Null(occupancy.At(new(0, 0))); Assert.Same(actor, occupancy.At(new(1, 0)));
        Assert.Equal(2, actor.Unit.Energy); Assert.Equal(1, actor.MovesUsedThisTurn);
    }
    [Theory]
    [InlineData(2, 0)] [InlineData(9, 9)] [InlineData(0, 0)]
    public void Movement_InvalidDistanceLeavesResourcesUnchanged(int q, int r)
    {
        var f = MovementFixture(); Assert.False(f.movement.TryMove(7, new(q, r), out _));
        Assert.Equal(3, f.actor.Unit.Energy); Assert.Equal(0, f.actor.MovesUsedThisTurn); Assert.Equal(new AxialHex(0, 0), f.actor.Coord);
    }
    [Fact]
    public void Movement_BlockedAndInsufficientResourcesRejectWithoutSideEffects()
    {
        var f = MovementFixture();
        Assert.True(f.occupancy.TryPlace(new BattleUnitPlacement(new TestUnitInstance { UniqueInGameId = 8, HP = 1 }, "enemy", BattlefieldRole.Enemy, 0), new(1, 0), out _));
        Assert.False(f.movement.TryMove(7, new(1, 0), out _));
        f.board.ChangeTerrain(new(0, 1), BattleCellKind.Obstacle, BattleSurface.Ground, true);
        Assert.False(f.movement.TryMove(7, new(0, 1), out _));
        f.actor.Unit.Energy = 0; Assert.False(f.movement.TryMove(7, new(-1, 0), out _));
        Assert.Equal(0, f.actor.MovesUsedThisTurn);
    }
    [Fact]
    public void Movement_EquipmentImmediatelyChangesLimitWithoutResetOrDoubleCounting()
    {
        var f = MovementFixture(); Assert.True(f.movement.TryMove(7, new(1, 0), out _));
        f.actor.SetEquipmentMoveModifier("two-handed", 2); f.actor.SetEquipmentMoveModifier("two-handed", 2);
        Assert.Equal(5, f.actor.EffectiveMovesPerTurn); Assert.Equal(4, f.actor.RemainingMoves);
        f.actor.SetEquipmentMoveModifier("two-handed", -3); Assert.Equal(0, f.actor.RemainingMoves);
        Assert.False(f.movement.TryMove(7, new(0, 0), out _));
        f.actor.SetEquipmentMoveModifier("two-handed", 0); Assert.Equal(2, f.actor.RemainingMoves);
        f.movement.StartPlayerTurn(); Assert.Equal(3, f.actor.RemainingMoves);
    }
    [Fact]
    public void Movement_DeathsReleaseCellsBeforeNextCommand()
    {
        var f = MovementFixture(); var unit = new TestUnitInstance { UniqueInGameId = 8, HP = 1 };
        f.occupancy.TryPlace(new(unit, "enemy", BattlefieldRole.Enemy, 0), new(1, 0), out _);
        unit.HP = 0; Assert.True(f.movement.TryMove(7, new(1, 0), out _));
        Assert.Equal(BattlefieldPresence.Defeated, f.occupancy.Placements[8].Presence);
    }
    [Theory]
    [InlineData(EntryTriggerMode.Once, 1)] [InlineData(EntryTriggerMode.EveryEntry, 2)]
    public void Movement_TriggerEntryIsUniqueAndOnceOrPermanent(EntryTriggerMode mode, int expected)
    {
        var f = MovementFixture();
        f.board.TryAddObject(new(1, 0), new("t", "test", GroundObjectKind.Trap, mode), out _);
        var events = new List<BattlefieldEntry>(); f.movement.Entered += events.Add;
        Assert.True(f.movement.TryMove(7, new(1, 0), out _));
        Assert.True(f.movement.TryMove(7, new(0, 0), out _));
        Assert.True(f.movement.TryMove(7, new(1, 0), out _));
        Assert.Equal(expected, events.Count(x => x.Trigger != null));
        Assert.Equal(3, events.Select(x => x.EventId).Distinct().Count());
    }
    [Fact]
    public void Drops_UseNearestReceivingRingAndAllowUnitWithPile()
    {
        var f = MovementFixture(); var service = new GroundObjectPlacementService(f.board, f.occupancy, 12);
        Assert.True(service.TryDrop(new(0, 0), new("a", "sword", GroundObjectKind.Equipment), out var landed, out _));
        Assert.Equal(new AxialHex(0, 0), landed);
        f.board.TryAddObject(new(1, 0), new("t", "trap", GroundObjectKind.Trap), out _);
        Assert.True(service.TryDrop(new(1, 0), new("b", "potion", GroundObjectKind.Item), out landed, out _));
        Assert.Equal(1, AxialHex.Distance(new(1, 0), landed));
        Assert.False(service.TryPlaceTrap(7, new(0, 0), new("t2", "trap", GroundObjectKind.Trap), 1, out _));
        Assert.False(service.TryPlaceTrap(7, new(-1, 0), new("m", "machine", GroundObjectKind.Mechanism), 1, out _));
    }
    [Fact]
    public void Movement_NonPlayerTurnAndReentryAreRejected()
    {
        var f = MovementFixture(); f.movement.PlayerTurn = false;
        Assert.False(f.movement.TryMove(7, new(1, 0), out _));
        f.movement.PlayerTurn = true;
        f.movement.Entered += entry => Assert.False(f.movement.TryMove(7, new(0, 0), out _));
        Assert.True(f.movement.TryMove(7, new(1, 0), out _)); Assert.Equal(2, f.actor.Unit.Energy);
    }

    [Fact]
    public void Fan_RangeTwoMatchesApprovedAxialCells()
    {
        var cells = BattleRangeResolver.ResolveFanCells(new AxialHex(0, 0), new AxialHex(1, 0), 2);
        var expected = new[] { new AxialHex(0, 1), new AxialHex(0, 2), new AxialHex(1, 1), new AxialHex(1, 0),
            new AxialHex(2, 0), new AxialHex(2, -1), new AxialHex(2, -2), new AxialHex(1, -1) };
        Assert.Equal(expected.OrderBy(x => x.Q).ThenBy(x => x.R), cells.OrderBy(x => x.Q).ThenBy(x => x.R));
        Assert.DoesNotContain(new AxialHex(0, 0), cells);
    }

    [Fact]
    public void WeaponCsv_LoadsEnumDrivenWeaponDefinitions()
    {
        var weapons = BattleWeaponCatalog.LoadAll(useCache: false);
        Assert.Equal(WeaponAttackMode.ThrowSingle, weapons["魔典"].Mode);
        Assert.Equal(4, weapons["魔典"].AttackRange);
        Assert.Equal(0, weapons["弓"].DefenseValue);
        Assert.Equal(1, weapons["行军短剑"].MoveBonus);
    }

    [Fact]
    public void WeaponTrace_ThrowBypassesMiddleButRangedLineStopsAtFirstUnit()
    {
        var cells = new[] { new BattleCell(new(0, 0)), new BattleCell(new(1, 0)), new BattleCell(new(2, 0)), new BattleCell(new(3, 0)) };
        var board = new BattleBoard(cells); var occupancy = new BattleOccupancyService(board);
        var blocker = new BattleUnitPlacement(new TestUnitInstance { UniqueInGameId = 91, HP = 10 }, "blocker", BattlefieldRole.Player, 0);
        Assert.True(occupancy.TryPlace(blocker, new(1, 0), out _));
        var ranged = new WeaponAttackSpec("bow", 3, 2, WeaponAttackMode.RangedLine, 0);
        var thrown = new WeaponAttackSpec("tome", 3, 2, WeaponAttackMode.ThrowSingle, 0);
        Assert.Equal(new[] { new AxialHex(1, 0) }, BattleAttackTraceResolver.Resolve(board, occupancy, new(0, 0), new(3, 0), ranged));
        Assert.Equal(new[] { new AxialHex(3, 0) }, BattleAttackTraceResolver.Resolve(board, occupancy, new(0, 0), new(3, 0), thrown));
    }

    [Fact]
    public void EnemyPlanner_ThrowSinglePrefersLowestHealthThenNearest()
    {
        var cells = BattleRangeResolver.CellsWithinRange(new AxialHex(0, 0), 4).Select(x => new BattleCell(x));
        var board = new BattleBoard(cells); var occupancy = new BattleOccupancyService(board);
        var enemy = new BattleUnitPlacement(new TestUnitInstance { UniqueInGameId = 1, HP = 10 }, "enemy", BattlefieldRole.Enemy, 0);
        var near = new BattleUnitPlacement(new TestUnitInstance { UniqueInGameId = 2, HP = 3 }, "near", BattlefieldRole.Player, 0);
        var far = new BattleUnitPlacement(new TestUnitInstance { UniqueInGameId = 3, HP = 3 }, "far", BattlefieldRole.Player, 0);
        occupancy.TryPlace(enemy, new(0, 0), out _); occupancy.TryPlace(near, new(1, 0), out _); occupancy.TryPlace(far, new(3, 0), out _);
        var plan = EnemyIntentPlanner.Plan(board, occupancy, enemy, new[] { near, far }, null,
            new EnemyIntentSpec(WeaponAttackMode.ThrowSingle, 4, 0, EnemyActionOrder.MoveThenAttack, EnemyTargetPolicy.ThrowSingleLowestHealth), new Random(1));
        Assert.Same(near, plan.Target);
    }
}
