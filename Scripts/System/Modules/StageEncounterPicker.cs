using System;
using System.Collections.Generic;

/// <summary>
/// Stage 遭遇选取（纯逻辑，可单测）：
/// 1) 有选取规则的类型（当前仅普通敌袭按难度档位）→ 规则优先；
/// 2) 无规则类型 → 从该文件全部行按 Weight 加权随机；
/// 3) 空候选/空表 → null（无配置，点击不触发）。
/// </summary>
public static class StageEncounterPicker
{
	/// <summary>第 1–3 场普通敌袭为 Low，4–5 场 Mid，6+ 场 High（来源《地图玩法数值设计.md》§四）。</summary>
	public static StageDifficulty ResolveNormalCombatDifficultyByEncounterCount(int foughtCount)
	{
		int nextIndex = foughtCount + 1;
		if (nextIndex <= 3)
		{
			return StageDifficulty.Low;
		}
		if (nextIndex <= 5)
		{
			return StageDifficulty.Mid;
		}
		return StageDifficulty.High;
	}

	/// <summary>
	/// 从候选行中按选取规则选一行。
	/// </summary>
	/// <param name="rows">某层某类型 CSV 的全部行（可为空）。</param>
	/// <param name="ruleDifficulty">规则难度（普通敌袭传解析后的档位；其余类型传 null/Any）。</param>
	/// <param name="requireUsableForCombat">战斗类类型要求行内 MonsterIds 非空。</param>
	public static StageEncounterRow Pick(
		IReadOnlyList<StageEncounterRow> rows,
		StageDifficulty? ruleDifficulty,
		Random rng,
		bool requireUsableForCombat = true)
	{
		if (rows == null || rows.Count == 0)
		{
			return null;
		}

		List<StageEncounterRow> candidates = new List<StageEncounterRow>(rows.Count);
		foreach (StageEncounterRow row in rows)
		{
			if (row == null)
			{
				continue;
			}

			if (ruleDifficulty.HasValue && ruleDifficulty.Value != StageDifficulty.Any && row.Difficulty != ruleDifficulty.Value)
			{
				continue;
			}

			if (requireUsableForCombat && !row.IsUsable)
			{
				continue;
			}

			candidates.Add(row);
		}

		if (candidates.Count == 0)
		{
			return null;
		}

		return candidates[PickWeightedIndex(candidates, rng)];
	}

	public static bool IsCombatLikeType(MapNodeType type)
	{
		return type == MapNodeType.NormalCombat
			|| type == MapNodeType.HighRiskCombat
			|| type == MapNodeType.Elite
			|| type == MapNodeType.Boss;
	}

	/// <summary>按 Weight 取一个随机下标；权重全 ≤0 时均匀随机。rng 为空时用默认随机源。</summary>
	public static int PickWeightedIndex<T>(IReadOnlyList<T> items, Random rng)
	{
		if (items == null || items.Count == 0)
		{
			return -1;
		}

		Random random = rng ?? new Random();

		int total = 0;
		foreach (T item in items)
		{
			total += GetWeight(item);
		}

		if (total <= 0)
		{
			return random.Next(items.Count);
		}

		int roll = random.Next(total);
		int accumulator = 0;
		for (int index = 0; index < items.Count; index++)
		{
			accumulator += GetWeight(items[index]);
			if (roll < accumulator)
			{
				return index;
			}
		}

		return items.Count - 1;
	}

	private static int GetWeight<T>(T item)
	{
		switch (item)
		{
			case StageEncounterRow row:
				return row.Weight > 0 ? row.Weight : 1;
			case DropTableEntry entry:
				return entry.Weight > 0 ? entry.Weight : 1;
			default:
				return 1;
		}
	}
}
