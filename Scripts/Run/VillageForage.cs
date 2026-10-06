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

	/// <summary>
	/// 每次**搜寻**的时间点代价（树林案 §一，2026-10-05 第四轮口径）= 全局数据表
	/// `DataBase/GameVariables.csv` 的 `ForestForageTimePointCost`（**树林专属项**，与村庄其他操作的
	/// `VillageOperationTimePointCost` = 0.1 **分开配**；由 `GameVariables.ApplyFacilityCosts()` 灌入，
	/// 表没接上时走 `RunFacilityCosts.DefaultForestForageCost`）。
	/// **进入树林本身不是操作**（踏入入口格不扣点），只有点 `进入` 执行一次搜寻才扣。
	/// </summary>
	public static float TimePointCost => RunFacilityCosts.ForestForageCost;

	/// <summary>稀有度权重（树林案 §三 的 70 / 25 / 5）。</summary>
	public static readonly (ItemRarity Rarity, int Weight)[] RarityWeights =
	{
		(ItemRarity.Common, 70),
		(ItemRarity.Uncommon, 25),
		(ItemRarity.Rare, 5),
	};

	/// <summary>
	/// 能否再搜寻一次（当天剩余 ≥ `TimePointCost`；不足 → 先去旅馆 / 民宿过夜或回营地结束当天）。
	/// 树林是**独立代价**，因此不复用 `RunFacilityCosts.CanOperate`（那个读村庄操作值）。
	/// </summary>
	public static bool CanSearch(float remainingToday) => remainingToday + 1e-4f >= TimePointCost;

	/// <summary>
	/// 时间点不足、不能搜寻的一行原因（含去处指引；句式共用 `RunTimePoints.ShortRestText`）。
	/// </summary>
	public static string SearchTimePointShortText(float remainingToday) =>
		RunTimePoints.ShortRestText(TimePointCost, remainingToday, RunFacilityCosts.RestHint);

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

	/// <summary>
	/// 时间点门槛与文案统一走 `VillageForage.CanSearch` / `VillageForage.SearchTimePointShortText`
	/// （树林案 §一：每次搜寻按**树林专属**表值收点），本文件只持有 `TimePointCost` 这一个数值来源。
	/// </summary>

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
