using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

public enum EnemyTargetPolicy { Nearest, ThrustNearestCollinear, ThrowSingleLowestHealth, ThrowAreaDense }
public enum EnemyActionOrder { MoveThenAttack, AttackThenMove }
public enum EnemyIntentPreviewCertainty { UnknownNumbers, KnownDamageUnknownRange, KnownDamageKnownRange }

public sealed record EnemyIntentSpec(WeaponAttackMode AttackMode, int AttackRange, int MoveBudget,
	EnemyActionOrder Order, EnemyTargetPolicy TargetPolicy, int AreaRadius = 0,
	EnemyIntentPreviewCertainty PreviewCertainty = EnemyIntentPreviewCertainty.KnownDamageUnknownRange,
	int ActionBudget = 0, AxialHex? PreviewDirection = null);

public sealed record EnemyIntentPlan(BattleUnitPlacement Target, AxialHex? LandingCell, IReadOnlyList<AxialHex> Path, AxialHex? AttackDirection = null);

/// <summary>Migration seam for data-driven intents. Unknown legacy intents remain melee with two movement steps.</summary>
public static class BattleEnemyIntentCatalog
{
	private static readonly Dictionary<(int MonsterId, int IntentionIndex), EnemyIntentSpec> overrides = new();
	private static bool loaded;
	public static void Register(int monsterId, int intentionIndex, EnemyIntentSpec spec) => overrides[(monsterId, intentionIndex)] = spec;
	public static EnemyIntentSpec Resolve(MonsterInstance monster)
	{
		EnsureLoaded();
		if (monster != null && overrides.TryGetValue((monster.id, monster.SelectedIntentionIndex), out var spec)) return spec;
		return new EnemyIntentSpec(WeaponAttackMode.AdjacentSingle, 1, 2, EnemyActionOrder.MoveThenAttack, EnemyTargetPolicy.Nearest);
	}

	private static void EnsureLoaded()
	{
		if (loaded) return;
		string[] lines = LoadCsv.LoadCSVDataLines(LoadingSystem.GetFilePathByKey("Data.Battlefield.EnemyIntent"));
		foreach (string line in lines)
		{
			if (string.IsNullOrWhiteSpace(line)) continue;
			string[] f = LoadCsv.ParseCSVFields(line);
			if (f.Length == 0 || string.Equals(f[0], "MonsterId", StringComparison.OrdinalIgnoreCase)) continue;
			if (f.Length < 11 || !int.TryParse(f[0], out int monsterId) || !int.TryParse(f[1], out int index) ||
				!Enum.TryParse(f[2], true, out EnemyIntentPreviewCertainty certainty) || !Enum.TryParse(f[3], true, out WeaponAttackMode mode) ||
				!int.TryParse(f[4], out int range) || !int.TryParse(f[5], out int move) || !int.TryParse(f[6], out int budget) ||
				!Enum.TryParse(f[7], true, out EnemyActionOrder order) || !Enum.TryParse(f[8], true, out EnemyTargetPolicy policy) || !int.TryParse(f[9], out int radius))
				throw new ArgumentException($"怪物意图 CSV 行无效：{line}");
			AxialHex? dir = null;
			string[] dirFields = f[10].Split(';');
			if (dirFields.Length == 2 && int.TryParse(dirFields[0], out int q) && int.TryParse(dirFields[1], out int r)) dir = new AxialHex(q, r);
			overrides[(monsterId, index)] = new EnemyIntentSpec(mode, range, move, order, policy, radius, certainty, budget, dir);
		}
		loaded = true;
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

		if (spec.AttackMode == WeaponAttackMode.RangedLine)
			return PlanRangedLine(board, occupancy, enemy, candidates, spec);

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

	/// <summary>
	/// Finds the player which can be hit after the fewest legal movement steps.  Every checked shot is an exact
	/// six-direction centre ray; obstacles and intervening units invalidate that ray, but are never target candidates.
	/// </summary>
	private static EnemyIntentPlan PlanRangedLine(BattleBoard board, BattleOccupancyService occupancy, BattleUnitPlacement enemy,
		IReadOnlyList<BattleUnitPlacement> players, EnemyIntentSpec spec)
	{
		var queue = new Queue<(AxialHex Cell, List<AxialHex> Path)>();
		var seen = new HashSet<AxialHex> { enemy.Coord };
		queue.Enqueue((enemy.Coord, new List<AxialHex>()));
		var options = new List<(BattleUnitPlacement Target, List<AxialHex> Path, AxialHex Direction)>();
		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			foreach (BattleUnitPlacement player in players)
			{
				if (CanRangedHitFrom(board, occupancy, enemy.UnitId, current.Cell, player.Coord, spec.AttackRange, out AxialHex direction))
					options.Add((player, current.Path, direction));
			}
			if (current.Path.Count >= spec.MoveBudget) continue;
			foreach (AxialHex next in BattleRangeResolver.Neighbors(current.Cell).OrderBy(x => x.Q).ThenBy(x => x.R))
			{
				if (!seen.Add(next) || !board.IsWalkable(next)) continue;
				if (next != enemy.Coord && occupancy.At(next) != null) continue;
				var path = new List<AxialHex>(current.Path) { next };
				queue.Enqueue((next, path));
			}
		}
		if (options.Count == 0) return null;
		int minSteps = options.Min(x => x.Path.Count);
		IEnumerable<(BattleUnitPlacement Target, List<AxialHex> Path, AxialHex Direction)> best = options.Where(x => x.Path.Count == minSteps);
		if (spec.TargetPolicy == EnemyTargetPolicy.ThrowSingleLowestHealth)
			best = best.OrderBy(x => x.Target.Unit.HP).ThenBy(x => x.Target.UnitId);
		else best = best.OrderBy(x => x.Target.UnitId);
		var chosen = best.First();
		return new EnemyIntentPlan(chosen.Target, chosen.Target.Coord, chosen.Path, chosen.Direction);
	}

	private static bool CanRangedHitFrom(BattleBoard board, BattleOccupancyService occupancy, int movingEnemyId,
		AxialHex origin, AxialHex target, int range, out AxialHex direction)
	{
		if (!BattleAttackSystem.TrySelectDirection(origin, target, out direction)) return false;
		int distance = BattleRangeResolver.Distance(origin, target);
		if (distance > range) return false;
		return BattleAttackSystem.ResolveFromDirection(board, occupancy, origin, direction,
			new WeaponAttackSpec("enemy", range, 0, WeaponAttackMode.RangedLine, 0), movingEnemyId).Contains(target);
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
