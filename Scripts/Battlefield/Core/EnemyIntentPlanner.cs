using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

public enum EnemyTargetPolicy { Nearest, ThrustNearestCollinear, ThrowSingleLowestHealth, ThrowAreaDense }
public enum EnemyActionOrder { MoveThenAttack, AttackThenMove }

public sealed record EnemyIntentSpec(WeaponAttackMode AttackMode, int AttackRange, int MoveBudget,
    EnemyActionOrder Order, EnemyTargetPolicy TargetPolicy, int AreaRadius = 0);

public sealed record EnemyIntentPlan(BattleUnitPlacement Target, AxialHex? LandingCell, IReadOnlyList<AxialHex> Path);

/// <summary>Migration seam for data-driven intents. Unknown legacy intents remain melee with two movement steps.</summary>
public static class BattleEnemyIntentCatalog
{
    private static readonly Dictionary<(int MonsterId, int IntentionIndex), EnemyIntentSpec> overrides = new();
    public static void Register(int monsterId, int intentionIndex, EnemyIntentSpec spec) => overrides[(monsterId, intentionIndex)] = spec;
    public static EnemyIntentSpec Resolve(MonsterInstance monster)
    {
        if (monster != null && overrides.TryGetValue((monster.id, monster.SelectedIntentionIndex), out var spec)) return spec;
        return new EnemyIntentSpec(WeaponAttackMode.AdjacentSingle, 1, 2, EnemyActionOrder.MoveThenAttack, EnemyTargetPolicy.Nearest);
    }
}

public static class EnemyIntentPlanner
{
    public static EnemyIntentPlan Plan(BattleBoard board, BattleOccupancyService occupancy, BattleUnitPlacement enemy,
        IEnumerable<BattleUnitPlacement> players, BattleUnitPlacement protectedTarget, EnemyIntentSpec spec, Random random)
    {
        var candidates = players.Where(x => x.Presence == BattlefieldPresence.Active && x.Unit.HP > 0).ToList();
        if (protectedTarget?.Presence == BattlefieldPresence.Active && protectedTarget.Unit.HP > 0 &&
            CanHit(board, occupancy, enemy.Coord, protectedTarget.Coord, spec))
            return new EnemyIntentPlan(protectedTarget, protectedTarget.Coord, Array.Empty<AxialHex>());
        if (candidates.Count == 0) return null;

        BattleUnitPlacement target;
        AxialHex? landing = null;
        if (spec.TargetPolicy == EnemyTargetPolicy.ThrowAreaDense)
        {
            var landings = board.Cells.Keys.Where(c => BattleRangeResolver.Distance(enemy.Coord, c) <= spec.AttackRange)
                .Select(c => new { Cell = c, Hits = candidates.Where(p => BattleRangeResolver.Distance(c, p.Coord) <= spec.AreaRadius).ToList() })
                .Where(x => x.Hits.Count > 0).ToList();
            if (landings.Count == 0) return null;
            int maxHits = landings.Max(x => x.Hits.Count);
            var dense = landings.Where(x => x.Hits.Count == maxHits).ToList();
            int lowestHp = dense.Min(x => x.Hits.Min(p => p.Unit.HP));
            var low = dense.Where(x => x.Hits.Min(p => p.Unit.HP) == lowestHp).ToList();
            int distance = low.Min(x => BattleRangeResolver.Distance(enemy.Coord, x.Cell));
            var final = low.Where(x => BattleRangeResolver.Distance(enemy.Coord, x.Cell) == distance).ToList();
            var chosen = final[random.Next(final.Count)]; landing = chosen.Cell;
            target = chosen.Hits.Where(x => x.Unit.HP == chosen.Hits.Min(p => p.Unit.HP)).OrderBy(x => x.UnitId).First();
        }
        else
        {
            IEnumerable<BattleUnitPlacement> ordered = candidates;
            if (spec.TargetPolicy == EnemyTargetPolicy.ThrustNearestCollinear)
                ordered = ordered.Where(p => BattleRangeResolver.SixNeighborOffsets.Any(d => IsOnRay(enemy.Coord, p.Coord, d)));
            if (!ordered.Any()) return null;
            ordered = spec.TargetPolicy == EnemyTargetPolicy.ThrowSingleLowestHealth
                ? ordered.OrderBy(p => p.Unit.HP).ThenBy(p => BattleRangeResolver.Distance(enemy.Coord, p.Coord)).ThenBy(p => p.UnitId)
                : ordered.OrderBy(p => BattleRangeResolver.Distance(enemy.Coord, p.Coord)).ThenBy(p => p.UnitId);
            target = ordered.First(); landing = target.Coord;
        }
        return new EnemyIntentPlan(target, landing, Array.Empty<AxialHex>());
    }

    private static bool CanHit(BattleBoard board, BattleOccupancyService occupancy, AxialHex origin, AxialHex target, EnemyIntentSpec spec)
    {
        if (spec.AttackMode == WeaponAttackMode.ThrowSingle) return BattleRangeResolver.Distance(origin, target) <= spec.AttackRange;
        return BattleAttackTraceResolver.Resolve(board, occupancy, origin, target,
            new WeaponAttackSpec("intent", spec.AttackRange, 0, spec.AttackMode, 0)).Contains(target);
    }
    private static bool IsOnRay(AxialHex from, AxialHex target, AxialHex dir)
    {
        int dq = target.Q - from.Q, dr = target.R - from.R;
        return (dir.Q == 0 ? dq == 0 : dq % dir.Q == 0) && (dir.R == 0 ? dr == 0 : dr % dir.R == 0)
            && ((dir.Q == 0 ? dr / dir.R : dq / dir.Q) > 0);
    }
}
