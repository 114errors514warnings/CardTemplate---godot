// InventoryConfigData.cs
// 背包配置表（DataBase/Inventory/InventoryConfig.csv）的数据模型：队伍共用负荷上限（背包系统交互案 §四）。
// 表头 `Scope,CharacterId,Capacity`：`Global` 行 = 队伍上限；`Character` 行 = 按角色的额外额度
// （交互案 §九 第 4 条的预留口径，当前表里不启用）。纯逻辑、不依赖 Godot，便于 xUnit 单测。
using System.Collections.Generic;

/// <summary>DataBase/Inventory/InventoryConfig.csv 的整表视图。</summary>
public sealed class InventoryConfigDefinition
{
	/// <summary>队伍共用上限（`Global` 行）。未配表时用 <see cref="ItemNameResolver.DefaultInventoryCapacity"/> 兜底。</summary>
	public float GlobalCapacity = ItemNameResolver.DefaultInventoryCapacity;

	/// <summary>按角色的**额外**额度（`Character` 行；当前预留，不在背包界面上体现）。</summary>
	public Dictionary<int, float> CharacterCapacities { get; } = new Dictionary<int, float>();

	/// <summary>某角色的实际上限 = 队伍上限 + 该角色的额外额度（未配置额外额度时等于队伍上限）。</summary>
	public float CapacityFor(int characterId) =>
		GlobalCapacity + (CharacterCapacities.TryGetValue(characterId, out float extra) ? extra : 0f);
}
