// VillageForage.cs
// 村庄树林「搜寻材料」的**纯逻辑**（无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/树林交互案.md §三 / §四 / §八。
//   · 每次搜寻固定抽 2 次（待拍板第 1 条默认值）；
//   · 每次先按稀有度定档（普通 70% / 罕见 25% / 稀有 5%，待拍板第 2 条默认值），再在该档的现役材料里**均匀**取一种；
//   · 两次可抽到同一种 → 按背包的叠加口径**合并成一条**（数量 +1），不新开条目。
using System;
using System.Collections.Generic;
using CardSimulator;

/// <summary>可被搜寻抽到的材料（运行期由 `Material.csv` 的现役行构造）。</summary>
public sealed class ForageMaterialEntry
{
	public int MaterialId;
	public string DefinitionId = string.Empty;
	public ItemRarity Rarity = ItemRarity.Common;
}

public static class VillageForage
{
	/// <summary>每次搜寻的抽取次数（树林案 §三：固定 2 次）。</summary>
	public const int PicksPerSearch = 2;

	/// <summary>进入代价（时间点，树林案 §一）。</summary>
	public const float TimePointCost = 1f;

	/// <summary>稀有度权重（树林案 §三 的 70 / 25 / 5）。</summary>
	public static readonly (ItemRarity Rarity, int Weight)[] RarityWeights =
	{
		(ItemRarity.Common, 70),
		(ItemRarity.Uncommon, 25),
		(ItemRarity.Rare, 5),
	};

	/// <summary>权重总和（0 = 权重表为空）。</summary>
	public static int TotalWeight
	{
		get
		{
			int total = 0;
			foreach ((ItemRarity _, int weight) in RarityWeights)
			{
				total += Math.Max(0, weight);
			}

			return total;
		}
	}

	/// <summary>时间点不足的文案（树林案 §五，与锻铁铺 / 餐厅同一句式）。</summary>
	public static string TimePointShortText(float need, float have) =>
		$"时间点不足：需要 {RunTimePoints.Format(need)}，当前剩余 {RunTimePoints.Format(have)}。";

	/// <summary>时间点是否够进一次（树林案 §五：不足时不弹 tips、只给原因）。</summary>
	public static bool CanSearch(float remainingToday) => remainingToday + 1e-4f >= TimePointCost;

	/// <summary>按权重抽一次稀有度档；池里没有该档时退化为「池中实际存在的档」按同权重再抽一次（不空转）。</summary>
	public static ItemRarity RollRarity(Random random, IReadOnlyList<ForageMaterialEntry> pool)
	{
		ItemRarity Fallback()
		{
			List<ForageMaterialEntry> available = new List<ForageMaterialEntry>();
			foreach (ForageMaterialEntry entry in pool ?? Array.Empty<ForageMaterialEntry>())
			{
				if (entry != null && !available.Exists(x => x.Rarity == entry.Rarity))
				{
					available.Add(entry);
				}
			}

			if (available.Count == 0)
			{
				return ItemRarity.Common;
			}

			return available[random.Next(available.Count)].Rarity;
		}

		int total = TotalWeight;
		if (pool == null || pool.Count == 0 || total <= 0)
		{
			return Fallback();
		}

		int roll = random.Next(total);
		foreach ((ItemRarity rarity, int weight) in RarityWeights)
		{
			roll -= Math.Max(0, weight);
			if (roll < 0)
			{
				return HasAny(pool, rarity) ? rarity : Fallback();
			}
		}

		return Fallback();
	}

	/// <summary>在某一档里均匀取一种（池里没有该档返回 null，由调用方兜底）。</summary>
	public static ForageMaterialEntry PickInRarity(Random random, IReadOnlyList<ForageMaterialEntry> pool, ItemRarity rarity)
	{
		List<ForageMaterialEntry> matches = new List<ForageMaterialEntry>();
		foreach (ForageMaterialEntry entry in pool ?? Array.Empty<ForageMaterialEntry>())
		{
			if (entry != null && entry.Rarity == rarity)
			{
				matches.Add(entry);
			}
		}

		return matches.Count == 0 ? null : matches[random.Next(matches.Count)];
	}

	/// <summary>
	/// 抽一次搜寻的产出：`PicksPerSearch` 次定档 + 档内均匀取，同材料**合并成一条**（数量累加），
	/// 返回顺序 = 首次抽到的顺序（与浮字 `药草 ×1 / 苔藓 ×1` 的显示顺序一致）。
	/// </summary>
	public static List<(int MaterialId, int Count)> Roll(Random random, IReadOnlyList<ForageMaterialEntry> pool)
	{
		List<(int MaterialId, int Count)> gained = new List<(int MaterialId, int Count)>();
		if (pool == null || pool.Count == 0)
		{
			return gained;
		}

		for (int i = 0; i < PicksPerSearch; i++)
		{
			ItemRarity rarity = RollRarity(random, pool);
			ForageMaterialEntry picked = PickInRarity(random, pool, rarity);
			if (picked == null)
			{
				continue;
			}

			int index = gained.FindIndex(x => x.MaterialId == picked.MaterialId);
			if (index >= 0)
			{
				gained[index] = (gained[index].MaterialId, gained[index].Count + 1);
			}
			else
			{
				gained.Add((picked.MaterialId, 1));
			}
		}

		return gained;
	}

	private static bool HasAny(IReadOnlyList<ForageMaterialEntry> pool, ItemRarity rarity)
	{
		foreach (ForageMaterialEntry entry in pool)
		{
			if (entry != null && entry.Rarity == rarity)
			{
				return true;
			}
		}

		return false;
	}
}
