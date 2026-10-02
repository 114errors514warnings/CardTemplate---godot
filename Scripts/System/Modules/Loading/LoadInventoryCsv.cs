// LoadInventoryCsv.cs
// 解析 DataBase/Inventory/InventoryConfig.csv（背包系统交互案 §四）。
// 列：Scope,CharacterId,Capacity —— `Global` 行 = 队伍共用负荷上限；`Character` 行 = 按角色的额外额度（预留）。
// 口径与物品四表一致：表头列名 / 列数不符、`Scope` 不认识、ID 非正整数、上限非正数一律抛 FormatException（不静默降级）。
using System;
using System.Collections.Generic;

/// <summary>背包配置表加载器（FilePathRegistry: Data.Inventory.Config）。</summary>
public static class LoadInventoryCsv
{
	private const string TableName = "InventoryConfig";

	private static readonly string[] Columns = { "Scope", "CharacterId", "Capacity" };

	public static InventoryConfigDefinition LoadFromCSV(string filePath)
	{
		InventoryConfigDefinition config = new InventoryConfigDefinition();
		string[] lines = ItemCsvSchema.LoadValidatedDataLines(filePath, TableName, Columns);
		bool hasGlobal = false;
		ItemCsvSchema.ParseRowsOrThrow(TableName, lines, (_, context, fields) =>
		{
			string scope = (fields[0] ?? string.Empty).Trim();
			string rawCharacterId = (fields[1] ?? string.Empty).Trim();
			float capacity = ItemCsvSchema.ParseNonNegativeFloat(fields[2], context);

			if (scope.Equals("Global", StringComparison.OrdinalIgnoreCase))
			{
				if (!string.IsNullOrWhiteSpace(rawCharacterId))
				{
					throw new FormatException($"{context}：`Global` 行的 CharacterId 必须留空");
				}

				if (capacity <= 0f)
				{
					throw new FormatException($"{context}：队伍负荷上限必须大于 0（实际 {capacity}）");
				}

				if (hasGlobal)
				{
					throw new FormatException($"{context}：`Global` 行只能有一条");
				}

				config.GlobalCapacity = capacity;
				hasGlobal = true;
				return;
			}

			if (scope.Equals("Character", StringComparison.OrdinalIgnoreCase))
			{
				int characterId = ItemCsvSchema.ParseId(rawCharacterId, context);
				if (capacity < 0f)
				{
					throw new FormatException($"{context}：角色的额外额度不得为负（实际 {capacity}）");
				}

				if (!config.CharacterCapacities.TryAdd(characterId, capacity))
				{
					throw new FormatException($"{context}：角色 {characterId} 的额度重复定义");
				}

				return;
			}

			throw new FormatException($"{context}：`Scope` 不认识：`{scope}`（可用：Global / Character）");
		});

		if (!hasGlobal)
		{
			throw new FormatException($"[{TableName}] 缺少 `Global` 行：队伍负荷上限没有来源（背包系统交互案 §四）。");
		}

		return config;
	}
}
