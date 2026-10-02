// LoadEquipmentConfigCsv.cs
// 解析 DataBase/Equipment/EquipmentConfig.csv（装备系统交互案 §九 第 5 条「饰品上限读配置」）。
// 列：Scope,CharacterId,AccessorySlots —— `Global` 行 = 全队饰品格数；`Character` 行 = 按角色的额外格数（预留）。
// 口径与 InventoryConfig.csv 一致：表头 / 列数 / `Scope` / 数值不符一律抛 FormatException（不静默降级）。
using System;
using System.Collections.Generic;

/// <summary>装备配置表加载器（FilePathRegistry: Data.Equipment.Config）。</summary>
public static class LoadEquipmentConfigCsv
{
	private const string TableName = "EquipmentConfig";

	private static readonly string[] Columns = { "Scope", "CharacterId", "AccessorySlots" };

	public static EquipmentConfigDefinition LoadFromCSV(string filePath)
	{
		EquipmentConfigDefinition config = new EquipmentConfigDefinition();
		string[] lines = ItemCsvSchema.LoadValidatedDataLines(filePath, TableName, Columns);
		bool hasGlobal = false;
		ItemCsvSchema.ParseRowsOrThrow(TableName, lines, (_, context, fields) =>
		{
			string scope = (fields[0] ?? string.Empty).Trim();
			string rawCharacterId = (fields[1] ?? string.Empty).Trim();
			int slots = ItemCsvSchema.ParseNonNegativeInt(fields[2], context);

			if (scope.Equals("Global", StringComparison.OrdinalIgnoreCase))
			{
				if (!string.IsNullOrWhiteSpace(rawCharacterId))
				{
					throw new FormatException($"{context}：`Global` 行的 CharacterId 必须留空");
				}

				if (slots <= 0)
				{
					throw new FormatException($"{context}：饰品格数必须大于 0（实际 {slots}）");
				}

				if (hasGlobal)
				{
					throw new FormatException($"{context}：`Global` 行只能有一条");
				}

				config.AccessorySlots = slots;
				hasGlobal = true;
				return;
			}

			if (scope.Equals("Character", StringComparison.OrdinalIgnoreCase))
			{
				int characterId = ItemCsvSchema.ParseId(rawCharacterId, context);
				if (!config.CharacterAccessorySlots.TryAdd(characterId, slots))
				{
					throw new FormatException($"{context}：角色 {characterId} 的格数重复定义");
				}

				return;
			}

			throw new FormatException($"{context}：`Scope` 不认识：`{scope}`（可用：Global / Character）");
		});

		if (!hasGlobal)
		{
			throw new FormatException($"[{TableName}] 缺少 `Global` 行：饰品格数没有来源（装备系统交互案 §九 第 5 条）。");
		}

		return config;
	}
}
