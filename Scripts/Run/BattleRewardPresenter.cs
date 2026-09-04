// BattleRewardPresenter.cs
// 结算奖励逻辑：按 DropTableId 取行并分组、卡池抽样、文案格式化。
// 依赖 LoadingSystem 的部分仅用于角色卡池解析，其余为纯函数便于单测。
using System;
using System.Collections.Generic;

public static class BattleRewardPresenter
{
	public static List<DropTableEntry> GetEntriesForTable(IReadOnlyList<DropTableEntry> all, int dropTableId)
	{
		List<DropTableEntry> result = new List<DropTableEntry>();
		if (all == null)
		{
			return result;
		}

		foreach (DropTableEntry entry in all)
		{
			if (entry != null && entry.DropTableId == dropTableId)
			{
				result.Add(entry);
			}
		}

		return result;
	}

	public static Dictionary<DropCategory, List<DropTableEntry>> GroupByCategory(IEnumerable<DropTableEntry> rows)
	{
		Dictionary<DropCategory, List<DropTableEntry>> groups = new Dictionary<DropCategory, List<DropTableEntry>>();
		if (rows == null)
		{
			return groups;
		}

		foreach (DropTableEntry entry in rows)
		{
			if (entry == null)
			{
				continue;
			}

			if (!groups.TryGetValue(entry.Category, out List<DropTableEntry> list))
			{
				list = new List<DropTableEntry>();
				groups[entry.Category] = list;
			}

			list.Add(entry);
		}

		return groups;
	}

	/// <summary>从候选池随机抽 amount 张不重复（不足则全给）。</summary>
	public static List<int> SampleFromPool(IReadOnlyList<int> pool, int amount, Random rng)
	{
		List<int> result = new List<int>();
		if (pool == null || pool.Count == 0 || amount <= 0)
		{
			return result;
		}

		List<int> candidates = new List<int>(pool);
		Random random = rng ?? new Random();
		for (int i = candidates.Count - 1; i > 0; i--)
		{
			int j = random.Next(i + 1);
			int temp = candidates[i];
			candidates[i] = candidates[j];
			candidates[j] = temp;
		}

		int count = candidates.Count < amount ? candidates.Count : amount;
		for (int i = 0; i < count; i++)
		{
			result.Add(candidates[i]);
		}

		return result;
	}

	/// <summary>解析 Card 行中的角色过滤：0=当前队伍全部槽位并集，否则按指定角色。</summary>
	public static List<int> ResolveCardRewardCharacterIds(DropTableEntry cardRow, RunSaveData run)
	{
		List<int> result = new List<int>();
		if (cardRow == null || cardRow.Category != DropCategory.Card)
		{
			return result;
		}

		if (run == null)
		{
			return result;
		}

		if (cardRow.RewardParam == 0)
		{
			foreach (RunCharacterSlotSave slot in run.CharacterSlots)
			{
				if (!result.Contains(slot.CharacterId))
				{
					result.Add(slot.CharacterId);
				}
			}

			return result;
		}

		result.Add(cardRow.RewardParam);
		return result;
	}

	/// <summary>给一行掉落物生成展示文案（不含 Card，Card 走选卡面板）。</summary>
	public static string FormatRewardLine(DropTableEntry entry)
	{
		if (entry == null)
		{
			return string.Empty;
		}

		switch (entry.Category)
		{
			case DropCategory.Gold:
				return $"金币 +{entry.Amount}";
			case DropCategory.Material:
				return $"材料 {entry.RewardParam} ×{entry.Amount}";
			case DropCategory.Item:
				return $"道具 {entry.RewardParam} ×{entry.Amount}";
			case DropCategory.Key:
				return $"钥匙 +{entry.Amount}";
			default:
				return entry.Category.ToString();
		}
	}

	/// <summary>把已配置奖励落到局内：金币累加（卡牌与材料由调用方单独处理）。</summary>
	public static void ApplyNonCardRewardsToRun(int dropTableId, RunSaveData run, IReadOnlyList<DropTableEntry> allEntries)
	{
		if (run == null || allEntries == null)
		{
			return;
		}

		foreach (DropTableEntry entry in allEntries)
		{
			if (entry == null || entry.DropTableId != dropTableId || entry.Category == DropCategory.Card)
			{
				continue;
			}

			switch (entry.Category)
			{
				case DropCategory.Gold:
					run.Gold += entry.Amount;
					break;
				case DropCategory.Key:
					run.Keys += entry.Amount;
					break;
				// Material / Item：本期仅计入计数用的占位逻辑（无背包系统），仅打日志由 UI 层处理
			}
		}
	}

	/// <summary>找出一张卡属于哪个槽位（以其卡池含该卡为准）；找不到返回 -1。</summary>
	public static int FindOwningSlotIndex(RunSaveData run, int cardId)
	{
		if (run == null)
		{
			return -1;
		}

		for (int i = 0; i < run.CharacterSlots.Count; i++)
		{
			List<int> pool = LoadingSystem.GetCharacterRewardCardIds(run.CharacterSlots[i].CharacterId);
			if (pool != null && pool.Contains(cardId))
			{
				return i;
			}
		}

		return -1;
	}
}
