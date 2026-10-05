// LoadEquipmentRecipeCsv.cs
// 读 DataBase/Equipment/EquipmentRecipe.csv（FilePathRegistry: Data.Equipment.Recipe）→ 配方字典。
// 解析规则全在纯逻辑 EquipmentRecipeCatalog（可单测）；本类只负责「取路径 + 读行 + 建字典 + 去重」。
// 用途：村庄锻铁铺与商人锻造炉（复用同一份配方的查询，见锻铁铺交互案 §2.1 的 SmithyContext 注入）。
using System;
using System.Collections.Generic;
using Godot;

public static class LoadEquipmentRecipeCsv
{
	public const string PathKey = "Data.Equipment.Recipe";
	private const string TableName = "EquipmentRecipe";

	private static Dictionary<int, EquipmentRecipe> cache;

	public static Dictionary<int, EquipmentRecipe> LoadFromCSV(string filePath)
	{
		Dictionary<int, EquipmentRecipe> result = new Dictionary<int, EquipmentRecipe>();
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return result;
		}

		List<EquipmentRecipe> recipes = EquipmentRecipeCatalog.ParseLines(LoadCsv.LoadCSVLines(filePath));
		foreach (EquipmentRecipe recipe in recipes)
		{
			if (!result.TryAdd(recipe.RecipeId, recipe))
			{
				throw new FormatException($"[{TableName}] RecipeId 重复：{recipe.RecipeId}");
			}
		}

		GD.Print($"Successfully loaded {result.Count} equipment recipes from {filePath}");
		return result;
	}

	/// <summary>按注册键加载并缓存（默认键 `Data.Equipment.Recipe`）。</summary>
	public static Dictionary<int, EquipmentRecipe> LoadByKey(string pathKey = PathKey, bool useCache = true)
	{
		if (useCache && cache != null)
		{
			return cache;
		}

		string path = LoadingSystem.GetFilePathByKey(pathKey);
		cache = string.IsNullOrWhiteSpace(path) ? new Dictionary<int, EquipmentRecipe>() : LoadFromCSV(path);
		return cache;
	}

	/// <summary>清缓存（测试 / 热重载用）。</summary>
	public static void ResetCache() => cache = null;
}
