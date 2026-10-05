// ArmorConfigData.cs
// 部位装备表（DataBase/Equipment/Armor.csv）的数据模型：头部 / 身体 / 脚部 / 饰品（装备系统交互案 §六）。
// 与 `Weapon.csv` 的分工（案 §六）：**手位**装备（武器与手位防具）继续住 Weapon.csv；
// 这里只住「戴在部位上、不占手位」的装备，两表共用同一个「装备」类目（背包页签 / 负荷 / 名字反查）。
// 纯逻辑、无 Godot 依赖，便于 xUnit 单测。
using System;

/// <summary>
/// 部位装备的**部位**（= 装备界面 6 格的部位编号）。
/// 界面文案（用户口述）= 头部 / 身体 / 脚部 / 饰品，玩法文档的称呼 = 头盔 / 护甲 / 鞋 / 饰品 ——
/// 同一部位的两种说法，实现只有这一份枚举（案 §二 末条：不做两套数据、不做两份配置）。
/// </summary>
public enum EquipmentSlotKind
{
	/// <summary>头部（玩法文档：头盔）；1 格。</summary>
	Head = 0,

	/// <summary>身体（护甲）；1 格。</summary>
	Body = 1,

	/// <summary>脚部（鞋）；1 格。</summary>
	Feet = 2,

	/// <summary>饰品：同部位**多槽**，槽数按配置（当前 3，见 EquipmentConfig.csv）。</summary>
	Accessory = 3,
}

/// <summary>DataBase/Equipment/Armor.csv 的一行。</summary>
public sealed class ArmorDefinition
{
	public int ArmorId;

	public string DefinitionId = string.Empty;

	/// <summary>部位（`Head` / `Body` / `Feet` / `Accessory`）。</summary>
	public EquipmentSlotKind Slot = EquipmentSlotKind.Head;

	/// <summary>
	/// 占用手位数：部位装备**不占手位**，本表恒为 0（手位防具在 Weapon.csv 里，`EquipmentType = Armor`）。
	/// 列保留为口径一致与后续扩展（表里填 1 / 2 只影响展示，不影响任何落点判定）。
	/// </summary>
	public int HandsRequired;

	public int DefenseValue;
	public int DamageBonus;
	public int MoveBonus;
	public int ResourceCost;

	/// <summary>
	/// 单件负荷（背包系统交互案 §四）：本表**必填**（部位装备没有「按手数推导」的兜底）；
	/// 负数 = 该行没走加载器（测试夹具 / 旧档），读数时按部位兜底。
	/// </summary>
	public float Load = -1f;

	/// <summary>
	/// 稀有度（`Rarity` 列，2026-10-05 阻断项清理）：商人装备货架分档与锻铁铺费用档共用；
	/// 列留空 = <see cref="ItemRarity.Common"/>（= 商人 40 金币档）。
	/// </summary>
	public ItemRarity Rarity = ItemRarity.Common;

	/// <summary>是否为「同部位多槽」的部位（只有饰品）。</summary>
	public bool IsMultiSlot => Slot == EquipmentSlotKind.Accessory;
}
