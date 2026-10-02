// ItemNameResolver.cs
// 物品显示名的**纯逻辑注册表**（无 Godot 依赖，可单测）。
// 为什么单独一层：结算 / 事件文案所在的 `BattleRewardPresenter` 是纯函数模块（单测直接调），
// 不能因为取名字而依赖 Godot 文件 IO；于是由 `LoadingSystem` 在各表加载后把「ID → 显示名」注册进来，
// 文案只读这里。单测也可以自己注册假表来断言文案。
using System;
using System.Collections.Generic;

public static class ItemNameResolver
{
	// ── 名字 ──
	private static readonly Dictionary<int, string> MaterialNames = new Dictionary<int, string>();
	private static readonly Dictionary<int, string> ItemNames = new Dictionary<int, string>();
	private static readonly Dictionary<int, string> FoodNames = new Dictionary<int, string>();
	private static readonly Dictionary<int, string> EquipmentNames = new Dictionary<int, string>();

	// ── 静态数值（背包负荷 / 稀有度 / 食物有效期）：同样由 LoadingSystem 在配表加载时注册 ──
	private static readonly Dictionary<int, float> MaterialLoads = new Dictionary<int, float>();
	private static readonly Dictionary<int, float> ItemLoads = new Dictionary<int, float>();
	private static readonly Dictionary<int, float> FoodLoads = new Dictionary<int, float>();
	private static readonly Dictionary<int, int> MaterialRarities = new Dictionary<int, int>();
	private static readonly Dictionary<int, int> ItemRarities = new Dictionary<int, int>();
	private static readonly Dictionary<int, int> FoodRarities = new Dictionary<int, int>();
	private static readonly Dictionary<int, int> FoodExpireDays = new Dictionary<int, int>();
	private static readonly Dictionary<int, int> FoodSatiety = new Dictionary<int, int>();
	private static readonly Dictionary<int, List<ItemEffectSpec>> FoodEffects = new Dictionary<int, List<ItemEffectSpec>>();

	/// <summary>食物饱食度（篝火结算用）；未注册为 0。</summary>
	public static int FoodSatietyOf(int foodKey) => FoodSatiety.TryGetValue(foodKey, out int satiety) ? satiety : 0;

	/// <summary>食物附带效果（各自带寿命轴）；未注册返回空表。</summary>
	public static IReadOnlyList<ItemEffectSpec> FoodEffectsOf(int foodKey) =>
		FoodEffects.TryGetValue(foodKey, out List<ItemEffectSpec> effects)
			? effects
			: (IReadOnlyList<ItemEffectSpec>)System.Array.Empty<ItemEffectSpec>();

	/// <summary>单件负荷（背包系统交互案 §四）：未注册（含装备表尚未提供 `Load` 列）时为 0。</summary>
	public static float LoadOf(BagCategory category, int definitionKey) => category switch
	{
		BagCategory.Material => MaterialLoads.TryGetValue(definitionKey, out float materialLoad) ? materialLoad : 0f,
		BagCategory.Item => ItemLoads.TryGetValue(definitionKey, out float itemLoad) ? itemLoad : 0f,
		BagCategory.Food => FoodLoads.TryGetValue(definitionKey, out float foodLoad) ? foodLoad : 0f,
		_ => 0f,
	};

	/// <summary>稀有度（`ItemRarity`）；未注册为 0（普通）。</summary>
	public static int RarityOf(BagCategory category, int definitionKey) => category switch
	{
		BagCategory.Material => MaterialRarities.TryGetValue(definitionKey, out int material) ? material : 0,
		BagCategory.Item => ItemRarities.TryGetValue(definitionKey, out int item) ? item : 0,
		BagCategory.Food => FoodRarities.TryGetValue(definitionKey, out int food) ? food : 0,
		_ => 0,
	};

	/// <summary>食物有效期（天，食物系统 §二）；未注册时按 1 天兜底（宁可短不可长）。</summary>
	public static int FoodExpireDaysOf(int foodKey) => FoodExpireDays.TryGetValue(foodKey, out int days) ? days : 1;

	/// <summary>物品显示名（按类别）。</summary>
	public static string NameOf(BagCategory category, int definitionKey) => category switch
	{
		BagCategory.Material => Material(definitionKey),
		BagCategory.Item => Item(definitionKey),
		BagCategory.Equipment => Equipment(definitionKey),
		BagCategory.Food => Food(definitionKey),
		_ => string.Empty,
	};

