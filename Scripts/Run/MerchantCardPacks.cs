// MerchantCardPacks.cs
// 商人**卡包快照**生成的纯逻辑（无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/商人交互案.md §4.2 / §4.3 / §5.1 / §七。
//   · 5 个包：1–3 = 槽位角色专属池（`Character`，固定写回本槽位）；4 = 三槽位混合（`Mixed`）；5 = 通用池（`Generic`）；
//   · 包 4 / 5 买时**由玩家点选**目标槽位（`OwnerSlotPolicy = Chosen`，快照里 `OwnerSlot = -1`）；
//   · **第 5 包构成硬口径**：3 张 A 级 + 2 张 S 级，全部取自通用卡池；
//   · 其余包按**等级权重**抽（权重表 `MerchantCardPack.csv`），包内不重复，且**不出现状态牌**（`CardType = State`）；
//   · 快照一次性生成后固定：不刷新、不补位、买走的那张不再上架（`Sold`）；价格取 `MerchantCardPrice.csv`（按等级），
//     等级缺价 → 该卡**不上架**（不静默取别的价，与货架同纪律）。
// 分工：本文件只生成/查询**快照**；金币、卡组写入、落档在 `RunSession.Merchant.cs`；界面只读快照。
using System;
using System.Collections.Generic;
using CardSimulator;

/// <summary>卡包候选卡（运行期由卡表构造；纯逻辑只吃这个 DTO）。</summary>
public sealed class MerchantCardCandidate
{
	public int CardId;
	public CardTier Tier = CardTier.None;

	/// <summary>是否状态牌（`CardCategory.State`）—— 商人卡包**不上架**状态牌（§4.2）。</summary>
	public bool IsState;

	public string Name = string.Empty;
}

public static class MerchantCardPacks
{
	/// <summary>第 5 包（通用牌包）的构成：3 张 A 级 + 2 张 S 级（用户硬口径，§4.2）。</summary>
	public const int GenericPackACount = 3;

	public const int GenericPackSCount = 2;

	/// <summary>
	/// 生成 5 个卡包快照。
	/// `slotPools` 索引 = 槽位（0–2）；`genericPool` = 通用卡池；`prices` = `MerchantCardPrice.csv` 的（等级 → 价）。
	/// </summary>
	public static List<RunMerchantCardPackSave> Generate(
		Random random,
		IReadOnlyList<MerchantPackRow> packs,
		IReadOnlyList<IReadOnlyList<MerchantCardCandidate>> slotPools,
		IReadOnlyList<MerchantCardCandidate> genericPool,
		IReadOnlyDictionary<CardTier, int> prices)
	{
		List<RunMerchantCardPackSave> result = new List<RunMerchantCardPackSave>();
		if (packs == null)
		{
			return result;
		}

		random ??= new Random(0);
		List<MerchantPackRow> ordered = new List<MerchantPackRow>(packs);
		ordered.Sort((a, b) => a.PackIndex.CompareTo(b.PackIndex));
		foreach (MerchantPackRow pack in ordered)
		{
			if (pack == null)
			{
				continue;
			}

			int ownerSlot = pack.Kind == MerchantPackKind.Character ? pack.PackIndex - 1 : -1;
			List<MerchantCardCandidate> pool = pack.Kind switch
			{
				MerchantPackKind.Character => Clean(SlotPool(slotPools, ownerSlot)),
				MerchantPackKind.Mixed => Clean(Union(slotPools)),
				_ => Clean(genericPool),
			};

			List<MerchantCardCandidate> picked = pack.Kind == MerchantPackKind.Generic
				? PickGenericPack(random, pool, pack.CardCount)
				: PickByTier(random, pool, pack.CardCount, pack.TierWeights);

			RunMerchantCardPackSave save = new RunMerchantCardPackSave
			{
				PackIndex = pack.PackIndex,
				Kind = (int)pack.Kind,
				Policy = (int)pack.Policy,
				OwnerSlot = ownerSlot,
			};

			foreach (MerchantCardCandidate candidate in picked)
			{
				int price = PriceFor(prices, candidate.Tier);
				if (price < 0)
				{
					continue; // 等级缺价：不上架（不静默取别的价）。
				}

				save.Cards.Add(new RunMerchantCardEntrySave
				{
					CardId = candidate.CardId,
					Tier = (int)candidate.Tier,
					Price = price,
					Sold = false,
					GrantedSlot = -1,
				});
			}

			result.Add(save);
		}

		return result;
	}

	/// <summary>按等级取卡价（`MerchantCardPrice.csv`）；没有该等级返回 -1。</summary>
	public static int PriceFor(IReadOnlyDictionary<CardTier, int> prices, CardTier tier) =>
		prices != null && prices.TryGetValue(tier, out int price) ? price : -1;

	/// <summary>找某包的快照（找不到返回 null）。</summary>
	public static RunMerchantCardPackSave FindPack(IReadOnlyList<RunMerchantCardPackSave> packs, int packIndex)
	{
		foreach (RunMerchantCardPackSave pack in packs ?? Array.Empty<RunMerchantCardPackSave>())
		{
			if (pack != null && pack.PackIndex == packIndex)
			{
				return pack;
			}
		}

		return null;
	}

	/// <summary>该包还剩几张可买（界面的 `余 N / M`）。</summary>
	public static int RemainingCount(RunMerchantCardPackSave pack)
	{
		if (pack?.Cards == null)
		{
			return 0;
		}

		int left = 0;
		foreach (RunMerchantCardEntrySave card in pack.Cards)
		{
			if (card != null && !card.Sold)
			{
				left++;
			}
		}

		return left;
	}

