// LoadMaterialCsv.cs
// 解析 DataBase/Item/Material.csv。
// 列：MaterialId,DefinitionId,Category,SubType,Rarity,Load,Description
// Category：合成材料 / 打造材料；Rarity：普通 / 罕见 / 稀有。
// 未定义枚举、空名称、重复 ID / 名称一律报错（P1-4 验收：不静默降级）。
using System;
using System.Collections.Generic;
using Godot;

/// <summary>材料表（装备、材料、道具系统 §一）：ID 101 起为合成材料，201 起为打造材料。</summary>
public static class LoadMaterialCsv
{
	private static readonly string[] Columns =
	{
		"MaterialId", "DefinitionId", "Category", "SubType", "Rarity", "Load", "Description",
	};

	public static Dictionary<int, MaterialDefinition> LoadFromCSV(string filePath)
	{
		Dictionary<int, MaterialDefinition> result = new Dictionary<int, MaterialDefinition>();
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return result;
		}

		string[] lines = ItemCsvSchema.LoadValidatedDataLines(filePath, "Material", Columns);
		Dictionary<int, string> seenIds = new Dictionary<int, string>();
		Dictionary<string, string> seenNames = new Dictionary<string, string>(StringComparer.Ordinal);
		ItemCsvSchema.ParseRowsOrThrow("Material", lines, (_, context, fields) =>
		{
			MaterialDefinition definition = new MaterialDefinition
			{
				MaterialId = ItemCsvSchema.ParseId(fields[0], context),
				DefinitionId = ItemCsvSchema.ParseName(fields[1], context),
				Category = ItemCsvSchema.ParseMaterialCategory(fields[2], context),
				SubType = ItemCsvSchema.Field(fields, 3),
				Rarity = ItemCsvSchema.ParseRarity(fields[4], context),
				Load = ItemCsvSchema.ParseNonNegativeFloat(fields[5], context),
				Description = ItemCsvSchema.Field(fields, 6),
			};

			ItemCsvSchema.EnsureUnique(seenIds, definition.MaterialId, context);
			if (!seenNames.TryAdd(definition.DefinitionId, context))
			{
				throw new FormatException($"{context}：材料名重复：{definition.DefinitionId}");
			}

			result[definition.MaterialId] = definition;
		});

		return result;
	}
}
