// ItemCsvSchema.cs
// 物品四表（Material / Item / Food / FoodRecipe）的共用列解析与表头校验。
// 口径：UTF-8 CSV、首行英文表头、ID 不得有空格、空值留空（不写 None）——见 配表规范.md。
// 解析失败一律抛 FormatException 并带「表 + 行」上下文：**未定义 ID / 非法枚举必须报错，不静默降级**（P1-4 验收）。
using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

public static class ItemCsvSchema
{
	/// <summary>
	/// 校验表头：列数一致且每列名匹配（大小写不敏感、去 BOM 与空格）。
	/// 返回数据行（已跳过表头）；表头不符时抛 <see cref="FormatException"/>。
	/// </summary>
	public static string[] LoadValidatedDataLines(string filePath, string tableName, params string[] expectedColumns)
	{
		string[] allLines = LoadCsv.LoadCSVLines(filePath);
		if (allLines.Length == 0)
		{
			throw new FormatException($"[{tableName}] 表为空或无法读取：{filePath}");
		}

		string[] header = LoadCsv.ParseCSVFields(allLines[0].TrimStart('\uFEFF'));
		if (header.Length != expectedColumns.Length)
		{
			throw new FormatException($"[{tableName}] 表头列数不符：期望 {expectedColumns.Length} 列" +
				$"（{string.Join(",", expectedColumns)}），实际 {header.Length} 列（{allLines[0]}）");
		}

		for (int i = 0; i < expectedColumns.Length; i++)
		{
			if (!string.Equals(header[i].Trim(), expectedColumns[i], StringComparison.OrdinalIgnoreCase))
			{
				throw new FormatException($"[{tableName}] 第 {i + 1} 列表头应为 `{expectedColumns[i]}`，实际 `{header[i].Trim()}`");
			}
		}

		string[] dataLines = new string[allLines.Length - 1];
		Array.Copy(allLines, 1, dataLines, 0, allLines.Length - 1);
		return dataLines;
	}

	/// <summary>稀有度：普通 / 罕见 / 稀有（兼容 Common / Uncommon / Rare）。</summary>
	public static ItemRarity ParseRarity(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Equals("普通", StringComparison.Ordinal) || text.Equals("Common", StringComparison.OrdinalIgnoreCase))
		{
			return ItemRarity.Common;
		}
		if (text.Equals("罕见", StringComparison.Ordinal) || text.Equals("Uncommon", StringComparison.OrdinalIgnoreCase))
		{
			return ItemRarity.Uncommon;
		}
		if (text.Equals("稀有", StringComparison.Ordinal) || text.Equals("Rare", StringComparison.OrdinalIgnoreCase))
		{
			return ItemRarity.Rare;
		}

