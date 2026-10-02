// ItemConfigData.cs
// 材料 / 道具 / 食物 / 配方 的数据模型（纯逻辑，不依赖 Godot，便于 xUnit 单测）。
// 表头与列规则见 README/功能说明文档/数据系统/数据配置/配表规范.md「物品数据」节；
// 玩法口径见 README/玩法说明文档/系统规则/物品系统/装备、材料、道具系统.md 与 食物系统.md。
//
// 2026-10-02 用户口径（两条，均已落进本模型）：
//   ① 材料暂不参与烹饪：配方输入带 Kind（Food / Material），当前只放行 Food；
//      「材料 → 食物」的 7 条旧配方保留在表里但 Enabled = FALSE（回改只翻这一列）。
//   ② 食物效果的持续时间因食物而异：每个效果各自带寿命轴（FoodEffectDurationKind），
//      数量参数 `DurationValue` 不填默认为 1（= 下一场战斗）。
using System.Collections.Generic;
using CardSimulator;

/// <summary>物品稀有度（普通 / 罕见 / 稀有）。文字解析见 <see cref="ItemCsvSchema"/>。</summary>
public enum ItemRarity
{
	Common = 0,
	Uncommon = 1,
	Rare = 2,
}

/// <summary>材料类别（装备、材料、道具系统 §1.2）。</summary>
public enum MaterialCategory
{
	Craft = 0,  // 合成材料
	Forge = 1,  // 打造材料
}

/// <summary>道具的生效范围（道具.csv 的「生效范围」列）。</summary>
public enum ItemUseScope
{
	Immediate = 0,   // 使用后立即
	InBattle = 1,    // 战斗内
	Persistent = 2,  // 持续
}

/// <summary>DataBase/Item/Material.csv 的一行。</summary>
public sealed class MaterialDefinition
{
	public int MaterialId;
	public string DefinitionId = string.Empty;
	public MaterialCategory Category = MaterialCategory.Craft;

	/// <summary>细分：植物 / 动物整体 / 身体部位 / 矿物（仅展示与筛选用）。</summary>
	public string SubType = string.Empty;

	public ItemRarity Rarity = ItemRarity.Common;

	/// <summary>单件负荷（背包系统交互案 §四；未接入负荷前只读展示）。</summary>
	public float Load;

	public string Description = string.Empty;
}

/// <summary>DataBase/Item/Item.csv 的一行。效果文本见 <see cref="ItemEffectSpecParser"/>。</summary>
public sealed class ItemDefinition
{
	public int ItemId;
	public string DefinitionId = string.Empty;
	public ItemRarity Rarity = ItemRarity.Common;
	public ItemUseScope UseScope = ItemUseScope.Immediate;

	public List<ItemEffectSpec> Effects { get; } = new List<ItemEffectSpec>();

	/// <summary>
	/// `Pending:&lt;说明&gt;` 条目（效果词汇未落时的**显式占位**）。不静默丢弃：
	/// 加载时统一打印一次，批 D 扩 `StateType` / `TurnStartResourceType` 后把这些格子换成真实效果。
	/// </summary>
	public List<string> PendingEffectNotes { get; } = new List<string>();

	public float Load;
	public string Description = string.Empty;
}

/// <summary>DataBase/Item/Food.csv 的一行（食物系统 §三）。</summary>
public sealed class FoodDefinition
{
	public int FoodId;
	public string DefinitionId = string.Empty;
	public ItemRarity Rarity = ItemRarity.Common;

	/// <summary>饱食度（篝火效果上限 10 计入的部分，见 篝火休息与食物 §三）。</summary>
	public int Satiety;

	/// <summary>以「天」为单位的有效期：每天休息结算后 −1，到 0 腐坏移除（食物系统 §二）。</summary>
	public int ExpireDays = 1;

	public List<ItemEffectSpec> Effects { get; } = new List<ItemEffectSpec>();
	public float Load;
	public string Description = string.Empty;
}

/// <summary>配方输入的来源类别。2026-10-02 起只放行 <see cref="Food"/>。</summary>
public enum RecipeInputKind
{
	Food = 0,
	Material = 1,
}

public sealed class RecipeInputSpec
{
	public RecipeInputKind Kind = RecipeInputKind.Food;
	public int Id;
	public int Count = 1;
}

/// <summary>DataBase/Item/FoodRecipe.csv 的一行。</summary>
public sealed class FoodRecipeDefinition
{
	public int RecipeId;
	public int ResultFoodId;
	public int ResultCount = 1;
	public List<RecipeInputSpec> Inputs { get; } = new List<RecipeInputSpec>();

	/// <summary>是否放行。材料通道的 7 条旧配方当前为 false（食物系统 §四 2026-10-02 修订注）。</summary>
	public bool Enabled = true;

	public string Description = string.Empty;
}
