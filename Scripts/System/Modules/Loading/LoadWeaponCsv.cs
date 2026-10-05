// LoadWeaponCsv.cs
// 解析 DataBase/Equipment/Weapon.csv 的**局外侧**视图（背包 / 装备界面：P0-17 / P0-18）。
// 表头（配表规范「装备数据」节）：WeaponId,DefinitionId,HandsRequired,AttackMode,AttackRange,DefenseValue,
//   DamageBonus,MoveBonus,ResourceCost,BlocksDefenseShield,EquipmentType[,Load]
// 本加载器只吃局外要用的列（ID / 名字 / 手数 / 关键数值 / 类型 / 可选负荷），**战斗侧**的 AttackMode 与射程
// 解析仍在 `BattleWeaponCatalog`（同一张表两个视图，各行都不复制数据）。
// 宽容口径：允许表尾追加列（`Load` 就靠这个通道接入）；但前三列列名、ID / 名字唯一性、手数越界、
// 未知 `EquipmentType` 一律抛 FormatException（与物品四表同口径：不静默降级）。
using System;
using System.Collections.Generic;
using System.Globalization;
using CardSimulator.Battlefield;
using Godot;

public static class LoadWeaponCsv
{
	private const string TableName = "Weapon";

	/// <summary>本加载器**必须**认得的列（其余列（含战斗侧与后续新增列）一律忽略）。</summary>
	private static readonly string[] RequiredColumns = { "WeaponId", "DefinitionId", "HandsRequired" };

	/// <summary>`EquipmentType` 所在的列序（0 起）：Melee / Ranged / Armor，留空按 AttackMode 推导（与战斗侧同规则）。</summary>
	private const int EquipmentTypeColumn = 10;

	// `Load` 与 `Rarity`（2026-10-05）都**按表头定位**（本表允许表尾追加列）：
	// 此前 `Load` 写死下标 11，而 Weapon.csv 原本**没有** Load 列 → 追加 `Rarity` 后下标 11 被当 Load 解析（整表加载抛错）。
	// 缺列时分别按 -1 处理：Load = 「按手数推导」、Rarity = 「普通」。

	public static Dictionary<int, WeaponDefinition> LoadFromCSV(string filePath)
	{
		Dictionary<int, WeaponDefinition> result = new Dictionary<int, WeaponDefinition>();
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return result;
		}

		string[] allLines = LoadCsv.LoadCSVLines(filePath);
		if (allLines.Length == 0)
		{
			throw new FormatException($"[{TableName}] 表为空或无法读取：{filePath}");
		}

		string[] header = LoadCsv.ParseCSVFields(allLines[0].TrimStart('\uFEFF'));
		if (header.Length < RequiredColumns.Length)
		{
			throw new FormatException($"[{TableName}] 表头列数不足：至少 {RequiredColumns.Length} 列（{string.Join(",", RequiredColumns)}），实际 {header.Length} 列");
		}

		for (int i = 0; i < RequiredColumns.Length; i++)
		{
			if (!string.Equals(header[i].Trim(), RequiredColumns[i], StringComparison.OrdinalIgnoreCase))
			{
				throw new FormatException($"[{TableName}] 第 {i + 1} 列表头应为 `{RequiredColumns[i]}`，实际 `{header[i].Trim()}`");
			}
		}

		// `Rarity` 与 `Load` 列（2026-10-05 阻断项清理）：都按**表头**定位 —— 本加载器允许表尾追加列，因此不硬编码列序；
		// 缺列 / 留空分别按「普通」与「按手数推导」处理。
		int rarityColumn = ResolveColumnIndex(header, "Rarity");
		int loadColumn = ResolveColumnIndex(header, "Load");

		Dictionary<int, string> seenIds = new Dictionary<int, string>();
		Dictionary<string, string> seenNames = new Dictionary<string, string>(StringComparer.Ordinal);
		for (int lineIndex = 1; lineIndex < allLines.Length; lineIndex++)
		{
			string line = allLines[lineIndex];
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] fields = LoadCsv.ParseCSVFields(line);
			string context = $"[{TableName}] 第 {lineIndex + 1} 行";
			if (fields.Length < RequiredColumns.Length)
			{
				throw new FormatException($"{context}：列数不足（实际 {fields.Length} 列）：{line}");
			}

