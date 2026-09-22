using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

/// <summary>Shared six-direction attack geometry for player weapons and enemy intentions.</summary>
public static class BattleAttackSystem
{
    public static bool TrySelectDirection(AxialHex origin, AxialHex selectedCell, out AxialHex direction) =>
        BattleRangeResolver.TryGetExactLineDirection(origin, selectedCell, out direction);

    public static IReadOnlyList<AxialHex> ResolveFromDirection(BattleBoard board, BattleOccupancyService occupancy,
        AxialHex origin, AxialHex direction, WeaponAttackSpec spec, int ignoredOccupantId = -1)
    {
        if (board == null || occupancy == null || spec == null || !BattleRangeResolver.SixNeighborOffsets.Contains(direction))
            return Array.Empty<AxialHex>();
        if (spec.Mode == WeaponAttackMode.Fan)
            return BattleRangeResolver.ResolveFanCells(origin, direction, spec.AttackRange).Where(board.Cells.ContainsKey).ToArray();
        if (spec.Mode == WeaponAttackMode.Ring)
            return BattleRangeResolver.CellsWithinRange(origin, spec.AttackRange).Where(board.Cells.ContainsKey).ToArray();
        if (spec.Mode == WeaponAttackMode.ThrowSingle) return Array.Empty<AxialHex>();

        // 近战直线不因单位停止，只被障碍截断；其余轴向模式统一走“首个阻挡即停”的射线。
        if (spec.Mode == WeaponAttackMode.MeleeLine)
        {
            var melee = new List<AxialHex>();
            for (int i = 1; i <= spec.AttackRange; i++)
            {
                AxialHex meleeCell = new(origin.Q + direction.Q * i, origin.R + direction.R * i);
                if (!board.Cells.TryGetValue(meleeCell, out BattleCell meleeData)) break;
                melee.Add(meleeCell);
                if (meleeData.Kind == BattleCellKind.Obstacle || meleeData.BlocksSight) break;
            }
            return melee;
        }

        return ResolveAxialRay(board, occupancy, origin, direction,
            spec.Mode == WeaponAttackMode.AdjacentSingle ? 1 : spec.AttackRange, ignoredOccupantId: ignoredOccupantId);
    }

    /// <summary>
    /// 沿六个轴向之一前进，返回经过格（含首个阻挡格）。方向必须正好是六邻向之一，非轴向返回空——
    /// 绝不像表现层那样把任意方向吸附成“最近的方向”。首个阻挡 = 单位或障碍/挡视线地形；
    /// 阻挡格本身可被命中，因此先记录再截断；`penetrates` 为真时穿过单位与可穿透地形。
    /// </summary>
    public static IReadOnlyList<AxialHex> ResolveAxialRay(BattleBoard board, BattleOccupancyService occupancy,
        AxialHex origin, AxialHex direction, int length, bool penetrates = false, int ignoredOccupantId = -1)
    {
        if (board == null || occupancy == null || length <= 0) return Array.Empty<AxialHex>();
        if (!BattleRangeResolver.SixNeighborOffsets.Contains(direction)) return Array.Empty<AxialHex>();
        var result = new List<AxialHex>();
        for (int i = 1; i <= length; i++)
        {
            AxialHex cell = new(origin.Q + direction.Q * i, origin.R + direction.R * i);
            if (!board.Cells.TryGetValue(cell, out BattleCell data)) break;
            result.Add(cell);
            BattleUnitPlacement occupant = occupancy.At(cell);
            bool unitBlocks = occupant != null && occupant.UnitId != ignoredOccupantId;
            bool terrainBlocks = data.Kind == BattleCellKind.Obstacle || data.BlocksSight;
            if (!penetrates && (unitBlocks || terrainBlocks)) break;
        }
        return result;
    }

    /// <summary>六条射线的并集（按方向顺序、每方向由近到远）：远程直线武器的候选格。</summary>
    public static IReadOnlyList<AxialHex> ResolveRayCandidates(BattleBoard board, BattleOccupancyService occupancy,
        AxialHex origin, int length, bool penetrates = false, int ignoredOccupantId = -1)
    {
        var result = new List<AxialHex>();
        var seen = new HashSet<AxialHex>();
        foreach (AxialHex direction in BattleRangeResolver.SixNeighborOffsets)
        {
            foreach (AxialHex cell in ResolveAxialRay(board, occupancy, origin, direction, length, penetrates, ignoredOccupantId))
            {
                if (seen.Add(cell)) result.Add(cell);
            }
        }
        return result;
    }

    public static IReadOnlyList<AxialHex> ResolveFromSelectedCell(BattleBoard board, BattleOccupancyService occupancy,
        AxialHex origin, AxialHex selectedCell, WeaponAttackSpec spec, int ignoredOccupantId = -1)
    {
        if (spec?.Mode == WeaponAttackMode.ThrowSingle)
            return BattleRangeResolver.Distance(origin, selectedCell) <= spec.AttackRange && board.Cells.ContainsKey(selectedCell)
                ? new[] { selectedCell } : Array.Empty<AxialHex>();
        if (spec?.Mode == WeaponAttackMode.Ring)
            return BattleRangeResolver.Distance(origin, selectedCell) <= spec.AttackRange
                ? BattleRangeResolver.CellsWithinRange(origin, spec.AttackRange).Where(board.Cells.ContainsKey).ToArray()
                : Array.Empty<AxialHex>();
        if (!TrySelectDirection(origin, selectedCell, out AxialHex direction)) return Array.Empty<AxialHex>();
        return ResolveFromDirection(board, occupancy, origin, direction, spec, ignoredOccupantId);
    }
}
