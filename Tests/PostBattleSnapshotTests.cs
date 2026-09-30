// PostBattleSnapshotTests.cs
// 覆盖「战后位置落档」的纯逻辑（战斗结算后战场操作交互案 §七 4 改口径）：
// 地面 / 随身物件的编解码、快照 → 单位布局匹配、占位服务按快照重建索引。
// 会话级导出 / 还原与宿主重建战场由 `--run-flow-ui-smoke` 端到端覆盖（那里才有真实关卡数据）。
using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;
using CardSimulator.Battlefield;
using Xunit;

public class PostBattleSnapshotTests
{
    private static BattleBoard OpenBoard(int radius = 2) =>
        new BattleBoard(BattleRangeResolver.CellsWithinRange(new AxialHex(0, 0), radius).Select(x => new BattleCell(x)));

    private static BattleUnitPlacement Placement(int id, BattlefieldRole role, string name)
        => new(new TestUnitInstance { UniqueInGameId = id, HP = 10 }, name, role, 1);

    [Fact]
    public void GroundObject_RoundTripsEveryField()
    {
        var trap = new GroundObject("trap-1", "尖刺陷阱", GroundObjectKind.Trap, EntryTriggerMode.EveryEntry);
        var thrown = new GroundObject("stone-1", "石头", GroundObjectKind.Item)
        {
            SpatialShape = ItemSpatialShape.Line,
            ItemMaxRange = 3,
            ItemLength = 3,
            DamageAmount = 3,
        };
        var weapon = new GroundObject("w-1", "长枪", GroundObjectKind.Equipment,
            EntryTriggerMode.Once, handsRequired: 2, attackRange: 3, moveBonus: -1);

        RunGroundObjectSave trapSave = PostBattleSnapshotCodec.ToSave(trap, new AxialHex(2, -1));
        Assert.Equal(2, trapSave.Q);
        Assert.Equal(-1, trapSave.R);
        Assert.Equal((int)GroundObjectKind.Trap, trapSave.Kind);
        GroundObject restoredTrap = PostBattleSnapshotCodec.ToGroundObject(trapSave);
        Assert.Equal(EntryTriggerMode.EveryEntry, restoredTrap.TriggerMode);
        Assert.True(restoredTrap.IsTrigger);

        GroundObject restoredThrown = PostBattleSnapshotCodec.ToGroundObject(PostBattleSnapshotCodec.ToSave(thrown));
        Assert.Equal(ItemSpatialShape.Line, restoredThrown.SpatialShape);
        Assert.Equal(3, restoredThrown.ItemMaxRange);
        Assert.Equal(3, restoredThrown.ItemLength);
        Assert.Equal(3, restoredThrown.DamageAmount);
        Assert.True(restoredThrown.NeedsTarget);

        GroundObject restoredWeapon = PostBattleSnapshotCodec.ToGroundObject(PostBattleSnapshotCodec.ToSave(weapon));
        Assert.Equal(2, restoredWeapon.HandsRequired);
        Assert.Equal(3, restoredWeapon.AttackRange);
        Assert.Equal(-1, restoredWeapon.MoveBonus);

        Assert.Null(PostBattleSnapshotCodec.ToGroundObject(null));
        Assert.Null(PostBattleSnapshotCodec.ToSave(null));
    }

    [Fact]
    public void RestoreGroundObjects_ClearsStaleObjectsAndRebuildsTheSnapshot()
    {
        BattleBoard board = OpenBoard();
        Assert.True(board.TryAddObject(new AxialHex(1, 0), new GroundObject("stale-item", "地图随机道具", GroundObjectKind.Item), out _));
        Assert.True(board.TryAddObject(new AxialHex(0, 1), new GroundObject("stale-trap", "地图随机陷阱", GroundObjectKind.Trap), out _));

        var keptItem = new RunGroundObjectSave { InstanceId = "kept-item", DefinitionId = "治疗药剂", Kind = (int)GroundObjectKind.Item, Q = 1, R = -1 };
        var keptTrap = new RunGroundObjectSave
        {
            InstanceId = "kept-trap", DefinitionId = "尖刺陷阱", Kind = (int)GroundObjectKind.Trap,
            TriggerMode = (int)EntryTriggerMode.EveryEntry, Q = 0, R = 0,
        };
        PostBattleSnapshotCodec.RestoreGroundObjects(board, new List<RunGroundObjectSave> { keptItem, keptTrap }, out string warning);

        Assert.Equal("", warning);
        // 重建战场的地面内容一律以快照为准：地图随机生成的道具 / 陷阱都不再出现。
        Assert.Empty(board.Cells[new AxialHex(1, 0)].Items);
        Assert.Null(board.Cells[new AxialHex(0, 1)].Trigger);
        Assert.Equal("kept-item", board.Cells[new AxialHex(1, -1)].Items.Single().InstanceId);
        Assert.Equal("kept-trap", board.Cells[new AxialHex(0, 0)].Trigger.InstanceId);
    }

