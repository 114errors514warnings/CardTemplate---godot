// SmithyCrafting.cs
// 锻铁铺 / 商人锻造炉共用的打造**纯逻辑**（无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/锻铁铺交互案.md §一 §二 §三 §四 §五 §2.1（复用接口）
//           与 商人交互案.md §4.9 / §5.3（商人侧：1 次、金币 ×1.5 并向上取整到 5 的倍数）。
// 分工：配方查询走 `EquipmentRecipeCatalog`，次数与金币系数走这里；界面只读返回值，不自己算价。
using System;
using System.Collections.Generic;

/// <summary>一条材料需求（界面右列的「名称 ×N（持有 M）」，不足标红）。</summary>
public sealed class SmithyRequirement
{
	public int MaterialId;
	public string DisplayName = string.Empty;
	public int Need;
	public int Have;

	/// <summary>是否满足（不足时界面标红）。</summary>
	public bool Satisfied => Have >= Need;
}

public static class SmithyCrafting
{
	/// <summary>村庄锻铁铺每次进入可打造次数（锻铁铺案 §一）。</summary>
	public const int VillageMaxCrafts = 2;

	/// <summary>商人锻造炉每次打开可打造次数（商人案 §十 第 13 条）。</summary>
	public const int MerchantMaxCrafts = 1;

	/// <summary>村庄锻铁铺的金币系数（×1）。</summary>
	public const float VillageGoldMultiplier = 1.0f;

	/// <summary>商人锻造炉的金币系数（×1.5，商人工匠费）。</summary>
	public const float MerchantGoldMultiplier = 1.5f;

	/// <summary>每次进入的时间点代价（锻铁铺案 §一；商人侧为 0，由调用方决定是否扣）。</summary>
	public const float VisitTimePointCost = 1f;

	/// <summary>时间点不足（锻铁铺案 §五）。</summary>
	public static string TimePointShortText(float need, float have) =>
		$"时间点不足：需要 {RunTimePoints.Format(need)}，当前剩余 {RunTimePoints.Format(have)}。";

	/// <summary>本次次数用尽（锻铁铺案 §五）。</summary>
	public const string CraftsExhaustedText = "本次进入的打造次数已用尽，可再次进入（消耗 1 时间点）。";

	/// <summary>背包超载（与商人 / 背包同一句式）。</summary>
	public const string BagOverloadText = "背包已超载，请先整理。";

	/// <summary>金币不足（锻铁铺案 §五）。</summary>
	public static string GoldShortText(int need, int have) => $"金币不足：需要 {need}，当前 {have}。";

	/// <summary>材料不足（锻铁铺案 §五：点名缺的材料与数量）。</summary>
	public static string MaterialShortText(string displayName, int need, int have) =>
		$"材料不足：{displayName} 需要 {need}，持有 {have}。";

	/// <summary>打造成功（锻铁铺案 §四 第 3 步的提示行）。</summary>
	public static string CraftedText(string resultDefinitionId) => $"打造完成：{resultDefinitionId}";

	/// <summary>单件金币费用：配方金币 × 系数，**向上取整到 5 的倍数**（§5.3 的 20→30 / 40→60 / 60→90 / 80→120）。</summary>
	public static int GoldFor(int recipeGold, float multiplier)
	{
		double raw = Math.Max(0, recipeGold) * Math.Max(0f, multiplier);
		return RoundUpToFive(raw);
	}

	/// <summary>向上取整到 5 的倍数（容差 1e-6：`40 × 1.5 = 60` 不因浮点误差被抬到 65）。</summary>
	public static int RoundUpToFive(double value)
	{
		if (value <= 0)
		{
			return 0;
		}

		return (int)(Math.Ceiling(value / 5.0 - 1e-6) * 5.0);
	}

	/// <summary>把配方摊成材料需求清单（名称与持有数由调用方给；第二材料为空时只出一条）。</summary>
	public static List<SmithyRequirement> Requirements(
		EquipmentRecipe recipe,
		Func<int, string> nameOf,
		Func<int, int> countOf)
	{
		List<SmithyRequirement> requirements = new List<SmithyRequirement>();
		if (recipe == null)
		{
			return requirements;
		}

		requirements.Add(MakeRequirement(recipe.Material1Id, recipe.Material1Count, nameOf, countOf));
		if (recipe.HasSecondMaterial)
		{
			requirements.Add(MakeRequirement(recipe.Material2Id, recipe.Material2Count, nameOf, countOf));
		}

		return requirements;
	}

	/// <summary>
	/// 打造校验（锻铁铺案 §四 第 2 步 / §五）：**空串 = 可以打造**。
	/// 顺序 = 本次次数 → 材料 → 金币；背包可入账由调用方在扣料前查（界面用它决定按钮禁用）。
	/// </summary>
	public static string Validate(int remainingCrafts, int gold, int price, IReadOnlyList<SmithyRequirement> requirements)
	{
		if (remainingCrafts <= 0)
		{
			return CraftsExhaustedText;
		}

		foreach (SmithyRequirement requirement in requirements ?? Array.Empty<SmithyRequirement>())
		{
			if (requirement != null && !requirement.Satisfied)
			{
				return MaterialShortText(requirement.DisplayName, requirement.Need, requirement.Have);
			}
		}

		return gold >= price ? string.Empty : GoldShortText(price, gold);
	}

	/// <summary>扣材料（按材料实例数扣；不做「不足」判断 —— 校验已在 `Validate` 里过）。</summary>
	public static void Consume(IReadOnlyList<SmithyRequirement> requirements, Action<int, int> consumeMaterial)
	{
		foreach (SmithyRequirement requirement in requirements ?? Array.Empty<SmithyRequirement>())
		{
			if (requirement != null && requirement.Need > 0)
			{
				consumeMaterial(requirement.MaterialId, requirement.Need);
			}
		}
	}

	private static SmithyRequirement MakeRequirement(int materialId, int need, Func<int, string> nameOf, Func<int, int> countOf) =>
		new SmithyRequirement
		{
			MaterialId = materialId,
			DisplayName = nameOf?.Invoke(materialId) ?? materialId.ToString(),
			Need = need,
			Have = countOf?.Invoke(materialId) ?? 0,
		};
}
