// RunFacilityCosts.cs
// 地点设施「操作代价」的**运行期取值**（纯逻辑，无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/村庄地图交互案.md §四（2026-10-05 第四轮口径）、
//   README/功能说明文档/数据系统/数据配置/配表规范.md §全局变量（第 7 / 8 列）。
// 分工（数值的**唯一来源**是全局数据表，代码只留兜底默认值，与移动代价 `MoveTimePointCost` 同口径）：
//   表 `DataBase/GameVariables.csv` 的 `VillageOperationTimePointCost` / `ForestForageTimePointCost`
//     →（Godot 侧）`GameVariables.ApplyFacilityCosts()`
//     → 本类 → `VillageVisit.OperationTimePointCost` / `VillageForage.TimePointCost` /
//       `SmithyCrafting.CraftTimePointCost` / `RestaurantTrade.CookTimePointCost` / `RestaurantTrade.OrderTimePointCost`
//     → 界面（`SmithyUi` / `RestaurantUi` / `VillageScene`）/ 结算（`RunSession.Place`）/ 单测。
// 本类刻意不碰 `LoadCsv` / `LoadingSystem`（那是 Godot 依赖）：纯 .NET 单测可直接读；
//   表没接上（或两项留空）时一律走兜底默认值，行为与「表里填了当前口径」一致。
public static class RunFacilityCosts
{
	/// <summary>
	/// 村庄设施**每次操作**的时间点代价的兜底默认值（表 `VillageOperationTimePointCost`）：
	/// 2026-10-05 用户口径 = **0.1**（= 时间点最小计量单位，1 个战斗回合）。
	/// </summary>
	public const float DefaultOperationCost = 0.1f;

	/// <summary>
	/// 树林**单次搜寻**的时间点代价的兜底默认值（表 `ForestForageTimePointCost`）：
	/// 与村庄操作**分开配**（用户口径「树林里搜索的耗时与村庄其他操作不同」），当前口径 1.0。
	/// </summary>
	public const float DefaultForestForageCost = 1.0f;

	/// <summary>村庄设施每次操作的时间点代价（搜寻之外的搜寻 / 锻造 / 烹饪 / 点菜等「操作」共用）。</summary>
	public static float OperationCost { get; private set; } = DefaultOperationCost;

	/// <summary>树林单次搜寻的时间点代价（独立项，不与村庄操作共用）。</summary>
	public static float ForestForageCost { get; private set; } = DefaultForestForageCost;

	/// <summary>
	/// 用表值覆盖运行期取值（`GameVariables.ApplyFacilityCosts()` 是唯一生产调用点）。
	/// 传 `null` = 该表项留空 → 回落兜底默认值；≤ 0 / NaN / ∞ 一律按留空处理（表侧 `GameVariables`
	/// 已把「填了坏值」判成坏表，这里只兜底不静默改写）。
	/// </summary>
	public static void Apply(float? operationCost, float? forestForageCost)
	{
		OperationCost = Sanitize(operationCost, DefaultOperationCost);
		ForestForageCost = Sanitize(forestForageCost, DefaultForestForageCost);
	}

	/// <summary>回到兜底默认值（单测用；也可作「重新读表」的前置）。</summary>
	public static void Reset() => Apply(null, null);

	private static float Sanitize(float? value, float fallback) =>
		value.HasValue && value.Value > 0f && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value)
			? value.Value
			: fallback;
}
