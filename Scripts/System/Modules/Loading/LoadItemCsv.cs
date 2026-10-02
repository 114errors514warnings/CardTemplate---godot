// LoadItemCsv.cs
// 解析 DataBase/Item/Item.csv。
// 列：ItemId,DefinitionId,Rarity,UseScope,Effect1,Effect2,Load,Description
// 效果语法见 ItemEffectSpecParser；`Pending:<说明>` 为**显式占位**（效果词汇未落），加载时打印一次、不静默丢弃。
using System;
using System.Collections.Generic;
using Godot;

/// <summary>道具表（装备、材料、道具系统 §二）：ID 301 起。</summary>
public static class LoadItemCsv
{
	private static readonly string[] Columns =
	{
		"ItemId", "DefinitionId", "Rarity", "UseScope", "Effect1", "Effect2", "Load", "Description",
	};

	private static readonly List<string> PendingNotes = new List<string>();

	/// <summary>本次加载遇到的 `Pending:` 占位说明（供面板 / 烟测读取；批 D 补齐词汇后应为空）。</summary>
	public static IReadOnlyList<string> PendingEffectNotes => PendingNotes;

	public static Dictionary<int, ItemDefinition> LoadFromCSV(string filePath)
	{
		Dictionary<int, ItemDefinition> result = new Dictionary<int, ItemDefinition>();
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return result;
		}

		PendingNotes.Clear();
		string[] lines = ItemCsvSchema.LoadValidatedDataLines(filePath, "Item", Columns);
		Dictionary<int, string> seenIds = new Dictionary<int, string>();
		Dictionary<string, string> seenNames = new Dictionary<string, string>(StringComparer.Ordinal);
		ItemCsvSchema.ParseRowsOrThrow("Item", lines, (_, context, fields) =>
		{
			ItemDefinition definition = new ItemDefinition
			{
				ItemId = ItemCsvSchema.ParseId(fields[0], context),
				DefinitionId = ItemCsvSchema.ParseName(fields[1], context),
				Rarity = ItemCsvSchema.ParseRarity(fields[2], context),
				UseScope = ItemCsvSchema.ParseUseScope(fields[3], context),
				Load = ItemCsvSchema.ParseNonNegativeFloat(fields[6], context),
				Description = ItemCsvSchema.Field(fields, 7),
			};

			foreach (int index in new[] { 4, 5 })
			{
				ItemEffectSpec effect = ItemEffectSpecParser.ParseEffect(ItemCsvSchema.Field(fields, index), context, definition.PendingEffectNotes);
				if (effect != null)
				{
					definition.Effects.Add(effect);
				}
			}

			ItemCsvSchema.EnsureUnique(seenIds, definition.ItemId, context);
			if (!seenNames.TryAdd(definition.DefinitionId, context))
			{
				throw new FormatException($"{context}：道具名重复：{definition.DefinitionId}");
			}

			foreach (string note in definition.PendingEffectNotes)
			{
				PendingNotes.Add($"{definition.DefinitionId}：{note}");
			}

			result[definition.ItemId] = definition;
		});

		if (PendingNotes.Count > 0)
		{
			GD.Print($"[Item] 有 {PendingNotes.Count} 条效果仍是 `Pending:` 占位（待扩 StateType / TurnStartResourceType）：" +
				string.Join("；", PendingNotes));
		}

		return result;
	}
}