	/// <summary>第 5 包的硬构成：3 张 A + 2 张 S；A / S 不足时用池里其余卡补足（不静默少卖）。</summary>
	private static List<MerchantCardCandidate> PickGenericPack(Random random, List<MerchantCardCandidate> pool, int count)
	{
		List<MerchantCardCandidate> picked = new List<MerchantCardCandidate>();
		List<MerchantCardCandidate> remaining = new List<MerchantCardCandidate>(pool);
		TakeTier(random, remaining, picked, CardTier.A, Math.Min(GenericPackACount, count));
		TakeTier(random, remaining, picked, CardTier.S, Math.Min(GenericPackSCount, Math.Max(0, count - picked.Count)));

		// 兜底：通用池里 A / S 不够 5 张时，把空位用池里剩下的卡补满（不静默留空位）。
		while (picked.Count < count && remaining.Count > 0)
		{
			int index = random.Next(remaining.Count);
			picked.Add(remaining[index]);
			remaining.RemoveAt(index);
		}

		return picked;
	}

	/// <summary>按等级权重抽；同包不重复。缺等级数据（`None`）的卡按「不过滤等级」处理（§十 第 9 / 10 条）。</summary>
	private static List<MerchantCardCandidate> PickByTier(
		Random random,
		List<MerchantCardCandidate> pool,
		int count,
		IReadOnlyDictionary<CardTier, int> weights)
	{
		List<MerchantCardCandidate> picked = new List<MerchantCardCandidate>();
		List<MerchantCardCandidate> remaining = new List<MerchantCardCandidate>(pool);
		while (picked.Count < count && remaining.Count > 0)
		{
			CardTier tier = RollTier(random, weights);
			List<MerchantCardCandidate> matches = remaining.FindAll(x => x.Tier == tier);
			if (matches.Count == 0)
			{
				// 该档在这张表里没有：先退「没有等级数据的卡」，再退整池（都不空转）。
				matches = remaining.FindAll(x => x.Tier == CardTier.None);
				if (matches.Count == 0)
				{
					matches = remaining;
				}
			}

			MerchantCardCandidate chosen = matches[random.Next(matches.Count)];
			picked.Add(chosen);
			remaining.Remove(chosen);
		}

		return picked;
	}

	/// <summary>按权重表抽一档；权重全 0 / 缺表时退化为「五档均匀」（都不空转）。</summary>
	private static CardTier RollTier(Random random, IReadOnlyDictionary<CardTier, int> weights)
	{
		CardTier[] order = { CardTier.D, CardTier.C, CardTier.B, CardTier.A, CardTier.S };
		int total = 0;
		foreach (CardTier tier in order)
		{
			total += WeightOf(weights, tier);
		}

		if (total <= 0)
		{
			return order[random.Next(order.Length)];
		}

		int roll = random.Next(total);
		foreach (CardTier tier in order)
		{
			roll -= WeightOf(weights, tier);
			if (roll < 0)
			{
				return tier;
			}
		}

		return CardTier.D;
	}

	private static int WeightOf(IReadOnlyDictionary<CardTier, int> weights, CardTier tier) =>
		weights != null && weights.TryGetValue(tier, out int weight) ? Math.Max(0, weight) : 0;

	private static void TakeTier(
		Random random,
		List<MerchantCardCandidate> remaining,
		List<MerchantCardCandidate> picked,
		CardTier tier,
		int want)
	{
		for (int i = 0; i < want; i++)
		{
			List<MerchantCardCandidate> matches = remaining.FindAll(x => x.Tier == tier);
			if (matches.Count == 0)
			{
				return;
			}

			MerchantCardCandidate chosen = matches[random.Next(matches.Count)];
			picked.Add(chosen);
			remaining.Remove(chosen);
		}
	}

	private static IReadOnlyList<MerchantCardCandidate> SlotPool(
		IReadOnlyList<IReadOnlyList<MerchantCardCandidate>> slotPools, int slot) =>
		slotPools != null && slot >= 0 && slot < slotPools.Count && slotPools[slot] != null
			? slotPools[slot]
			: Array.Empty<MerchantCardCandidate>();

	private static List<MerchantCardCandidate> Union(IReadOnlyList<IReadOnlyList<MerchantCardCandidate>> slotPools)
	{
		List<MerchantCardCandidate> union = new List<MerchantCardCandidate>();
		if (slotPools == null)
		{
			return union;
		}

		foreach (IReadOnlyList<MerchantCardCandidate> pool in slotPools)
		{
			foreach (MerchantCardCandidate candidate in pool ?? Array.Empty<MerchantCardCandidate>())
			{
				if (candidate != null && !union.Exists(x => x.CardId == candidate.CardId))
				{
					union.Add(candidate);
				}
			}
		}

		return union;
	}

	/// <summary>去重 + 去状态牌（`CardCategory.State` 不上架，§4.2）。</summary>
	private static List<MerchantCardCandidate> Clean(IReadOnlyList<MerchantCardCandidate> pool)
	{
		List<MerchantCardCandidate> clean = new List<MerchantCardCandidate>();
		foreach (MerchantCardCandidate candidate in pool ?? Array.Empty<MerchantCardCandidate>())
		{
			if (candidate == null || candidate.IsState || clean.Exists(x => x.CardId == candidate.CardId))
			{
				continue;
			}

			clean.Add(candidate);
		}

		return clean;
	}
}
