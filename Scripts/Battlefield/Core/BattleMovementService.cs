using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

public sealed record BattlefieldEntry(long EventId, int UnitId, AxialHex From, AxialHex To, GroundObject Trigger);

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

    public bool TryMove(int unitId, AxialHex destination, out string error)
    {
        occupancy.SyncDeaths(); error = Validate(unitId, destination);
        if (error.Length > 0) return false;
        executing = true;
        try
        {
            var p = occupancy.Placements[unitId]; var from = p.Coord;
            p.Unit.Energy--; p.MovesUsedThisTurn++;
            occupancy.CommitMove(p, destination);
            var trigger = board.Cells[destination].Trigger;
            if (trigger?.TriggerMode == EntryTriggerMode.Once)
                board.TryRemoveObject(destination, trigger.InstanceId, out _);
            Entered?.Invoke(new BattlefieldEntry(++entrySequence, unitId, from, destination, trigger));
            occupancy.SyncDeaths();
            return true;
        }
        finally { executing = false; }
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
            Entered?.Invoke(new BattlefieldEntry(++entrySequence, unitId, from, destination, trigger));
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