		throw new FormatException($"{context}：稀有度不认识：`{text}`（可用：普通 / 罕见 / 稀有）");
	}

	/// <summary>材料类别：合成材料 / 打造材料。</summary>
	public static MaterialCategory ParseMaterialCategory(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Equals("合成材料", StringComparison.Ordinal) || text.Equals("Craft", StringComparison.OrdinalIgnoreCase))
		{
			return MaterialCategory.Craft;
		}
		if (text.Equals("打造材料", StringComparison.Ordinal) || text.Equals("Forge", StringComparison.OrdinalIgnoreCase))
		{
			return MaterialCategory.Forge;
		}

		throw new FormatException($"{context}：材料类别不认识：`{text}`（可用：合成材料 / 打造材料）");
	}

	/// <summary>道具生效范围：使用后立即 / 战斗内 / 持续。</summary>
	public static ItemUseScope ParseUseScope(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Equals("使用后立即", StringComparison.Ordinal) || text.Equals("Immediate", StringComparison.OrdinalIgnoreCase))
		{
			return ItemUseScope.Immediate;
		}
		if (text.Equals("战斗内", StringComparison.Ordinal) || text.Equals("InBattle", StringComparison.OrdinalIgnoreCase))
		{
			return ItemUseScope.InBattle;
		}
		if (text.Equals("持续", StringComparison.Ordinal) || text.Equals("Persistent", StringComparison.OrdinalIgnoreCase))
		{
			return ItemUseScope.Persistent;
		}

		throw new FormatException($"{context}：生效范围不认识：`{text}`（可用：使用后立即 / 战斗内 / 持续）");
	}

	/// <summary>配方输入来源：Food / Material（2026-10-02 起只有 Food 放行）。</summary>
	public static RecipeInputKind ParseRecipeInputKind(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Equals("Food", StringComparison.OrdinalIgnoreCase) || text.Equals("食物", StringComparison.Ordinal))
		{
			return RecipeInputKind.Food;
		}
		if (text.Equals("Material", StringComparison.OrdinalIgnoreCase) || text.Equals("材料", StringComparison.Ordinal))
		{
			return RecipeInputKind.Material;
		}

		throw new FormatException($"{context}：配方输入来源不认识：`{text}`（可用：Food / Material）");
	}

	/// <summary>布尔：TRUE / FALSE（兼容 1 / 0、是 / 否）。</summary>
	public static bool ParseBool(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return false;
		}
		if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || text.Equals("1", StringComparison.Ordinal) ||
			text.Equals("是", StringComparison.Ordinal))
		{
			return true;
		}
		if (text.Equals("FALSE", StringComparison.OrdinalIgnoreCase) || text.Equals("0", StringComparison.Ordinal) ||
			text.Equals("否", StringComparison.Ordinal))
		{
			return false;
		}

		throw new FormatException($"{context}：布尔值不认识：`{text}`（可用：TRUE / FALSE）");
	}

	/// <summary>非负整数（空 = <paramref name="fallback"/>，默认 0）。</summary>
	public static int ParseNonNegativeInt(string raw, string context, int fallback = 0)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return fallback;
		}
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
		{
			throw new FormatException($"{context}：必须是非负整数：`{text}`");
		}

		return value;
	}

	/// <summary>正整数（空 = <paramref name="fallback"/>，默认 1）。</summary>
	public static int ParsePositiveInt(string raw, string context, int fallback = 1)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return fallback;
		}
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
		{
			throw new FormatException($"{context}：必须是正整数：`{text}`");
		}

		return value;
	}

	/// <summary>非负小数（空 = 0）：负荷列用；固定不变文化解析，避免小数点随区域漂移。</summary>
	public static float ParseNonNegativeFloat(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return 0f;
		}
		if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value < 0f)
		{
			throw new FormatException($"{context}：必须是非负小数：`{text}`");
		}

		return value;
	}

	/// <summary>ID 列：正整数且**不得含空格**（配表规范 §目录与总表）。</summary>
	public static int ParseId(string raw, string context)
	{
		string text = raw ?? string.Empty;
		if (text.Length != text.Trim().Length)
		{
			throw new FormatException($"{context}：ID 不得含空格：`{text}`");
		}
		if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
		{
			throw new FormatException($"{context}：ID 必须是正整数：`{text}`");
		}

		return value;
	}

	/// <summary>名称列：非空。</summary>
	public static string ParseName(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			throw new FormatException($"{context}：名称列不得为空");
		}

		return text;
	}

	/// <summary>逐行解析并带上「表名 + 行号」上下文；空行跳过。</summary>
	public static void ParseRowsOrThrow(string tableName, string[] dataLines, Action<int, string, string[]> parseRow)
	{
		for (int i = 0; i < dataLines.Length; i++)
		{
			if (string.IsNullOrWhiteSpace(dataLines[i]))
			{
				continue;
			}

			string[] fields = LoadCsv.ParseCSVFields(dataLines[i]);
			parseRow(i, $"[{tableName}] 第 {i + 2} 行（{dataLines[i]}）", fields);
		}
	}

	/// <summary>同一表内的 ID 去重检查：重复即报错。</summary>
	public static void EnsureUnique<TKey>(Dictionary<TKey, string> seen, TKey key, string context)
	{
		if (seen.ContainsKey(key))
		{
			throw new FormatException($"{context}：ID 重复（已由 {seen[key]} 使用）：{key}");
		}

		seen[key] = context;
	}

	/// <summary>取列（越界返回空串）：列数不足时由各表按需报错。</summary>
	public static string Field(string[] fields, int index) => fields != null && index >= 0 && index < fields.Length
		? fields[index].Trim()
		: string.Empty;
}