    [Fact]
    public void RestoreGroundObjects_ReportsFailuresWithoutThrowing()
    {
        BattleBoard board = OpenBoard();
        var offBoard = new RunGroundObjectSave { InstanceId = "a", DefinitionId = "石头", Kind = (int)GroundObjectKind.Item, Q = 9, R = 9 };
        PostBattleSnapshotCodec.RestoreGroundObjects(board, new List<RunGroundObjectSave> { offBoard }, out string warning);
        Assert.Contains("未能按快照放回", warning);
    }

    [Fact]
    public void ResolveUnitLayout_MatchesPlayersBySlotAndOtherRolesByOrderOrKey()
    {
        BattleBoard board = OpenBoard();
        var occupancy = new BattleOccupancyService(board);
        var p0 = Placement(11, BattlefieldRole.Player, "重剑手");
        var p1 = Placement(12, BattlefieldRole.Player, "重剑手2");
        var enemy = Placement(21, BattlefieldRole.Enemy, "堕魔守卫");
        Assert.True(occupancy.TryPlace(p0, new AxialHex(0, 0), out _));
        Assert.True(occupancy.TryPlace(p1, new AxialHex(1, 0), out _));
        Assert.True(occupancy.TryPlace(enemy, new AxialHex(2, 0), out _));

        var snapshot = new RunPostBattleSave();
        snapshot.Units.Add(new RunUnitPlacementSave { OrderIndex = 0, UnitId = 11, Role = 0, SlotIndex = 0, Name = "重剑手", Q = 2, R = -2, Presence = 0, Hp = 7 });
        snapshot.Units.Add(new RunUnitPlacementSave { OrderIndex = 1, UnitId = 12, Role = 0, SlotIndex = 1, Name = "重剑手2", Q = 1, R = -1, Presence = 0, Hp = 4 });
        // 敌人故意给错导出序：靠怪物实例键兜底匹配。
        snapshot.Units.Add(new RunUnitPlacementSave { OrderIndex = 9, UnitId = 21, Role = 1, InstanceKey = "F1-001-M01", Presence = 1, Hp = 0 });

        bool ok = PostBattleSnapshotCodec.ResolveUnitLayout(snapshot, new List<int> { 11, 12 },
            id => id == 21 ? "F1-001-M01" : string.Empty, occupancy.Placements,
            out Dictionary<int, (AxialHex Coord, BattlefieldPresence Presence)> layout, out Dictionary<int, int> hp, out string error);

        Assert.True(ok);
        Assert.Equal("", error);
        Assert.Equal(new AxialHex(2, -2), layout[11].Coord);
        Assert.Equal(new AxialHex(1, -1), layout[12].Coord);
        Assert.Equal(BattlefieldPresence.Active, layout[12].Presence);
        Assert.Equal(4, hp[12]);
        Assert.Equal(BattlefieldPresence.Defeated, layout[21].Presence);
        Assert.Equal(0, hp[21]);
    }

    [Fact]
    public void ResolveUnitLayout_FailsWhenNothingMatchesTheRebuiltBattlefield()
    {
        BattleBoard board = OpenBoard();
        var occupancy = new BattleOccupancyService(board);
        Assert.True(occupancy.TryPlace(Placement(11, BattlefieldRole.Player, "重剑手"), new AxialHex(0, 0), out _));

        var snapshot = new RunPostBattleSave();
        snapshot.Units.Add(new RunUnitPlacementSave { OrderIndex = 40, UnitId = 99, Role = 1, Name = "不存在", InstanceKey = "X", Presence = 0, Hp = 5 });
        Assert.False(PostBattleSnapshotCodec.ResolveUnitLayout(snapshot, new List<int> { 11 }, _ => string.Empty,
            occupancy.Placements, out _, out _, out string error));
        Assert.Contains("没有可匹配的单位", error);

        Assert.False(PostBattleSnapshotCodec.ResolveUnitLayout(new RunPostBattleSave(), new List<int> { 11 }, _ => string.Empty,
            occupancy.Placements, out _, out _, out string emptyError));
        Assert.Contains("没有单位", emptyError);
    }