			WeaponDefinition definition = new WeaponDefinition
			{
				WeaponId = ItemCsvSchema.ParseId(fields[0], context),
				DefinitionId = ItemCsvSchema.ParseName(fields[1], context),
				HandsRequired = ParseHandsRequired(fields[2], context),
				AttackRange = ParseOptionalNonNegativeInt(fields, 4, 1, context),
				DefenseValue = ParseOptionalNonNegativeInt(fields, 5, 0, context),
				DamageBonus = ParseOptionalNonNegativeInt(fields, 6, 0, context),
				MoveBonus = ParseOptionalNonNegativeInt(fields, 7, 0, context),
			};
			definition.Type = ParseEquipmentType(fields, context);
			definition.Load = ParseOptionalLoad(fields, loadColumn, context);
			definition.Rarity = ParseOptionalRarity(fields, rarityColumn, context);

			ItemCsvSchema.EnsureUnique(seenIds, definition.WeaponId, context);
			if (!seenNames.TryAdd(definition.DefinitionId, context))
			{
				throw new FormatException($"{context}：装备名重复：{definition.DefinitionId}");
			}

			result[definition.WeaponId] = definition;
		}

		return result;
	}

	/// <summary>表头里的某一列下标（大小写不敏感）；没有该列时返回 -1。</summary>
	private static int ResolveColumnIndex(string[] header, string columnName)
	{
		for (int i = 0; i < header.Length; i++)
		{
			if ((header[i] ?? string.Empty).Trim().Equals(columnName, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}

		return -1;
	}

	/// <summary>稀有度（可选）：空 / 无该列 = 普通；填了就必须是「普通 / 罕见 / 稀有」。列序由 <see cref="ResolveColumnIndex"/> 给出。</summary>
	private static ItemRarity ParseOptionalRarity(string[] fields, int rarityColumn, string context)
	{
		string raw = rarityColumn >= 0 && fields.Length > rarityColumn ? (fields[rarityColumn] ?? string.Empty).Trim() : string.Empty;
		return raw.Length == 0 ? ItemRarity.Common : ItemCsvSchema.ParseRarity(raw, context);
	}

	/// <summary>手数：只能是 1（单手）或 2（双手）。</summary>
	private static int ParseHandsRequired(string raw, string context)
	{
		if (!int.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hands) || hands is < 1 or > 2)
		{
			throw new FormatException($"{context}：HandsRequired 只能是 1 或 2，实际 `{raw}`");
		}

		return hands;
	}

	/// <summary>装备类型：留空按 `AttackMode` 推导（与战斗侧同规则）；填了就必须是 Melee / Ranged / Armor。</summary>
	private static EquipmentType ParseEquipmentType(string[] fields, string context)
	{
		string raw = fields.Length > EquipmentTypeColumn ? (fields[EquipmentTypeColumn] ?? string.Empty).Trim() : string.Empty;
		if (raw.Length == 0)
		{
			return EquipmentType.Melee;
		}

		if (Enum.TryParse(raw, true, out EquipmentType parsed) && Enum.IsDefined(parsed))
		{
			return parsed;
		}

		throw new FormatException($"{context}：EquipmentType 必须是 Melee / Ranged / Armor，实际 `{raw}`");
	}

	/// <summary>`Load` 列（可选，按表头定位）：留空 / 无该列 = -1（由 ItemNameResolver 按手数推导）。</summary>
	private static float ParseOptionalLoad(string[] fields, int loadColumn, string context)
	{
		string raw = loadColumn >= 0 && fields.Length > loadColumn ? (fields[loadColumn] ?? string.Empty).Trim() : string.Empty;
		return raw.Length == 0 ? -1f : ItemCsvSchema.ParseNonNegativeFloat(raw, context);
	}

	private static int ParseOptionalNonNegativeInt(string[] fields, int index, int fallback, string context)
	{
		string raw = fields.Length > index ? (fields[index] ?? string.Empty).Trim() : string.Empty;
		if (raw.Length == 0)
		{
			return fallback;
		}

		if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
		{
			throw new FormatException($"{context}：第 {index + 1} 列必须是非负整数，实际 `{raw}`");
		}

		return value;
	}
}
