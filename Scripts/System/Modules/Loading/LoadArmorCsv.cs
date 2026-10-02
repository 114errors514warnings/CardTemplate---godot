// LoadArmorCsv.cs
// 解析 DataBase/Equipment/Armor.csv（装备系统交互案 §六）：部位装备（头部 / 身体 / 脚部 / 饰品）。
// 表头（固定 9 列，顺序不可换）：ArmorId,DefinitionId,Slot,HandsRequired,DefenseValue,DamageBonus,MoveBonus,ResourceCost,Load
// 口径与物品四表一致：表头 / ID / 名字 / 枚举 / 数值有误一律抛 FormatException，**不静默降级**。
// 两处本表独有的硬口径：
//   1. `Slot` 必须是 Head / Body / Feet / Accessory（未知部位报错，不静默丢进「头部」）；
//   2. `Load` **必填** —— 部位装备没有「按手数推导」的兜底（那是 Weapon.csv 的口径），留空即报错。
using System;
using System.Collections.Generic;

/// <summary>部位装备表加载器（FilePathRegistry: Data.Equipment.Armor）。</summary>
public static class LoadArmorCsv
{
	private const string TableName = "Armor";

	/// <summary>固定表头（9 列，顺序即列序）。</summary>
	private static readonly string[] Columns =
	{
		"ArmorId", "DefinitionId", "Slot", "HandsRequired", "DefenseValue", "DamageBonus", "MoveBonus", "ResourceCost", "Load",
	};

	public static Dictionary<int, ArmorDefinition> LoadFromCSV(string filePath)
	{
		Dictionary<int, ArmorDefinition> result = new Dictionary<int, ArmorDefinition>();
		string[] dataLines = ItemCsvSchema.LoadValidatedDataLines(filePath, TableName, Columns);
		Dictionary<int, string> seenIds = new Dictionary<int, string>();
		Dictionary<string, string> seenNames = new Dictionary<string, string>(StringComparer.Ordinal);

		ItemCsvSchema.ParseRowsOrThrow(TableName, dataLines, (_, context, fields) =>
		{
			ArmorDefinition definition = new ArmorDefinition
			{
				ArmorId = ItemCsvSchema.ParseId(ItemCsvSchema.Field(fields, 0), context),
				DefinitionId = ItemCsvSchema.ParseName(ItemCsvSchema.Field(fields, 1), context),
				Slot = ParseSlot(ItemCsvSchema.Field(fields, 2), context),
				HandsRequired = ParseHandsRequired(ItemCsvSchema.Field(fields, 3), context),
				DefenseValue = ItemCsvSchema.ParseNonNegativeInt(ItemCsvSchema.Field(fields, 4), context),
				DamageBonus = ItemCsvSchema.ParseNonNegativeInt(ItemCsvSchema.Field(fields, 5), context),
				MoveBonus = ItemCsvSchema.ParseNonNegativeInt(ItemCsvSchema.Field(fields, 6), context),
				ResourceCost = ItemCsvSchema.ParseNonNegativeInt(ItemCsvSchema.Field(fields, 7), context),
				Load = ParseRequiredLoad(ItemCsvSchema.Field(fields, 8), context),
			};

			ItemCsvSchema.EnsureUnique(seenIds, definition.ArmorId, context);
			if (!seenNames.TryAdd(definition.DefinitionId, context))
			{
				throw new FormatException($"{context}：装备名重复：{definition.DefinitionId}");
			}

			result[definition.ArmorId] = definition;
		});

		return result;
	}

	/// <summary>部位：只能是 Head / Body / Feet / Accessory（大小写不敏感）。</summary>
	private static EquipmentSlotKind ParseSlot(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (Enum.TryParse(text, true, out EquipmentSlotKind slot) && Enum.IsDefined(slot))
		{
			return slot;
		}

		throw new FormatException($"{context}：Slot 必须是 Head / Body / Feet / Accessory，实际 `{text}`");
	}

	/// <summary>占用手数：留空 = 0（部位装备不占手位）；填了就只能是 0 / 1 / 2。</summary>
	private static int ParseHandsRequired(string raw, string context)
	{
		int hands = ItemCsvSchema.ParseNonNegativeInt(raw, context);
		if (hands > 2)
		{
			throw new FormatException($"{context}：HandsRequired 只能是 0 / 1 / 2，实际 `{raw}`");
		}

		return hands;
	}

	/// <summary>`Load` 必填（部位装备没有按手数推导的兜底）：留空 / 非数字 / 负数一律报错。</summary>
	private static float ParseRequiredLoad(string raw, string context)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			throw new FormatException($"{context}：Armor 表的 Load 必填（部位装备没有按手数推导的兜底）");
		}

		float load = ItemCsvSchema.ParseNonNegativeFloat(raw, context);
		if (load <= 0f)
		{
			throw new FormatException($"{context}：Load 必须大于 0，实际 `{raw}`（0 负荷的部位装备请显式确认后再放宽本口径）");
		}

		return load;
	}

	/// <summary>装备名跨表去重（Weapon.csv 与 Armor.csv 共用一个「装备」命名空间，重名会让名字反查失真）。</summary>
	public static void ValidateDistinctNames(
		IReadOnlyDictionary<int, ArmorDefinition> armors,
		IReadOnlyDictionary<int, string> weaponNames)
	{
		if (armors == null || weaponNames == null)
		{
			return;
		}

		List<string> duplicates = new List<string>();
		foreach (ArmorDefinition armor in armors.Values)
		{
			if (armor == null || string.IsNullOrWhiteSpace(armor.DefinitionId))
			{
				continue;
			}

			if (weaponNames.TryGetValue(armor.ArmorId, out string sameId))
			{
				duplicates.Add($"{armor.DefinitionId}（ArmorId {armor.ArmorId} 与 Weapon.csv 的同 ID 装备名 `{sameId}` 冲突）");
				continue;
			}

			foreach (KeyValuePair<int, string> weapon in weaponNames)
			{
				if (string.Equals(weapon.Value, armor.DefinitionId, StringComparison.Ordinal))
				{
					duplicates.Add($"{armor.DefinitionId}（ArmorId {armor.ArmorId} 与 WeaponId {weapon.Key} 重名）");
					break;
				}
			}
		}

		if (duplicates.Count > 0)
		{
			throw new FormatException("[Armor] 装备名与 Weapon.csv 冲突（两表共用一个装备命名空间）：" + string.Join("；", duplicates));
		}
	}

	/// <summary>测试与文案用：枚举的规范写法（`Head` / `Body` / `Feet` / `Accessory`）。</summary>
	public static string SlotNameOf(EquipmentSlotKind slot) => slot.ToString();
}