    [Fact]
    public void RestoreLayout_MovesUnitsAndDropsDefeatedOnesFromOccupancy()
    {
        BattleBoard board = OpenBoard();
        var occupancy = new BattleOccupancyService(board);
        var player = Placement(11, BattlefieldRole.Player, "重剑手");
        var enemy = Placement(21, BattlefieldRole.Enemy, "堕魔守卫");
        Assert.True(occupancy.TryPlace(player, new AxialHex(0, 0), out _));
        Assert.True(occupancy.TryPlace(enemy, new AxialHex(2, 0), out _));

        occupancy.RestoreLayout(new Dictionary<int, (AxialHex Coord, BattlefieldPresence Presence)>
        {
            [11] = (new AxialHex(1, 1), BattlefieldPresence.Active),
            [21] = (new AxialHex(2, 0), BattlefieldPresence.Defeated),
        });

        Assert.Equal(new AxialHex(1, 1), player.Coord);
        Assert.Same(player, occupancy.At(new AxialHex(1, 1)));
        Assert.Null(occupancy.At(new AxialHex(0, 0)));            // 旧格已释放
        Assert.Equal(BattlefieldPresence.Defeated, enemy.Presence);
        Assert.Null(occupancy.At(new AxialHex(2, 0)));            // 退场单位不再占格
        Assert.True(occupancy.CanEnter(new AxialHex(2, 0)));
    }

    [Fact]
    public void RestoreLayout_KeepsUnitsMissingFromTheSnapshotAndFallsBackOnBadCoords()
    {
        BattleBoard board = OpenBoard();
        var occupancy = new BattleOccupancyService(board);
        var moved = Placement(11, BattlefieldRole.Player, "重剑手");
        var blocker = Placement(12, BattlefieldRole.Player, "重剑手2");
        var untouched = Placement(21, BattlefieldRole.Enemy, "堕魔守卫");
        Assert.True(occupancy.TryPlace(moved, new AxialHex(0, 0), out _));
        Assert.True(occupancy.TryPlace(blocker, new AxialHex(1, 0), out _));
        Assert.True(occupancy.TryPlace(untouched, new AxialHex(2, 0), out _));

        occupancy.RestoreLayout(new Dictionary<int, (AxialHex Coord, BattlefieldPresence Presence)>
        {
            [11] = (new AxialHex(9, 9), BattlefieldPresence.Active),        // 地图外 → 退回原格
            [12] = (new AxialHex(0, 0), BattlefieldPresence.Active),        // 已被 11 占着的快照格 → 退回原格
        });

        Assert.Equal(new AxialHex(0, 0), moved.Coord);
        Assert.Equal(new AxialHex(1, 0), blocker.Coord);
        // 不在快照里的单位保持原位，并继续占格。
        Assert.Equal(new AxialHex(2, 0), untouched.Coord);
        Assert.Same(untouched, occupancy.At(new AxialHex(2, 0)));
    }

    [Fact]
    public void RestoreLayout_ReleasesTheCellOfAUnitThatLeavesTheBoard()
    {
        BattleBoard board = OpenBoard();
        var occupancy = new BattleOccupancyService(board);
        var fled = Placement(21, BattlefieldRole.Enemy, "逃走的怪物");
        Assert.True(occupancy.TryPlace(fled, new AxialHex(0, 1), out _));

        occupancy.RestoreLayout(new Dictionary<int, (AxialHex Coord, BattlefieldPresence Presence)>
        {
            [21] = (new AxialHex(0, 1), BattlefieldPresence.Departed),
        });

        Assert.Equal(BattlefieldPresence.Departed, fled.Presence);
        Assert.Null(occupancy.At(new AxialHex(0, 1)));
    }
}
