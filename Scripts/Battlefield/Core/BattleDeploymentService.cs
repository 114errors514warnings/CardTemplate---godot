using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

public sealed record GeneratedBattlefield(BattleBoard Board, IReadOnlyList<AxialHex> PlayerCoords,
    IReadOnlyList<AxialHex> EnemyCoords, int Seed, int Attempt);

public static class BattleDeploymentService
{
    public static GeneratedBattlefield Generate(BattleMapDefinition definition, int? seed = null)
    {
        definition.Validate();
        int actualSeed = seed ?? definition.Seed;
        var players = definition.PlayerSpawnCoords.Select(x => x.ToHex()).ToArray();
        var baseCells = CreateBase(definition);
        var protectedCells = new HashSet<AxialHex>(players);
        foreach (var entry in definition.GenerationExcludedCoords)
        {
            if (entry == null || !baseCells.ContainsKey(entry.ToHex())) throw new ArgumentException("生成禁区坐标无效。");
            protectedCells.Add(entry.ToHex());
        }
        foreach (var entry in definition.ObjectPlacements)
        {
            if (entry == null || !baseCells.ContainsKey(new AxialHex(entry.Q, entry.R))) throw new ArgumentException("固定物件坐标无效。");
            protectedCells.Add(new AxialHex(entry.Q, entry.R));
        }
        foreach (var spawn in players)
            if (!baseCells.TryGetValue(spawn, out var cell) || !cell.Walkable) throw new ArgumentException("玩家出生格不存在或不可通行。");
        var exits = definition.ExitAnchors.Select(x => x?.ToHex() ?? throw new ArgumentException("出口为空。")).ToArray();
        if (exits.Any(x => !baseCells.ContainsKey(x))) throw new ArgumentException("出口坐标不在地图中。");
        foreach (var exit in exits) protectedCells.Add(exit);
        string lastError = "";
        for (int attempt = 0; attempt < definition.MaxGenerationAttempts; attempt++)
        {
            var board = new BattleBoard(baseCells.Values.Select(x => new BattleCell(x.Coord, x.Kind, x.Surface, x.BlocksSight)));
            var obstacleCandidates = Sorted(board.Cells.Values.Where(x => x.Walkable && !protectedCells.Contains(x.Coord)).Select(x => x.Coord));
            if (obstacleCandidates.Count < definition.RandomObstacleCount) throw new ArgumentException("随机障碍数量超过可用格数。");
            Shuffle(obstacleCandidates, new Random(unchecked(actualSeed * 397 + attempt * 7919 + 11)));
            foreach (var coord in obstacleCandidates.Take(definition.RandomObstacleCount))
                board.ChangeTerrain(coord, BattleCellKind.Obstacle, BattleSurface.Ground, true);
            var component = CollectConnected(board, players[0]);
            bool hasExit = exits.Length > 0 ? exits.Any(component.Contains) :
                component.Any(x => AxialHex.Distance(x, new AxialHex(0, 0)) == definition.Radius);
            if (!hasExit || players.Any(x => !component.Contains(x))) { lastError = "玩家出生区被封闭。"; continue; }
            var candidates = Sorted(component.Where(x => !players.Contains(x) &&
                players.All(p => AxialHex.Distance(p, x) >= definition.MinEnemyDistance)));
            if (candidates.Count < definition.MonsterIds.Count) { lastError = "开放区域的怪物出生格不足。"; continue; }
            Shuffle(candidates, new Random(unchecked(actualSeed * 397 + attempt * 7919 + 29)));
            var enemies = candidates.Take(definition.MonsterIds.Count).ToArray();
            int serial = 0;
            foreach (var entry in definition.ObjectPlacements)
            {
                var obj = new GroundObject($"fixed-{++serial}", entry.DefinitionId, entry.Kind, entry.TriggerMode);
                if (!board.TryAddObject(new AxialHex(entry.Q, entry.R), obj, out string error))
                    throw new ArgumentException($"固定物件 {entry.DefinitionId}：{error}");
            }
            var itemCells = Sorted(board.Cells.Values.Where(x => x.Walkable && x.Trigger == null &&
                !definition.GenerationExcludedCoords.Any(e => e.ToHex() == x.Coord)).Select(x => x.Coord));
            if (definition.RandomItemCount > 0 && itemCells.Count == 0) { lastError = "没有合法道具接收格。"; continue; }
            var itemRandom = new Random(unchecked(actualSeed * 397 + attempt * 7919 + 47));
            for (int i = 0; i < definition.RandomItemCount; i++)
            {
                var coord = itemCells[itemRandom.Next(itemCells.Count)];
                var item = new GroundObject($"random-{i}", definition.RandomItemDefinitions[itemRandom.Next(definition.RandomItemDefinitions.Count)], GroundObjectKind.Item);
                if (!board.TryAddObject(coord, item, out var error)) throw new InvalidOperationException(error);
            }
            return new GeneratedBattlefield(board, Array.AsReadOnly(players), Array.AsReadOnly(enemies), actualSeed, attempt);
        }
        throw new InvalidOperationException($"地图 {definition.MapId} 在 {definition.MaxGenerationAttempts} 次生成后失败：{lastError}");
    }

    public static HashSet<AxialHex> CollectConnected(BattleBoard board, AxialHex start)
    {
        var found = new HashSet<AxialHex>();
        if (!board.IsWalkable(start)) return found;
        var queue = new Queue<AxialHex>(); queue.Enqueue(start); found.Add(start);
        while (queue.Count > 0)
            foreach (var next in BattleHexLayout.Neighbors(queue.Dequeue()))
                if (board.IsWalkable(next) && found.Add(next)) queue.Enqueue(next);
        return found;
    }

    private static Dictionary<AxialHex, BattleCell> CreateBase(BattleMapDefinition definition)
    {
        var cells = new Dictionary<AxialHex, BattleCell>();
        if (definition.Shape == "Hexagon")
            for (int q = -definition.Radius; q <= definition.Radius; q++)
                for (int r = -definition.Radius; r <= definition.Radius; r++)
                {
                    var coord = new AxialHex(q, r);
                    if (AxialHex.Distance(coord, new AxialHex(0, 0)) <= definition.Radius) cells.Add(coord, new BattleCell(coord));
                }
        else
            foreach (var item in definition.Cells)
            {
                var cell = FromData(item);
                if (!cells.TryAdd(cell.Coord, cell)) throw new ArgumentException("Cells 包含重复坐标。");
            }
        var overridden = new HashSet<AxialHex>();
        foreach (var item in definition.CellOverrides)
        {
            var cell = FromData(item);
            if (!cells.ContainsKey(cell.Coord) || !overridden.Add(cell.Coord)) throw new ArgumentException("覆盖坐标不存在或重复。");
            cells[cell.Coord] = cell;
        }
        return cells;
    }

    private static BattleCell FromData(BattleCellData item)
    {
        if (item == null || Math.Abs((long)item.Q) > 128 || Math.Abs((long)item.R) > 128 ||
            !Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Surface)) throw new ArgumentException("格点数据无效。");
        return new BattleCell(new AxialHex(item.Q, item.R), item.Kind, item.Surface, item.BlocksSight);
    }

    private static List<AxialHex> Sorted(IEnumerable<AxialHex> cells) => cells.OrderBy(x => x.Q).ThenBy(x => x.R).ToList();
    private static void Shuffle<T>(IList<T> items, Random random)
    {
        for (int i = items.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (items[i], items[j]) = (items[j], items[i]); }
    }
}
