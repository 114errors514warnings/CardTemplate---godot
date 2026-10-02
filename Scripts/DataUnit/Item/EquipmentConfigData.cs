// EquipmentConfigData.cs
// 装备配置表（DataBase/Equipment/EquipmentConfig.csv）的数据模型：装备界面**格数**这类配置项。
// 为什么单开一张表：背包配置（InventoryConfig.csv）管的是负荷上限，装备格数属于装备侧；
// 两者列口径不同、生命周期也不同（改格数只影响界面，不动背包规则）。
// 纯逻辑、无 Godot 依赖。
using System.Collections.Generic;

/// <summary>装备配置表（FilePathRegistry: Data.Equipment.Config）。</summary>
public sealed class EquipmentConfigDefinition
{
	/// <summary>饰品格数的**默认值**（案 §九 第 5 条：当前 3，保留为配置项）。</summary>
	public const int DefaultAccessorySlotCount = 3;

	/// <summary>`Global` 行：全队统一的饰品格数（必须 > 0）。</summary>
	public int AccessorySlots = DefaultAccessorySlotCount;

	/// <summary>`Character` 行：按角色的**额外**饰品格数（预留，当前不启用）。</summary>
	public Dictionary<int, int> CharacterAccessorySlots { get; set; } = new Dictionary<int, int>();
}
