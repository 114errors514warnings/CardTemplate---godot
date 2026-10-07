// PlaceLevelConfigTests.cs
// 地点关（`LevelType = Village / Merchant`）的版图自检（2026-10-07，§33 统一关卡通道）。
// 口径出处：村庄地图交互案 §2.1–§2.3（R=3 / 37 格 / 设施占格定稿表 + 六条不变式）、§三（入口 / 离开）、
//   商人交互案 §二（空白底图 + 商人 3 格 + 入口 / 离开格）、配表规范 §「交互点定义表」。
// 这六条不变式原住在已删的 `VillageLayoutTests`（专用版图表）里，本批改成按**统一载体**复核：
//   地图 JSON（`BattleMap/MapIndex.csv` + `Maps/M-V-001.json`）+ 关卡 CSV（`Level/Config/F1-V-001.csv`）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardSimulator.Battlefield;
using Xunit;

public sealed class PlaceLevelConfigTests
{
    private const string VillageLevelId = "F1-V-001";
    private const string MerchantLevelId = "F1-M-001";

    private static BattleLevelConfig LoadLevel(string levelId)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Level", "Config", levelId + ".csv");
        string[] rows = File.ReadAllLines(path).Where(x => !string.IsNullOrWhiteSpace(x)).Skip(1).ToArray();
        return BattleLevelCatalog.Parse(levelId, rows);
    }

    private static BattleMapDefinition LoadMap(string mapId) =>
        BattleMapDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BattleMap", mapId + ".json")));

    private static List<BattleLevelObject> Points(BattleLevelConfig level, string definitionId) =>
        level.Objects.Where(x => x.ObjectType == "InteractPoint" && x.DefinitionId == definitionId).ToList();

    private static List<AxialHex> Cells(BattleLevelConfig level, string definitionId, bool door) =>
        Points(level, definitionId)
            .Where(x => string.Equals(x.Extra, InteractPointCatalog.DoorExtraValue, StringComparison.OrdinalIgnoreCase) == door)
            .Select(x => new AxialHex(x.Q, x.R))
            .ToList();

    private static int NodeIdOf(int radius, AxialHex hex) => MapGeometry.EnumerateCells(radius).IndexOf(hex);

    private static int Ring(AxialHex hex) => Math.Max(Math.Abs(hex.Q), Math.Max(Math.Abs(hex.R), Math.Abs(hex.Q + hex.R)));

    private static HashSet<AxialHex> Bfs(IReadOnlyList<AxialHex> walkable, AxialHex from)
    {
        var set = new HashSet<AxialHex>(walkable);
        var visited = new HashSet<AxialHex> { from };
        var queue = new Queue<AxialHex>();
        queue.Enqueue(from);
        AxialHex[] offsets =
        {
            new AxialHex(1, 0), new AxialHex(-1, 0), new AxialHex(0, 1),
            new AxialHex(0, -1), new AxialHex(1, -1), new AxialHex(-1, 1),
        };
        while (queue.Count > 0)
        {
            AxialHex current = queue.Dequeue();
            foreach (AxialHex offset in offsets)
            {
                var next = new AxialHex(current.Q + offset.Q, current.R + offset.R);
                if (set.Contains(next) && visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return visited;
    }

    // ── 村庄：§2.2 定稿表逐格一致 ─────────────────────────────────────

    [Fact]
    public void Village_level_declares_25_interact_points_on_a_37_cell_board()
    {
        BattleLevelConfig level = LoadLevel(VillageLevelId);
        BattleMapDefinition map = LoadMap(level.MapId);

        Assert.Equal(PlaceLevelTypes.Village, level.LevelType);
        Assert.Equal("M-V-001", level.MapId);
        Assert.Equal(3, map.Radius);
        Assert.Equal(37, MapGeometry.EnumerateCells(map.Radius).Count);
        Assert.Equal(25, level.Objects.Count);
        Assert.All(level.Objects, x => Assert.Equal("InteractPoint", x.ObjectType));
        Assert.Equal(25, level.Objects.Select(x => x.InstanceId).Distinct().Count());
    }

    [Fact]
    public void Village_facilities_and_doors_match_the_final_layout_table()
    {
        BattleLevelConfig level = LoadLevel(VillageLevelId);
        const int radius = 3;

        var expected = new (string Id, int[] BodyNodeIds, int DoorNodeId)[]
        {
            ("Inn", new[] { 15, 22, 28, 33 }, 23),
            ("Guesthouse", new[] { 3, 2, 1 }, 7),
            ("Smithy", new[] { 0, 4, 9 }, 10),
            ("Restaurant", new[] { 21, 14, 8 }, 13),
            ("Forest", new[] { 34, 35, 36, 32, 27 }, 31),
        };

        foreach ((string id, int[] bodyNodeIds, int doorNodeId) in expected)
        {
            int[] body = Cells(level, id, door: false).Select(x => NodeIdOf(radius, x)).OrderBy(x => x).ToArray();
            Assert.Equal(bodyNodeIds.OrderBy(x => x).ToArray(), body);
            Assert.Equal(new[] { doorNodeId }, Cells(level, id, door: true).Select(x => NodeIdOf(radius, x)).ToArray());
        }

        Assert.Equal(new[] { 5 }, Cells(level, InteractPointCatalog.EntranceId, door: true).Select(x => NodeIdOf(radius, x)).ToArray());
        Assert.Equal(new[] { 30 }, Cells(level, InteractPointCatalog.ExitId, door: true).Select(x => NodeIdOf(radius, x)).ToArray());
    }

    [Fact]
    public void Village_layout_keeps_the_six_invariants()
    {
        BattleLevelConfig level = LoadLevel(VillageLevelId);
        const int radius = 3;
        List<AxialHex> cells = MapGeometry.EnumerateCells(radius);
        var occupied = new HashSet<AxialHex>(level.Objects.Select(x => new AxialHex(x.Q, x.R)));
        var blocked = new HashSet<AxialHex>(level.Objects
            .Where(x => !string.Equals(x.Extra, InteractPointCatalog.DoorExtraValue, StringComparison.OrdinalIgnoreCase))
            .Select(x => new AxialHex(x.Q, x.R)));
        List<AxialHex> walkable = cells.Where(c => !blocked.Contains(c)).ToList();

        // ① 版图恰 37 格；每行都在版图内，且没有任何两行占同一格。
        Assert.Equal(37, cells.Count);
        Assert.Equal(level.Objects.Count, occupied.Count);
        Assert.All(occupied, c => Assert.Contains(c, cells));

        // ② / ③ 5 个设施：本体全在**外圈**（Radius == 3）、门恰一个且在**内圈**、门与本体相邻。
        foreach (string id in new[] { "Inn", "Guesthouse", "Smithy", "Restaurant", "Forest" })
        {
            List<AxialHex> body = Cells(level, id, door: false);
            Assert.InRange(body.Count, 3, 5);
            Assert.All(body, c => Assert.Equal(radius, Ring(c)));

            List<AxialHex> doors = Cells(level, id, door: true);
            Assert.Single(doors);
            Assert.True(Ring(doors[0]) < radius);
            Assert.Contains(body, b => AxialHex.Distance(b, doors[0]) == 1);
        }

        // 入口格 / 离开格：各一个、都在内圈。
        foreach (string id in new[] { InteractPointCatalog.EntranceId, InteractPointCatalog.ExitId })
        {
            List<AxialHex> doors = Cells(level, id, door: true);
            Assert.Single(doors);
            Assert.True(Ring(doors[0]) < radius);
        }

        // ④ 内圈 19 格全可走 = 5 个门口格 + 入口 + 离开 + 12 空地；从入口格 BFS 全覆盖。
        List<AxialHex> inner = cells.Where(c => Ring(c) < radius).ToList();
        Assert.Equal(19, inner.Count);
        Assert.Equal(19, walkable.Count);
        Assert.Equal(
            walkable.Select(c => NodeIdOf(radius, c)).OrderBy(x => x),
            inner.Select(c => NodeIdOf(radius, c)).OrderBy(x => x));

        AxialHex entrance = Cells(level, InteractPointCatalog.EntranceId, door: true)[0];
        HashSet<AxialHex> reached = Bfs(walkable, entrance);
        Assert.All(walkable, c => Assert.Contains(c, reached));

        // ⑤ 入口格 ≠ 离开格。
        Assert.NotEqual(entrance, Cells(level, InteractPointCatalog.ExitId, door: true)[0]);
    }

    // ── 商人：空白底图 + 商人 3 格 + 入口 / 离开格 ────────────────────

    [Fact]
    public void Merchant_level_is_a_blank_board_with_a_three_cell_merchant()
    {
        BattleLevelConfig level = LoadLevel(MerchantLevelId);
        BattleMapDefinition map = LoadMap(level.MapId);

        Assert.Equal(PlaceLevelTypes.Merchant, level.LevelType);
        Assert.Equal("M-M-001", level.MapId);
        Assert.Equal(3, map.Radius);
        Assert.Equal(5, level.Objects.Count);

        List<AxialHex> merchant = Cells(level, InteractPointCatalog.MerchantId, door: false);
        Assert.Equal(3, merchant.Count);
        Assert.Single(merchant.Select(c => c.R).Distinct());                    // 横向三格
        Assert.Equal(3, merchant.Select(c => c.Q).Distinct().Count());
        Assert.Empty(Cells(level, InteractPointCatalog.MerchantId, door: true)); // 商人没有门口格（相邻自动触发）

        AxialHex entrance = Cells(level, InteractPointCatalog.EntranceId, door: true).Single();
        AxialHex exit = Cells(level, InteractPointCatalog.ExitId, door: true).Single();

        // 入口 / 离开格都不与商人相邻：进店先看到地图、走到商人旁边才弹界面，离开格也不会被弹窗打断。
        Assert.All(merchant, c => Assert.NotEqual(1, AxialHex.Distance(c, entrance)));
        Assert.All(merchant, c => Assert.NotEqual(1, AxialHex.Distance(c, exit)));

        // 离开格从入口格走得到（商人本体不可通行，必须能绕过去）。
        List<AxialHex> walkable = MapGeometry.EnumerateCells(map.Radius)
            .Where(c => !merchant.Contains(c))
            .ToList();
        Assert.Contains(exit, Bfs(walkable, entrance));
    }

    [Fact]
    public void Level_index_lists_both_place_levels_and_classification_is_by_level_type()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Level", "LevelIndex.csv");
        List<string> ids = File.ReadAllLines(path)
            .Skip(1)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Split(',')[0].Trim())
            .ToList();

        Assert.Contains(VillageLevelId, ids);
        Assert.Contains(MerchantLevelId, ids);
        Assert.True(PlaceLevelTypes.IsPlace(LoadLevel(VillageLevelId).LevelType));
        Assert.True(PlaceLevelTypes.IsPlace(LoadLevel(MerchantLevelId).LevelType));
        Assert.False(PlaceLevelTypes.IsPlace("NormalCombat"));
        Assert.False(PlaceLevelTypes.IsPlace("Event"));
        Assert.False(PlaceLevelTypes.IsPlace("Survival"));
        Assert.Equal("村庄", PlaceLevelTypes.DisplayName(VillageLevelId == string.Empty ? string.Empty : "Village"));
    }
}
