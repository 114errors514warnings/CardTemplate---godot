// RunFacilityCosts.cs
// 地点设施「操作代价」的**运行期取值**（纯逻辑，无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/村庄地图交互案.md §四（2026-10-05 第四轮口径）、
//   README/功能说明文档/数据系统/数据配置/配表规范.md §全局变量（第 7 / 8 列）。
// 分工（数值的**唯一来源**是全局数据表，代码只留兜底默认值，与移动代价 `MoveTimePointCost` 同口径）：
//   表 `DataBase/GameVariables.csv` 的 `VillageOperationTimePointCost` / `ForestForageTimePointCost`
//     →（Godot 侧）`GameVariables.ApplyFacilityCosts()`
//     → 本类 → `RunFacilityCosts.OperationTimePointCost` / `VillageForage.TimePointCost` /
//       `SmithyCrafting.CraftTimePointCost` / `RestaurantTrade.CookTimePointCost` / `RestaurantTrade.OrderTimePointCost`
//     → 界面（`SmithyUi` / `RestaurantUi`）/ 结算（`RunSession.Place`）/ 单测。
// 2026-10-06（村庄专用场景撤除、改走统一关卡流程）：原 `VillageVisit` 里的四个共用小件
//   （`OperationTimePointCost` / `RestHint` / `CanOperate` / `OperationTimePointShortText`）迁进本类 ——
//   这些是**设施操作口径**，不是村庄版图口径，故与两笔代价同住一处（消费方不变：界面 / 结算 / 单测）。
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
	/// 设施「一次操作」的时间点代价（= <see cref="OperationCost"/> 的别名，沿用村庄案 §四 的字段名；
	/// 2026-10-06 从 `VillageVisit` 迁来 —— 读这里与读 `OperationCost` 完全等价）。
	/// </summary>
	public static float OperationTimePointCost => OperationCost;

	/// <summary>
	/// 时间点不足、不能执行设施操作时的去处指引（村庄案 §四：不足一次操作的量必须先去休息）。
	/// 设施界面（`SmithyUi` / `RestaurantUi`）与各设施规则层共用这一份文案（2026-10-06 从 `VillageVisit` 迁来）。
	/// </summary>
	public const string RestHint = "请前往旅馆或民宿过夜，或回营地结束当天。";

	/// <summary>
	/// 能否再执行一次设施操作（村庄案 §四）：当天剩余 ≥ 一次操作的代价才可执行；
	/// 不足 → 拒绝操作并引导去旅馆 / 民宿过夜（过夜 = 推进到新一天 → 时间点补满 4.0）。
	/// 树林是**独立代价**，不走这里（见 `VillageForage.CanSearch`）。
	/// </summary>
	public static bool CanOperate(float totalTimePoints) =>
		RunTimePoints.CanSpend(totalTimePoints, OperationCost);

	/// <summary>
	/// 设施操作被时间点卡住的一行原因（含去处指引）：
	/// `时间点不足：需要 0.1，当前剩余 0.0。请前往旅馆或民宿过夜，或回营地结束当天。`
	/// </summary>
	public static string OperationTimePointShortText(float totalTimePoints) =>
		RunTimePoints.ShortRestText(OperationCost, RunTimePoints.RemainingToday(totalTimePoints), RestHint);

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
