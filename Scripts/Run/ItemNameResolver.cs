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

	// ── 装备（Weapon.csv 局外侧视图，2026-10-02 批 E）：手数 / 单件负荷 / 名字反查 ──
	private static readonly Dictionary<int, float> EquipmentLoads = new Dictionary<int, float>();
	private static readonly Dictionary<int, int> EquipmentHandsRequired = new Dictionary<int, int>();
	private static readonly Dictionary<string, int> EquipmentKeysByName = new Dictionary<string, int>(StringComparer.Ordinal);

	// ── 部位装备（Armor.csv，2026-10-02 装备界面批）：部位 / 关键数值 / 名字 ──
	// 两张装备表（Weapon / Armor）共用上面那三个「装备」字典的命名空间：背包页签、负荷、名字反查都只认一份。
	private static readonly Dictionary<int, ArmorDefinition> ArmorByKey = new Dictionary<int, ArmorDefinition>();
	private static readonly Dictionary<string, ArmorDefinition> ArmorByName = new Dictionary<string, ArmorDefinition>(StringComparer.Ordinal);

	/// <summary>饰品格数（装备界面按它铺格；`EquipmentConfig.csv` 未配时用默认 3，装备系统交互案 §九 第 5 条）。</summary>
	private static int accessorySlotCount = EquipmentConfigDefinition.DefaultAccessorySlotCount;

	/// <summary>饰品格数（读数口：界面与落点判定共用；非正数一律回落到默认 3）。</summary>
	public static int AccessorySlotCount => accessorySlotCount;

	/// <summary>写入饰品格数；非正数一律回落到默认值（不让一张坏表把装备界面锁死）。</summary>
	public static void SetAccessorySlotCount(int count) =>
		accessorySlotCount = count > 0 ? count : EquipmentConfigDefinition.DefaultAccessorySlotCount;


	// ── 队伍负荷上限（DataBase/Inventory/InventoryConfig.csv 的 `Global` 行）──
	private static float inventoryCapacity = DefaultInventoryCapacity;

	/// <summary>未配表（或表里没有 `Global` 行）时的队伍负荷上限兜底：背包系统交互案 §四 的默认值。</summary>
	public const float DefaultInventoryCapacity = 30f;

	/// <summary>队伍负荷上限（背包系统交互案 §四）：`LoadingSystem` 载入 InventoryConfig.csv 后覆盖。</summary>
	public static float InventoryCapacity => inventoryCapacity;

	/// <summary>写入队伍负荷上限；非正数一律回落到默认值（不让一张坏表把背包锁死）。</summary>
	public static void SetInventoryCapacity(float capacity) =>
		inventoryCapacity = capacity > 0f ? capacity : DefaultInventoryCapacity;
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
		BagCategory.Equipment => EquipmentLoads.TryGetValue(definitionKey, out float equipmentLoad) ? equipmentLoad : 0f,
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

	/// <summary>装备名（按武器 / 防具表的数字 ID）。手数 / 负荷按「单手」兜底，真实值走 <see cref="RegisterWeapons"/>。</summary>
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
				EquipmentKeysByName[name] = id;
				EquipmentHandsRequired.TryAdd(id, 1);
				EquipmentLoads.TryAdd(id, DerivedEquipmentLoad(1));
			}
		}
	}

	/// <summary>
	/// 注册装备表（Weapon.csv 局外侧视图）：名字 + 名字反查 + 占用手数 + 单件负荷。
	/// 负荷口径（背包系统交互案 §四）：表里有 `Load` 就用表里的，留空则按手数推导（单手 2.0 / 双手 4.0）。
	/// </summary>
	public static void RegisterWeapons(IReadOnlyDictionary<int, WeaponDefinition> definitions)
	{
		if (definitions == null)
		{
			return;
		}

		foreach ((int id, WeaponDefinition definition) in definitions)
		{
			if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId))
			{
				continue;
			}

			int hands = Math.Max(1, definition.HandsRequired);
			EquipmentNames[id] = definition.DefinitionId;
			EquipmentKeysByName[definition.DefinitionId] = id;
			EquipmentHandsRequired[id] = hands;
			EquipmentLoads[id] = definition.Load >= 0f ? definition.Load : DerivedEquipmentLoad(hands);
		}
	}

	/// <summary>装备单件负荷的推导口径（表里没填 `Load` 时）：单手 2.0 / 双手 4.0。</summary>
	public static float DerivedEquipmentLoad(int handsRequired) => handsRequired >= 2 ? 4f : 2f;

	/// <summary>
	/// 注册部位装备表（Armor.csv）：名字 + 名字反查 + 部位 + 关键数值；负荷写进**同一个**装备负荷表。
	/// 表里 `Load` 必填（加载器已保证），但测试夹具可能只填名字 —— 那时按部位兜底（饰品 0.5 / 其它 2.0）。
	/// </summary>
	public static void RegisterArmors(IReadOnlyDictionary<int, ArmorDefinition> definitions)
	{
		if (definitions == null)
		{
			return;
		}

		foreach ((int id, ArmorDefinition definition) in definitions)
		{
			if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId))
			{
				continue;
			}

			EquipmentNames[id] = definition.DefinitionId;
			EquipmentKeysByName[definition.DefinitionId] = id;
			EquipmentHandsRequired[id] = Math.Max(0, definition.HandsRequired);
			EquipmentLoads[id] = definition.Load >= 0f ? definition.Load : DerivedArmorLoad(definition.Slot);
			ArmorByKey[id] = definition;
			ArmorByName[definition.DefinitionId] = definition;
		}
	}

	/// <summary>部位装备单件负荷的推导口径（**只在**表里没填 `Load` 时用到）：饰品 0.5 / 其它部位 2.0。</summary>
	public static float DerivedArmorLoad(EquipmentSlotKind slot) =>
		slot == EquipmentSlotKind.Accessory ? 0.5f : 2f;

	/// <summary>按**装备名**取部位装备定义（武器 / 手位防具返回 false）。</summary>
	public static bool TryGetArmorDefinition(string definitionId, out ArmorDefinition definition) =>
		ArmorByName.TryGetValue(definitionId ?? string.Empty, out definition) && definition != null;

	/// <summary>按**装备名**取部位：只有 Armor.csv 的部位装备返回 true（武器与手位防具返回 false）。</summary>
	public static bool TryGetBodySlotOfDefinition(string definitionId, out EquipmentSlotKind slot)
	{
		if (TryGetArmorDefinition(definitionId, out ArmorDefinition definition))
		{
			slot = definition.Slot;
			return true;
		}

		slot = EquipmentSlotKind.Head;
		return false;
	}

	/// <summary>是否「部位装备」（Armor.csv 的行）。手位落点用它判「部位不符」。</summary>
	public static bool IsBodySlotEquipment(string definitionId) => TryGetArmorDefinition(definitionId, out _);


	/// <summary>占用手数（1 = 单手 / 2 = 双手）：未注册按 1 兜底（宁可宽放，不误判成双手）。</summary>
	public static int HandsRequiredOf(int equipmentKey) =>
		EquipmentHandsRequired.TryGetValue(equipmentKey, out int hands) ? hands : 1;

	/// <summary>按**装备名**取占用手数（手位字段是名字串，读档校验 / 迁移用）；未注册按 1 兜底。</summary>
	public static int HandsRequiredOfDefinition(string definitionId) =>
		TryGetEquipmentKey(definitionId, out int key) ? HandsRequiredOf(key) : 1;

	/// <summary>装备名 → 表内数字 ID（手位卸下回背包时用它建条目）。未注册返回 false。</summary>
	public static bool TryGetEquipmentKey(string definitionId, out int key) =>
		EquipmentKeysByName.TryGetValue(definitionId ?? string.Empty, out key);

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
		EquipmentLoads.Clear();
		EquipmentHandsRequired.Clear();
		EquipmentKeysByName.Clear();
		ArmorByKey.Clear();
		ArmorByName.Clear();
		accessorySlotCount = EquipmentConfigDefinition.DefaultAccessorySlotCount;
		inventoryCapacity = DefaultInventoryCapacity;
	}
}
