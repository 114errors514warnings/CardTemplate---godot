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
        if (spec.Mode == WeaponAttackMode.ThrowSingle) return Array.Empty<AxialHex>();

        int length = spec.Mode == WeaponAttackMode.AdjacentSingle ? 1 : spec.AttackRange;
        var result = new List<AxialHex>();
        for (int i = 1; i <= length; i++)
        {
            AxialHex cell = new(origin.Q + direction.Q * i, origin.R + direction.R * i);
            if (!board.Cells.TryGetValue(cell, out BattleCell data)) break;
            result.Add(cell);
            BattleUnitPlacement occupant = occupancy.At(cell);
            bool occupiedByOther = occupant != null && occupant.UnitId != ignoredOccupantId;
            bool terrainBlocks = data.Kind == BattleCellKind.Obstacle || data.BlocksSight;
            if (spec.Mode is WeaponAttackMode.RangedLine or WeaponAttackMode.Thrust)
            {
                if (terrainBlocks || occupiedByOther) break;
            }
            else if (spec.Mode == WeaponAttackMode.MeleeLine && terrainBlocks) break;
        }
        return result;
    }

    public static IReadOnlyList<AxialHex> ResolveFromSelectedCell(BattleBoard board, BattleOccupancyService occupancy,
        AxialHex origin, AxialHex selectedCell, WeaponAttackSpec spec, int ignoredOccupantId = -1)
    {
        if (spec?.Mode == WeaponAttackMode.ThrowSingle)
            return BattleRangeResolver.Distance(origin, selectedCell) <= spec.AttackRange && board.Cells.ContainsKey(selectedCell)
                ? new[] { selectedCell } : Array.Empty<AxialHex>();
        if (!TrySelectDirection(origin, selectedCell, out AxialHex direction)) return Array.Empty<AxialHex>();
        return ResolveFromDirection(board, occupancy, origin, direction, spec, ignoredOccupantId);
    }
}
