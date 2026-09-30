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
        if (!TryGetMover(unitId, out var actor)) return Array.Empty<AxialHex>();
        if (destination == actor.Coord) return Array.Empty<AxialHex>();
        int availableActions = Math.Min(actor.RemainingMoves, actor.Unit.Energy);
        if (maximumActions.HasValue) availableActions = Math.Min(availableActions, Math.Max(0, maximumActions.Value));
        int maxSteps = availableActions * actor.EffectiveMoveDistancePerAction;
        return BuildPath(actor.Coord, destination, Search(actor.Coord, destination, maxSteps));
    }

    /// <summary>
    /// 战后自由移动的最短合法路径（新案 §五）：除**额度**外与 <see cref="FindPath"/> 完全同规则
    /// （逐格可走、逐格可进入、不穿过障碍与单位）。返回的路径不含起点；目标不可达时为空。
    /// </summary>
    public IReadOnlyList<AxialHex> FindPathIgnoringBudget(int unitId, AxialHex destination)
    {
        if (!TryGetMover(unitId, out var actor)) return Array.Empty<AxialHex>();
        if (destination == actor.Coord) return Array.Empty<AxialHex>();
        return BuildPath(actor.Coord, destination, Search(actor.Coord, destination, NoStepLimit));
    }

    /// <summary>
    /// 战后自由移动的可达格（新案 §五）：从当前格出发的**连通可达区域**（不限步数、不含当前格）。
    /// 判定与逐格移动同规则，因此这里列出的每一格都能被 <see cref="TryMoveWithoutPlayerCost"/> 逐格走到。
    /// </summary>
    public IReadOnlyCollection<AxialHex> ReachableCells(int unitId)
    {
        if (!TryGetMover(unitId, out var actor)) return Array.Empty<AxialHex>();
        var previous = Search(actor.Coord, null, NoStepLimit);
        return previous.Keys.Where(x => x != actor.Coord).OrderBy(x => x.Q).ThenBy(x => x.R).ToArray();
    }

    /// <summary>不限步数（战后自由移动）。</summary>
    private const int NoStepLimit = -1;

    /// <summary>与 <see cref="FindPath"/> 同一口径的可移动单位：已注册且在场。</summary>
    private bool TryGetMover(int unitId, out BattleUnitPlacement placement)
    {
        if (!occupancy.Placements.TryGetValue(unitId, out placement)) return false;
        return placement.Presence == BattlefieldPresence.Active;
    }

    /// <summary>
    /// 同规则 BFS：逐格可走（非地图外 / 障碍 / 坑洞）且逐格可进入（不被任何单位占据）。
    /// <paramref name="maxSteps"/> 为 <see cref="NoStepLimit"/> 时不限步数（战后自由移动）。
    /// </summary>
    private Dictionary<AxialHex, AxialHex> Search(AxialHex start, AxialHex? destination, int maxSteps)
    {
        var queue = new Queue<AxialHex>();
        var previous = new Dictionary<AxialHex, AxialHex>();
        queue.Enqueue(start); previous[start] = start;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (destination.HasValue && current == destination.Value) break;
            if (maxSteps >= 0 && Depth(start, current, previous) >= maxSteps) continue;
            foreach (var next in BattleHexLayout.Neighbors(current).OrderBy(x => x.Q).ThenBy(x => x.R))
            {
                if (previous.ContainsKey(next) || !board.IsWalkable(next) || occupancy.At(next) != null) continue;
                previous[next] = current; queue.Enqueue(next);
            }
        }
        return previous;
    }

    private static int Depth(AxialHex start, AxialHex current, Dictionary<AxialHex, AxialHex> previous)
    {
        int depth = 0;
        for (var cursor = current; cursor != start; cursor = previous[cursor]) depth++;
        return depth;
    }

    /// <summary>由 BFS 前驱表回溯路径（不含起点）；目标不可达时为空。</summary>
    private static IReadOnlyList<AxialHex> BuildPath(AxialHex start, AxialHex destination, Dictionary<AxialHex, AxialHex> previous)
    {
        if (!previous.ContainsKey(destination)) return Array.Empty<AxialHex>();
        var result = new List<AxialHex>();
        for (var cursor = destination; cursor != start; cursor = previous[cursor]) result.Add(cursor);
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
