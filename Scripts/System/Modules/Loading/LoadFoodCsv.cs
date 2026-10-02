// LoadFoodCsv.cs
// 解析 DataBase/Item/Food.csv 与 DataBase/Item/FoodRecipe.csv。
//   Food.csv   列：FoodId,DefinitionId,Rarity,Satiety,ExpireDays,Effect1,Effect1Duration,Effect2,Effect2Duration,Load,Description
//   Recipe.csv 列：RecipeId,ResultFoodId,ResultCount,Input1Kind,Input1Id,Input1Count,Input2Kind,Input2Id,Input2Count,Enabled,Description
// 2026-10-02 口径：① 只有食物可作烹饪输入 → Enabled=TRUE 的配方出现 Material 输入即报错；
//                  ② 效果寿命轴独立成列（EffectNDuration），留空 = BattleCount:1（下一场战斗）。
using System;
using System.Collections.Generic;
using Godot;

/// <summary>食物表（食物系统 §三）与篝火合成配方表（§四）。</summary>
public static class LoadFoodCsv
{
	private static readonly string[] FoodColumns =
	{
		"FoodId", "DefinitionId", "Rarity", "Satiety", "ExpireDays",
		"Effect1", "Effect1Duration", "Effect2", "Effect2Duration", "Load", "Description",
	};

	private static readonly string[] RecipeColumns =
	{
		"RecipeId", "ResultFoodId", "ResultCount",
		"Input1Kind", "Input1Id", "Input1Count", "Input2Kind", "Input2Id", "Input2Count",
		"Enabled", "Description",
	};

	public static Dictionary<int, FoodDefinition> LoadFoodsFromCSV(string filePath)
	{
		Dictionary<int, FoodDefinition> result = new Dictionary<int, FoodDefinition>();
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return result;
		}

		string[] lines = ItemCsvSchema.LoadValidatedDataLines(filePath, "Food", FoodColumns);
		Dictionary<int, string> seenIds = new Dictionary<int, string>();
		Dictionary<string, string> seenNames = new Dictionary<string, string>(StringComparer.Ordinal);
		ItemCsvSchema.ParseRowsOrThrow("Food", lines, (_, context, fields) =>
		{
			FoodDefinition definition = new FoodDefinition
			{
				FoodId = ItemCsvSchema.ParseId(fields[0], context),
				DefinitionId = ItemCsvSchema.ParseName(fields[1], context),
				Rarity = ItemCsvSchema.ParseRarity(fields[2], context),
				Satiety = ItemCsvSchema.ParseNonNegativeInt(fields[3], context),
				ExpireDays = ItemCsvSchema.ParsePositiveInt(fields[4], context),
				Load = ItemCsvSchema.ParseNonNegativeFloat(fields[9], context),
				Description = ItemCsvSchema.Field(fields, 10),
			};

			foreach ((int effectIndex, int durationIndex) in new[] { (5, 6), (7, 8) })
			{
				ItemEffectSpec effect = ItemEffectSpecParser.ParseEffect(ItemCsvSchema.Field(fields, effectIndex), context);
				if (effect == null)
				{
					continue;
				}

				ItemEffectSpecParser.ApplyDuration(effect, ItemCsvSchema.Field(fields, durationIndex), context);
				definition.Effects.Add(effect);
			}

			ItemCsvSchema.EnsureUnique(seenIds, definition.FoodId, context);
			if (!seenNames.TryAdd(definition.DefinitionId, context))
			{
				throw new FormatException($"{context}：食物名重复：{definition.DefinitionId}");
			}

			result[definition.FoodId] = definition;
		});