	public static void RegisterMaterials(IReadOnlyDictionary<int, MaterialDefinition> definitions)
	{
		if (definitions == null)
		{
			return;
		}

		foreach ((int id, MaterialDefinition definition) in definitions)
		{
			if (definition != null && !string.IsNullOrWhiteSpace(definition.DefinitionId))
			{
				MaterialNames[id] = definition.DefinitionId;
				MaterialLoads[id] = definition.Load;
				MaterialRarities[id] = (int)definition.Rarity;
			}
		}
	}

	public static void RegisterItems(IReadOnlyDictionary<int, ItemDefinition> definitions)
	{
		if (definitions == null)
		{
			return;
		}

		foreach ((int id, ItemDefinition definition) in definitions)
		{
			if (definition != null && !string.IsNullOrWhiteSpace(definition.DefinitionId))
			{
				ItemNames[id] = definition.DefinitionId;
				ItemLoads[id] = definition.Load;
				ItemRarities[id] = (int)definition.Rarity;
			}
		}
	}

	public static void RegisterFoods(IReadOnlyDictionary<int, FoodDefinition> definitions)
	{
		if (definitions == null)
		{
			return;
		}

		foreach ((int id, FoodDefinition definition) in definitions)
		{
			if (definition != null && !string.IsNullOrWhiteSpace(definition.DefinitionId))
			{
				FoodNames[id] = definition.DefinitionId;
				FoodLoads[id] = definition.Load;
				FoodRarities[id] = (int)definition.Rarity;
				FoodExpireDays[id] = Math.Max(1, definition.ExpireDays);
				FoodSatiety[id] = Math.Max(0, definition.Satiety);
				FoodEffects[id] = new List<ItemEffectSpec>(definition.Effects ?? new List<ItemEffectSpec>());
			}
		}
	}

	/// <summary>装备名（按武器 / 防具表的数字 ID）：等 P0-18 装备表落地后再注册，现在只留通道。</summary>
	public static void RegisterEquipmentNames(IReadOnlyDictionary<int, string> names)
	{
		if (names == null)
		{
			return;
		}

		foreach ((int id, string name) in names)
		{
			if (!string.IsNullOrWhiteSpace(name))
			{
				EquipmentNames[id] = name;
			}
		}
	}

	public static bool TryGetMaterialName(int id, out string name) => MaterialNames.TryGetValue(id, out name);

	public static string Material(int id) => MaterialNames.TryGetValue(id, out string name) ? name : string.Empty;

	public static string Item(int id) => ItemNames.TryGetValue(id, out string name) ? name : string.Empty;

	public static string Food(int id) => FoodNames.TryGetValue(id, out string name) ? name : string.Empty;

	public static string Equipment(int id) => EquipmentNames.TryGetValue(id, out string name) ? name : string.Empty;

	/// <summary>文案用材料名：未注册时返回 `未定义材料(101)`（P1-4：不静默显示裸编号）。</summary>
	public static string DisplayMaterial(int id) => Resolve(id, "材料", MaterialNames);

	/// <summary>文案用道具名：未注册时返回 `未定义道具(301)`。</summary>
	public static string DisplayItem(int id) => Resolve(id, "道具", ItemNames);

	/// <summary>文案用食物名：未注册时返回 `未定义食物(401)`。</summary>
	public static string DisplayFood(int id) => Resolve(id, "食物", FoodNames);

	/// <summary>文案用装备名：未注册时返回 `未定义装备(10001)`（P0-18 落地后自动变名字）。</summary>
	public static string DisplayEquipment(int id) => Resolve(id, "装备", EquipmentNames);

	private static string Resolve(int id, string category, Dictionary<int, string> names) =>
		names.TryGetValue(id, out string name) ? name : $"未定义{category}({id})";

	/// <summary>清空注册表：读档重进 / 重新加载配表时避免旧名字残留。</summary>
	public static void Clear()
	{
		MaterialNames.Clear();
		ItemNames.Clear();
		FoodNames.Clear();
		EquipmentNames.Clear();
		MaterialLoads.Clear();
		ItemLoads.Clear();
		FoodLoads.Clear();
		MaterialRarities.Clear();
		ItemRarities.Clear();
		FoodRarities.Clear();
		FoodExpireDays.Clear();
		FoodSatiety.Clear();
		FoodEffects.Clear();
	}
}
