// EquipmentRecipeCatalog.cs
// 装备配方表（DataBase/Equipment/EquipmentRecipe.csv）的**纯逻辑**解析与模型（无 Godot 依赖，可 xUnit 直测）。
// 用途：村庄锻铁铺与商人锻造炉共用同一份配方的「材料需求 + 金币费用」查询
// （口径出处：README/施工文档/2026/2026.10/交互/锻铁铺交互案.md §三，商人侧金币 ×1.5 由 SmithyContext 注入）。
//
// 表头（固定 8 列，顺序即列序）：
//   RecipeId,ResultDefinitionId,Material1Id,Material1Count,Material2Id,Material2Count,Gold,Enabled
// 口径与物品四表一致：表头 / ID / 数值有误一律抛 FormatException，**不静默降级**。
// 只支持两种材料（现役 14 条配方的最多形态）；第三种的错误写法会因列数不符被拦下。
using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>DataBase/Equipment/EquipmentRecipe.csv 的一行。</summary>
public sealed class EquipmentRecipe
{
	public int RecipeId;
	public string ResultDefinitionId = string.Empty;
	public int Material1Id;
	public int Material1Count;
	/// <summary>第二材料（无则 0）。</summary>
	public int Material2Id;
	public int Material2Count;
	public int Gold;
	public bool Enabled = true;

	/// <summary>是否有第二材料。</summary>
	public bool HasSecondMaterial => Material2Id > 0 && Material2Count > 0;
}

public static class EquipmentRecipeCatalog
{
	/// <summary>表头（顺序即列序）。</summary>
	public static readonly string[] Header =
	{
		"RecipeId", "ResultDefinitionId", "Material1Id", "Material1Count", "Material2Id", "Material2Count", "Gold", "Enabled",
	};

	/// <summary>该行是否是配方表表头（首列 `RecipeId`）。判表用「含表头的行」，不要用跳过表头的读取结果。</summary>
	public static bool IsRecipeTableHeader(IReadOnlyList<string> cells) =>
		cells != null && cells.Count > 0 && (cells[0] ?? string.Empty).Trim().Equals("RecipeId", StringComparison.OrdinalIgnoreCase);

	/// <summary>按固定列序校验表头；不符抛 <see cref="FormatException"/>。</summary>
	public static void ValidateHeader(IReadOnlyList<string> cells)
	{
		if (cells == null || cells.Count != Header.Length)
		{
			throw new FormatException($"[EquipmentRecipe] 表头列数不符：期望 {Header.Length} 列，实际 {(cells?.Count ?? 0)} 列");
		}

		for (int i = 0; i < Header.Length; i++)
		{
			if (!string.Equals((cells[i] ?? string.Empty).Trim(), Header[i], StringComparison.OrdinalIgnoreCase))
			{
				throw new FormatException($"[EquipmentRecipe] 第 {i + 1} 列表头应为 `{Header[i]}`，实际 `{(cells[i] ?? string.Empty).Trim()}`");
			}
		}
	}

	/// <summary>解析一行（已拆列的字段，不含表头）；**列数必须正好 8 列**（多一列 = 有人加了第三种材料却没改加载器），数值不合法同样抛 <see cref="FormatException"/>。</summary>
	public static EquipmentRecipe ParseFields(IReadOnlyList<string> cells, string context = "[EquipmentRecipe]")
	{
		if (cells == null || cells.Count != Header.Length)
		{
			throw new FormatException($"{context}：列数不符（期望 {Header.Length} 列，实际 {(cells?.Count ?? 0)} 列）");
		}

		string rowContext = $"{context} 行（{string.Join(",", cells)}）";
		EquipmentRecipe recipe = new EquipmentRecipe
		{
			RecipeId = ParsePositiveInt(cells[0], rowContext, "RecipeId"),
			ResultDefinitionId = ParseName(cells[1], rowContext),
			Material1Id = ParseNonNegativeInt(cells[2], rowContext, "Material1Id"),
			Material1Count = ParseNonNegativeInt(cells[3], rowContext, "Material1Count"),
			Material2Id = ParseNonNegativeInt(cells[4], rowContext, "Material2Id"),
			Material2Count = ParseNonNegativeInt(cells[5], rowContext, "Material2Count"),
			Gold = ParseNonNegativeInt(cells[6], rowContext, "Gold"),
			Enabled = ParseBool(cells[7], rowContext),
		};

		if (recipe.Material1Id <= 0 || recipe.Material1Count <= 0)
		{
			throw new FormatException($"{rowContext}：第一条材料必须同时给出 Material1Id 与 Material1Count（正数）");
		}

		if (recipe.HasSecondMaterial == false && (recipe.Material2Id != 0 || recipe.Material2Count != 0))
		{
			throw new FormatException($"{rowContext}：第二条材料必须 Id 与 Count 同时给（或同时留空）");
		}

		return recipe;
	}

	/// <summary>解析整表（传入**含表头**的行序列），返回按 RecipeId 升序的配方表。</summary>
	public static List<EquipmentRecipe> ParseLines(IEnumerable<string> lines)
	{
		List<EquipmentRecipe> recipes = new List<EquipmentRecipe>();
		bool headerSeen = false;
		int lineNumber = 0;
		foreach (string line in lines ?? Array.Empty<string>())
		{
			lineNumber++;
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] cells = line.Split(',');
			if (!headerSeen)
			{
				ValidateHeader(cells);
				headerSeen = true;
				continue;
			}

			recipes.Add(ParseFields(cells, $"[EquipmentRecipe] 第 {lineNumber} 行"));
		}

		if (!headerSeen)
		{
			throw new FormatException("[EquipmentRecipe] 缺表头（首行应为 RecipeId,...）");
		}

		recipes.Sort((a, b) => a.RecipeId.CompareTo(b.RecipeId));
		return recipes;
	}

	private static string ParseName(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			throw new FormatException($"{context}：ResultDefinitionId 不得为空");
		}

		return text;
	}

	private static int ParsePositiveInt(string raw, string context, string column)
	{
		int value = ParseNonNegativeInt(raw, context, column);
		if (value <= 0)
		{
			throw new FormatException($"{context}：{column} 必须是正整数，实际 `{raw}`");
		}

		return value;
	}

	private static int ParseNonNegativeInt(string raw, string context, string column)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return 0;
		}

		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
		{
			throw new FormatException($"{context}：{column} 必须是非负整数，实际 `{text}`");
		}

		return value;
	}

	private static bool ParseBool(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || text.Equals("1", StringComparison.Ordinal))
		{
			return true;
		}

		if (text.Equals("FALSE", StringComparison.OrdinalIgnoreCase) || text.Equals("0", StringComparison.Ordinal))
		{
			return false;
		}

		throw new FormatException($"{context}：Enabled 必须是 TRUE / FALSE，实际 `{text}`");
	}
}
