using System;
using System.Linq;

namespace CardSimulator.Battlefield;

public sealed class GroundObjectPlacementService
{
    private readonly BattleBoard board;
    private readonly BattleOccupancyService occupancy;
    private readonly Random random;
    public GroundObjectPlacementService(BattleBoard board, BattleOccupancyService occupancy, int seed)
    { this.board = board; this.occupancy = occupancy; random = new Random(seed); }

    public bool TryDrop(AxialHex desired, GroundObject item, out AxialHex landed, out string error)
    {
        landed = desired; error = "";
        if (item == null || item.IsTrigger) { error = "掉落只能放置道具或装备。"; return false; }
        if (CanReceive(desired)) return board.TryAddObject(desired, item, out error);
        var candidates = board.Cells.Keys.Where(CanReceive).OrderBy(x => AxialHex.Distance(desired, x))
            .ThenBy(x => x.Q).ThenBy(x => x.R).ToArray();
        if (candidates.Length == 0) { error = "没有合法物品接收格。"; return false; }
        int distance = AxialHex.Distance(desired, candidates[0]);
        var nearest = candidates.TakeWhile(x => AxialHex.Distance(desired, x) == distance).ToArray();
        landed = nearest[random.Next(nearest.Length)];
        return board.TryAddObject(landed, item, out error);
    }

    public bool TryPlaceTrap(int sourceUnitId, AxialHex target, GroundObject trap, int range, out string error)
    {
        error = "";
        if (trap?.Kind != GroundObjectKind.Trap) { error = "机关只能在初始化生成。"; return false; }
        if (!occupancy.Placements.TryGetValue(sourceUnitId, out var source) || source.Unit.HP <= 0 ||
            source.Presence != BattlefieldPresence.Active) { error = "投放来源无效。"; return false; }
        int distance = AxialHex.Distance(source.Coord, target);
        if (distance < 1 || distance > range || !occupancy.CanEnter(target)) { error = "投放超出范围或格点有单位/障碍。"; return false; }
        return board.TryAddObject(target, trap, out error);
    }

    private bool CanReceive(AxialHex coord) => board.Cells.TryGetValue(coord, out var cell) && cell.Walkable && cell.Trigger == null;
}
