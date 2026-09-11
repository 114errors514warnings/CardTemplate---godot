using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

public sealed record BattlefieldEntry(long EventId, int UnitId, AxialHex From, AxialHex To, GroundObject Trigger, bool ConsumesPlayerMove);

public sealed class BattleMovementService
{
    private readonly BattleBoard board;
    private readonly BattleOccupancyService occupancy;
    private long entrySequence;
    private bool executing;
    public bool PlayerTurn { get; set; } = true;
    public event Action<BattlefieldEntry> Entered;

    public BattleMovementService(BattleBoard board, BattleOccupancyService occupancy)
    { this.board = board; this.occupancy = occupancy; }

    public string Validate(int unitId, AxialHex destination)
    {
        if (executing) return "当前移动正在结算。";
        if (!PlayerTurn) return "当前不是玩家行动阶段。";
        if (!occupancy.Placements.TryGetValue(unitId, out var p) || p.Role != BattlefieldRole.Player ||
            p.Presence != BattlefieldPresence.Active || p.Unit.HP <= 0) return "当前角色无法移动。";
        if (AxialHex.Distance(p.Coord, destination) != 1) return "一次只能移动到相邻一格。";
        if (!board.IsWalkable(destination)) return "目标为地图外、障碍或坑洞。";
        if (!occupancy.CanEnter(destination)) return "目标格已有单位。";
        if (p.Unit.Energy < 1) return "能量不足，需要 1 点能量。";
        if (p.RemainingMoves < 1) return "本回合移动次数已用完。";
        return "";
    }

    public IReadOnlyList<AxialHex> LegalDestinations(int unitId)
    {
        if (!occupancy.Placements.TryGetValue(unitId, out var p)) return Array.Empty<AxialHex>();
        return BattleHexLayout.Neighbors(p.Coord).Where(x => Validate(unitId, x).Length == 0).ToArray();
    }

