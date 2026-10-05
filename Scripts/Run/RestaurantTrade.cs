// RestaurantTrade.cs
// 村庄餐厅的**纯逻辑**：买 / 卖价格、现做标记、防套利校验、每次进入的烹饪次数（无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/餐厅交互案.md §一 §三 §五 §九。
//   · 购买价与商人**共用一张表**（`MerchantPrice.csv` 的 `Category=Food` 行，餐厅案 §三 / 待拍板第 1 条）；
//   · 出售价 = 购买价 × 40%（取整）；**现做**再 ×1.5（普通 20 → 8 / 12，罕见 40 → 16 / 24，稀有 80 → 32 / 48）；
//   · 防套利硬口径：现做卖价（60%）仍低于购买价（100%）—— 买进来再卖掉一定亏（§五）。
using System;
using System.Collections.Generic;
using CardSimulator;

public static class RestaurantTrade
{
	/// <summary>每次进入可烹饪次数（餐厅案 §一 / 待拍板第 6 条）。</summary>
	public const int CooksPerVisit = 2;

	/// <summary>每次进入的时间点代价（餐厅案 §一）。</summary>
	public const float VisitTimePointCost = 1f;

	/// <summary>出售比例（餐厅案 §五：购买价 × 40%）。</summary>
	public const float SellRatio = 0.4f;

	/// <summary>现做加成（餐厅案 §五：出售价 ×1.5）。</summary>
	public const float FreshBonus = 1.5f;

	/// <summary>时间点不足（餐厅案 §九 第 1 条）。</summary>
	public static string TimePointShortText(float need, float have) =>
		$"时间点不足：需要 {RunTimePoints.Format(need)}，当前剩余 {RunTimePoints.Format(have)}。";

	/// <summary>本次烹饪次数已满（餐厅案 §四）。</summary>
	public const string CooksExhaustedText = "本次进入的烹饪次数已用尽，可再次进入（消耗 1 时间点）。";

	/// <summary>材料不足（与锻铁铺同一句式）。</summary>
	public static string MaterialShortText(string displayName, int need, int have) =>
		$"材料不足：{displayName} 需要 {need}，持有 {have}。";

	/// <summary>金币不足。</summary>
	public static string GoldShortText(int need, int have) => $"金币不足：需要 {need}，当前 {have}。";

	/// <summary>已售出。</summary>
	public const string SoldOutText = "该商品已售出。";

	/// <summary>背包超载。</summary>
	public const string BagOverloadText = "背包已超载，请先整理。";

	/// <summary>该行是背包里的食物、不在此出售（只收食物 —— 本常量给非食物行用）。</summary>
	public const string OnlyFoodText = "这里只收食物。";

	/// <summary>出售价：`floor(购买价 × 40%)`；现做再乘 1.5（整数取整，与案里的 8 / 12、16 / 24、32 / 48 一致）。</summary>
	public static int SellPrice(int buyPrice, bool fresh)
	{
		if (buyPrice <= 0)
		{
			return 0;
		}

		int sell = (int)Math.Floor(buyPrice * (double)SellRatio + 1e-6);
		return fresh ? (int)Math.Floor(sell * (double)FreshBonus + 1e-6) : sell;
	}

	/// <summary>防套利判据：**现做卖价必须严格低于购买价**（餐厅案 §五：买进来再卖掉一定亏）。</summary>
	public static bool IsArbitrageFree(int buyPrice) => SellPrice(buyPrice, true) < buyPrice;

	/// <summary>可以烹饪：本次次数未用尽 + 材料齐 + 背包可入账（背包项由调用方折算成 `bagAccepts`）。</summary>
	public static string ValidateCook(
		int remainingCooks,
		IReadOnlyList<SmithyRequirement> requirements,
		bool bagAccepts,
		int gold = 0,
		int price = 0)
	{
		if (remainingCooks <= 0)
		{
			return CooksExhaustedText;
		}

		foreach (SmithyRequirement requirement in requirements ?? Array.Empty<SmithyRequirement>())
		{
			if (requirement != null && !requirement.Satisfied)
			{
				return MaterialShortText(requirement.DisplayName, requirement.Need, requirement.Have);
			}
		}

		if (price > 0 && gold < price)
		{
			return GoldShortText(price, gold);
		}

		return bagAccepts ? string.Empty : BagOverloadText;
	}

	/// <summary>
	/// 「现做」标记集合（餐厅案 §五）：按作用域内的**食物实例 Id** 记账，离开餐厅即失效
	/// （待拍板第 3 条默认值）—— 宿主在 `Close` 时调 `Clear()`，不需要写进物品表。
	/// </summary>
	public sealed class FreshMarks
	{
		private readonly HashSet<string> fresh = new HashSet<string>(StringComparer.Ordinal);

		public int Count => fresh.Count;

		/// <summary>本次进入餐厅烹饪出的那一个实例打标。</summary>
		public void Mark(string instanceId)
		{
			if (!string.IsNullOrEmpty(instanceId))
			{
				fresh.Add(instanceId);
			}
		}

		/// <summary>该实例是否现做（决定卖价是否 ×1.5，以及行上是否显示「现做」标）。</summary>
		public bool IsFresh(string instanceId) => !string.IsNullOrEmpty(instanceId) && fresh.Contains(instanceId);

		/// <summary>离开餐厅即失效（本批默认；改成当日有效时改调用点即可）。</summary>
		public void Clear() => fresh.Clear();
	}
}
