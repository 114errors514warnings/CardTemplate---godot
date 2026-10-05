// MerchantStock.cs
// 商人货架（材料 / 食物 / 装备 / 道具 / 钥匙）的**纯逻辑**：按稀有度权重抽样、查价、快照生成（无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/商人交互案.md §4.1 / §4.4–§4.7 / §五 / §七。
//   · 格数来自 `MerchantCatalog.StockSlots`（材料 / 食物 / 装备 / 道具 各 3 格、钥匙 1 格）；
//   · 「本层首次进入商人场景时」抽一次 → 快照落档（`RunMerchantStateSave.Stock`），之后**不重抽、不补位、不重排**；
//   · 一格一件（待拍板第 5 条默认值），同物品不重复占两格；
//   · 价格取 `MerchantPrice.csv`（`MerchantCatalog.PriceFor`）；价格表里没有的档 → 该定义**不上架**（不静默取别的价）。
// ⚠️ 稀有度抽样权重：案里只写了「按稀有度权重抽」而**没有给数**（10 月施工文档 §27 的待拍板项 B17）。
//    本文件取**与树林同一档的默认值 70 / 25 / 5**（树林交互案 §三 已落档的口径），
//    并把权重收在 `RarityWeights` 一处 —— 用户拍板后只改这一张表。
using System;
using System.Collections.Generic;
using CardSimulator;

/// <summary>可上架的候选定义（运行期由材料 / 食物 / 装备 / 道具四张现役表构造）。</summary>
public sealed class MerchantStockCandidate
{
	public MerchantCategory Category;
	public int DefinitionKey;
	public string DefinitionId = string.Empty;
	public ItemRarity Rarity = ItemRarity.Common;
}

public static class MerchantStock
{
	/// <summary>货架类目顺序（界面从上到下 / 从左到右的绘制顺序，锁定唯一一处）。</summary>
	public static readonly MerchantCategory[] CategoryOrder =
	{
		MerchantCategory.Material,
		MerchantCategory.Food,
		MerchantCategory.Equipment,
		MerchantCategory.Item,
		MerchantCategory.Key,
	};

	/// <summary>
	/// 稀有度抽样权重（普通 70% / 罕见 25% / 稀有 5%）。
	/// **待拍板 B17**：商人案未给数，这里取树林的同一档默认值（见文件头说明）。
	/// </summary>
	public static readonly (ItemRarity Rarity, int Weight)[] RarityWeights =
	{
		(ItemRarity.Common, 70),
		(ItemRarity.Uncommon, 25),
		(ItemRarity.Rare, 5),
	};

	/// <summary>通用格的三态文案（商人案 §6.1 失败原因表）。</summary>
	public const string SoldOutText = "该商品已售出。";

	/// <summary>金币不足（与卡牌操作 / 锻铁铺同一句式）。</summary>
	public static string GoldShortText(int need, int have) => $"金币不足：需要 {need}，当前 {have}。";

	/// <summary>背包超载。</summary>
	public const string BagOverloadText = "背包已超载，请先整理。";

	/// <summary>权重总和。</summary>
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
	/// 生成一份货架快照：按 `CategoryOrder` 逐类抽「格数」件（同物品不重复、价格表缺档不上架）。
	/// `SlotIndex` 从 0 起连续；不足格数时留空位（不会用别的类目补）。
	/// </summary>
	public static List<RunMerchantStockEntrySave> Generate(
		Random random,
		IReadOnlyList<MerchantStockCandidate> catalog,
		IReadOnlyList<MerchantPriceRow> prices)
	{
		List<RunMerchantStockEntrySave> stock = new List<RunMerchantStockEntrySave>();
		if (catalog == null || catalog.Count == 0)
		{
			return stock;
		}

		foreach (MerchantCategory category in CategoryOrder)
		{
			List<MerchantStockCandidate> pool = new List<MerchantStockCandidate>();
			foreach (MerchantStockCandidate candidate in catalog)
			{
				if (candidate != null
					&& candidate.Category == category
					&& MerchantCatalog.PriceFor(prices, category, candidate.Rarity) >= 0)
				{
					pool.Add(candidate);
				}
			}

			int slots = MerchantCatalog.StockSlots(category);
			for (int slotIndex = 0; slotIndex < slots && pool.Count > 0; slotIndex++)
			{
				ItemRarity rarity = RollRarity(random, pool);
				MerchantStockCandidate picked = TakeUniform(random, pool, rarity) ?? TakeUniform(random, pool, null);
				if (picked == null)
				{
					break;
				}

				pool.Remove(picked);
				int price = MerchantCatalog.PriceFor(prices, category, picked.Rarity);
				stock.Add(new RunMerchantStockEntrySave
				{
					Category = (int)category,
					SlotIndex = slotIndex,
					DefinitionKey = picked.DefinitionKey,
					DefinitionId = picked.DefinitionId,
					Price = price,
					Sold = false,
				});
			}
		}

		return stock;
	}

	/// <summary>取某类目某一格（找不到返回 null；界面重进时按它恢复「已售出」/ 变暗态）。</summary>
	public static RunMerchantStockEntrySave FindEntry(
		IReadOnlyList<RunMerchantStockEntrySave> stock,
		MerchantCategory category,
		int slotIndex)
	{
		foreach (RunMerchantStockEntrySave entry in stock ?? Array.Empty<RunMerchantStockEntrySave>())
		{
			if (entry != null && entry.Category == (int)category && entry.SlotIndex == slotIndex)
			{
				return entry;
			}
		}

		return null;
	}

	/// <summary>某类目已上架的格数（烟测 / 界面计数用）。</summary>
	public static int CountInCategory(IReadOnlyList<RunMerchantStockEntrySave> stock, MerchantCategory category)
	{
		int count = 0;
		foreach (RunMerchantStockEntrySave entry in stock ?? Array.Empty<RunMerchantStockEntrySave>())
		{
			if (entry != null && entry.Category == (int)category)
			{
				count++;
			}
		}

		return count;
	}

	/// <summary>按权重抽一档；池中没有该档时按池里实际存在的档同权重再抽（不空转）。</summary>
	private static ItemRarity RollRarity(Random random, IReadOnlyList<MerchantStockCandidate> pool)
	{
		List<ItemRarity> available = new List<ItemRarity>();
		foreach (MerchantStockCandidate candidate in pool)
		{
			if (!available.Contains(candidate.Rarity))
			{
				available.Add(candidate.Rarity);
			}
		}

		int total = TotalWeight;
		int roll = random.Next(Math.Max(1, total));
		foreach ((ItemRarity rarity, int weight) in RarityWeights)
		{
			roll -= Math.Max(0, weight);
			if (roll < 0 && available.Contains(rarity))
			{
				return rarity;
			}
		}

		return available.Count == 0 ? ItemRarity.Common : available[random.Next(available.Count)];
	}

	/// <summary>在某档里均匀取一个（`rarity` 为 null = 全池均匀）。</summary>
	private static MerchantStockCandidate TakeUniform(Random random, IReadOnlyList<MerchantStockCandidate> pool, ItemRarity? rarity)
	{
		List<MerchantStockCandidate> matches = new List<MerchantStockCandidate>();
		foreach (MerchantStockCandidate candidate in pool)
		{
			if (rarity == null || candidate.Rarity == rarity.Value)
			{
				matches.Add(candidate);
			}
		}

		return matches.Count == 0 ? null : matches[random.Next(matches.Count)];
	}
}
