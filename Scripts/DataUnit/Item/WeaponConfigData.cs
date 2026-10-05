// WeaponConfigData.cs
// 装备表（DataBase/Equipment/Weapon.csv）在**局外**侧（背包 / 装备界面）的数据模型：
// 只承载界面与换取规则用得到的字段（ID / 名字 / 手数 / 关键数值 / 类型 / 负荷）。
// 同一张表的**战斗侧**视图仍在 `CardSimulator.Battlefield.BattleWeaponCatalog`（攻击方式与射程解析）——
// 两处各读自己需要的列，表只有一张，不复制数据。
// 纯逻辑、不依赖 Godot，便于 xUnit 单测。
using CardSimulator.Battlefield;

/// <summary>DataBase/Equipment/Weapon.csv 的一行（局外侧视图）。</summary>
public sealed class WeaponDefinition
{
	public int WeaponId;

	public string DefinitionId = string.Empty;

	/// <summary>占用手位数（1 = 单手 / 2 = 双手）；双手装备要求左右手同时让位（六边形战场玩法 §5.2）。</summary>
	public int HandsRequired = 1;

	/// <summary>攻击距离（展示用；战斗侧口径见 `BattleWeaponCatalog`）。</summary>
	public int AttackRange = 1;

	public int DefenseValue;
	public int DamageBonus;
	public int MoveBonus;

	/// <summary>装备类型：近战 / 远程 / 防具（手位防具 = 类型为 `Armor` 的装备）。</summary>
	public EquipmentType Type = EquipmentType.Melee;

	/// <summary>
	/// 单件负荷（背包系统交互案 §四）：`Load` 列留空时为 **-1**，由 `ItemNameResolver` 按手数推导
	/// （单手 2.0 / 双手 4.0）；显式填了正数 / 0 就按表里的值。
	/// </summary>
	public float Load = -1f;

	/// <summary>
	/// 稀有度（`Rarity` 列，2026-10-05 阻断项清理）：商人装备货架分档与锻铁铺费用档共用；
	/// 列缺省 / 留空 = <see cref="ItemRarity.Common"/>（= 商人 40 金币档，见商人交互案 §十 第 4 条默认值）。
	/// </summary>
	public ItemRarity Rarity = ItemRarity.Common;
}