		return result;
	}

	public static Dictionary<int, FoodRecipeDefinition> LoadRecipesFromCSV(string filePath, string tableName = "FoodRecipe")
	{
		Dictionary<int, FoodRecipeDefinition> result = new Dictionary<int, FoodRecipeDefinition>();
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return result;
		}

		string[] lines = ItemCsvSchema.LoadValidatedDataLines(filePath, tableName, RecipeColumns);
		Dictionary<int, string> seenIds = new Dictionary<int, string>();
		ItemCsvSchema.ParseRowsOrThrow(tableName, lines, (_, context, fields) =>
		{
			FoodRecipeDefinition recipe = new FoodRecipeDefinition
			{
				RecipeId = ItemCsvSchema.ParseId(fields[0], context),
				ResultFoodId = ItemCsvSchema.ParseId(fields[1], context),
				ResultCount = ItemCsvSchema.ParsePositiveInt(fields[2], context),
				Enabled = ItemCsvSchema.ParseBool(fields[9], context),
				Description = ItemCsvSchema.Field(fields, 10),
			};

			foreach ((int kindIndex, int idIndex, int countIndex) in new[] { (3, 4, 5), (6, 7, 8) })
			{
				string kindText = ItemCsvSchema.Field(fields, kindIndex);
				string idText = ItemCsvSchema.Field(fields, idIndex);
				string countText = ItemCsvSchema.Field(fields, countIndex);
				if (kindText.Length == 0 && idText.Length == 0)
				{
					continue; // 没有这一条输入（数量列即使写了 0 也按"没有"处理）
				}

				if (kindText.Length == 0 || idText.Length == 0)
				{
					throw new FormatException($"{context}：输入条目必须同时给 Kind 与 Id（`{kindText}` / `{idText}`）");
				}

				recipe.Inputs.Add(new RecipeInputSpec
				{
					Kind = ItemCsvSchema.ParseRecipeInputKind(kindText, context),
					Id = ItemCsvSchema.ParseId(idText, context),
					Count = ItemCsvSchema.ParsePositiveInt(countText, context),
				});
			}

			if (recipe.Inputs.Count == 0)
			{
				throw new FormatException($"{context}：配方至少要有一个输入");
			}

			// 2026-10-02 口径：材料暂不参与烹饪 —— 放行中的配方出现材料输入即报错（回改 = 先改这一条口径）。
			if (recipe.Enabled)
			{
				foreach (RecipeInputSpec input in recipe.Inputs)
				{
					if (input.Kind == RecipeInputKind.Material)
					{
						throw new FormatException($"{context}：材料暂不参与烹饪 —— Enabled=TRUE 的配方不得含 Material 输入" +
							"（2026-10-02 口径，见 食物系统 §四；材料通道的配方保持 Enabled=FALSE）");
					}
				}
			}

			ItemCsvSchema.EnsureUnique(seenIds, recipe.RecipeId, context);
			result[recipe.RecipeId] = recipe;
		});

		return result;
	}

	/// <summary>
	/// 跨表校验：结果食物必须存在；食物类输入必须是已定义食物；材料类输入必须是已定义材料。
	/// **只为 Enabled=TRUE 的配方校验引用**（禁用的材料通道允许悬空），不通过即报错。
	/// </summary>
	public static void ValidateReferences(
		IReadOnlyDictionary<int, FoodRecipeDefinition> recipes,
		IReadOnlyDictionary<int, FoodDefinition> foods,
		IReadOnlyDictionary<int, MaterialDefinition> materials)
	{
		if (recipes == null)
		{
			return;
		}

		foreach (FoodRecipeDefinition recipe in recipes.Values)
		{
			if (!recipe.Enabled)
			{
				continue;
			}

			string context = $"[FoodRecipe] 配方 {recipe.RecipeId}（{recipe.Description}）";
			if (foods == null || !foods.ContainsKey(recipe.ResultFoodId))
			{
				throw new FormatException($"{context}：结果食物未定义：{recipe.ResultFoodId}");
			}

			foreach (RecipeInputSpec input in recipe.Inputs)
			{
				bool found = input.Kind == RecipeInputKind.Food
					? foods.ContainsKey(input.Id)
					: materials != null && materials.ContainsKey(input.Id);
				if (!found)
				{
					throw new FormatException($"{context}：输入 {input.Kind}:{input.Id} 未定义");
				}
			}
		}
	}
}