    /// <summary>Shortest legal player path. The returned path excludes the actor's current cell.</summary>
    public IReadOnlyList<AxialHex> FindPath(int unitId, AxialHex destination, int? maximumActions = null)
    {
        if (!occupancy.Placements.TryGetValue(unitId, out var actor) || actor.Presence != BattlefieldPresence.Active) return Array.Empty<AxialHex>();
        if (destination == actor.Coord) return Array.Empty<AxialHex>();
        var queue = new Queue<AxialHex>();
        var previous = new Dictionary<AxialHex, AxialHex>();
        queue.Enqueue(actor.Coord); previous[actor.Coord] = actor.Coord;
        int availableActions = Math.Min(actor.RemainingMoves, actor.Unit.Energy);
        if (maximumActions.HasValue) availableActions = Math.Min(availableActions, Math.Max(0, maximumActions.Value));
        int maxSteps = availableActions * actor.EffectiveMoveDistancePerAction;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == destination) break;
            int depth = 0; for (var cursor = current; cursor != actor.Coord; cursor = previous[cursor]) depth++;
            if (depth >= maxSteps) continue;
            foreach (var next in BattleHexLayout.Neighbors(current).OrderBy(x => x.Q).ThenBy(x => x.R))
            {
                if (previous.ContainsKey(next) || !board.IsWalkable(next)) continue;
                if (next != destination && occupancy.At(next) != null) continue;
                if (next == destination && occupancy.At(next) != null) continue;
                previous[next] = current; queue.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(destination)) return Array.Empty<AxialHex>();
        var result = new List<AxialHex>();
        for (var cursor = destination; cursor != actor.Coord; cursor = previous[cursor]) result.Add(cursor);
        result.Reverse(); return result;
    }

    public string ValidatePath(int unitId, IReadOnlyList<AxialHex> path)
    {
        if (path == null || path.Count == 0) return "请选择至少一个可到达格。";
        if (!occupancy.Placements.TryGetValue(unitId, out var actor)) return "当前角色无法移动。";
        int actions = (path.Count + actor.EffectiveMoveDistancePerAction - 1) / actor.EffectiveMoveDistancePerAction;
        if (actions > actor.RemainingMoves) return "本回合移动次数不足。";
        if (actions > actor.Unit.Energy) return "能量不足。";
        var cursor = actor.Coord;
        foreach (var cell in path)
        {
            if (AxialHex.Distance(cursor, cell) != 1 || !board.IsWalkable(cell) || occupancy.At(cell) != null)
                return "路径包含不可通行或已被占据的格子。";
            cursor = cell;
        }
        return "";
    }

    /// <summary>Executes every entered cell in order. Costs are charged once per move-action group.</summary>
    public bool TryMovePath(int unitId, IReadOnlyList<AxialHex> path, out string error)
    {
        occupancy.SyncDeaths(); error = ValidatePath(unitId, path);
        if (error.Length > 0) return false;
        var actor = occupancy.Placements[unitId]; int distance = actor.EffectiveMoveDistancePerAction;
        for (int index = 0; index < path.Count; index++)
        {
            if (!occupancy.Placements.TryGetValue(unitId, out actor) || actor.Presence != BattlefieldPresence.Active || actor.Unit.HP <= 0)
            { error = "单位在移动途中离场，后续路径已取消。"; return false; }
            bool startsAction = index % distance == 0;
            if (!TryMoveInternal(actor, path[index], startsAction, out error)) return false;
        }
        error = ""; return true;
    }

    public bool TryMove(int unitId, AxialHex destination, out string error)
    {
        occupancy.SyncDeaths(); error = Validate(unitId, destination);
        if (error.Length > 0) return false;
        return TryMoveInternal(occupancy.Placements[unitId], destination, true, out error);
    }

    public bool TryMoveWithoutPlayerCost(int unitId, AxialHex destination, out string error)
    {
        occupancy.SyncDeaths(); error = "";
        if (executing) { error = "当前移动正在结算。"; return false; }
        if (!occupancy.Placements.TryGetValue(unitId, out var p) || p.Presence != BattlefieldPresence.Active || p.Unit.HP <= 0)
        { error = "单位无法移动。"; return false; }
        if (AxialHex.Distance(p.Coord, destination) != 1 || !board.IsWalkable(destination) || !occupancy.CanEnter(destination))
        { error = "目标不是合法相邻空格。"; return false; }
        executing = true;
        try
        {
            var from = p.Coord; occupancy.CommitMove(p, destination);
            var trigger = board.Cells[destination].Trigger;
            if (trigger?.TriggerMode == EntryTriggerMode.Once) board.TryRemoveObject(destination, trigger.InstanceId, out _);
            Entered?.Invoke(new BattlefieldEntry(++entrySequence, unitId, from, destination, trigger, false));
            occupancy.SyncDeaths(); return true;
        }
        finally { executing = false; }
    }

    private bool TryMoveInternal(BattleUnitPlacement placement, AxialHex destination, bool chargeAction, out string error)
    {
        error = ""; executing = true;
        try
        {
            if (!board.IsWalkable(destination) || !occupancy.CanEnter(destination) || AxialHex.Distance(placement.Coord, destination) != 1)
            { error = "路径已被阻挡。"; return false; }
            if (chargeAction) { placement.Unit.Energy--; placement.MovesUsedThisTurn++; }
            var from = placement.Coord; occupancy.CommitMove(placement, destination);
            var trigger = board.Cells[destination].Trigger;
            if (trigger?.TriggerMode == EntryTriggerMode.Once) board.TryRemoveObject(destination, trigger.InstanceId, out _);
            Entered?.Invoke(new BattlefieldEntry(++entrySequence, placement.UnitId, from, destination, trigger, chargeAction));
            occupancy.SyncDeaths(); return true;
        }
        finally { executing = false; }
    }

    public void StartPlayerTurn()
    {
        if (executing) throw new InvalidOperationException("不能在移动结算中重置回合。");
        occupancy.SyncDeaths();
        foreach (var p in occupancy.Placements.Values) p.MovesUsedThisTurn = 0;
        PlayerTurn = true;
    }
}
