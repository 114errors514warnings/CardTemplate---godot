// RunFacilityCostsTests.cs
// 全局数据表里的**设施操作代价**落地后的纯逻辑取值（2026-10-05 第四轮口径，用户裁定）：
//   村庄设施「每次操作」= `VillageOperationTimePointCost`（默认 0.1）/ 树林「单次搜寻」= `ForestForageTimePointCost`
//   （独立项，当前 1.0）；两项**分开配**，各设施规则层读同一份运行期取值。
// 分工：表 → `GameVariables` 的解析由 `GameVariablesTableTests` 做文件级校验；
//   Godot 侧「`GameVariables.Load().ApplyFacilityCosts()` → `RunFacilityCosts`」的接线由地点场景
//   （`VillageScene._Ready`）覆盖；本文件只验 `Apply` / `Reset` 的语义与各规则层的读数。
// ⚠️ `Apply` 改的是全局静态值：本类用例一律 `try/finally` 复位，且与读同一份取值的
//   `VillageVisitTests` / `VillageFacilityLogicTests` 同属 `[Collection("RunFacilityCosts")]`（串行，不互相踩）。
using Xunit;

[Collection("RunFacilityCosts")]
public class RunFacilityCostsTests
{
	[Fact]
	public void Apply_OverridesBothCosts_AndResetRestoresTheDefaults()
	{
		try
		{
			RunFacilityCosts.Apply(0.2f, 0.5f);

			Assert.Equal(0.2f, RunFacilityCosts.OperationCost, 5);
			Assert.Equal(0.5f, RunFacilityCosts.ForestForageCost, 5);

			// 各设施规则层读同一份运行期取值：村庄操作一处改、四处生效；树林是**独立**项。
			Assert.Equal(0.2f, VillageVisit.OperationTimePointCost, 5);
			Assert.Equal(0.2f, SmithyCrafting.CraftTimePointCost, 5);
			Assert.Equal(0.2f, RestaurantTrade.CookTimePointCost, 5);
			Assert.Equal(0.2f, RestaurantTrade.OrderTimePointCost, 5);
			Assert.Equal(0.5f, VillageForage.TimePointCost, 5);

			// 门槛随表值走（进程 → 当天剩余）：
			Assert.True(VillageVisit.CanOperate(RunTimePoints.PointsPerDay - 0.2f));    // 剩余正好 = 一次操作
			Assert.False(VillageVisit.CanOperate(RunTimePoints.PointsPerDay - 0.1f));   // 剩余只有一半 → 不行
			Assert.True(VillageForage.CanSearch(0.5f));
			Assert.False(VillageForage.CanSearch(0.4f));
			Assert.True(RestaurantTrade.CanOrder(0.2f));
			Assert.False(RestaurantTrade.CanOrder(0.1f));
			Assert.True(SmithyCrafting.CanCraft(0.2f));
			Assert.False(SmithyCrafting.CanCraft(0.1f));
		}
		finally
		{
			RunFacilityCosts.Reset();
		}

		Assert.Equal(RunFacilityCosts.DefaultOperationCost, RunFacilityCosts.OperationCost, 5);
		Assert.Equal(RunFacilityCosts.DefaultForestForageCost, RunFacilityCosts.ForestForageCost, 5);
	}

	[Fact]
	public void Apply_TreatsMissingOrInvalidValuesPerItemAsFallback()
	{
		try
		{
			// 两项都留空（表里没填）= 保持兜底默认值（与移动代价「留空 = 回落默认」同口径）。
			RunFacilityCosts.Apply(null, null);
			Assert.Equal(RunFacilityCosts.DefaultOperationCost, RunFacilityCosts.OperationCost, 5);
			Assert.Equal(RunFacilityCosts.DefaultForestForageCost, RunFacilityCosts.ForestForageCost, 5);

			// 非正数 / NaN 也按留空处理（表侧 `GameVariables` 已把「填了坏值」判成坏表，这里不静默改写语义）。
			RunFacilityCosts.Apply(0f, float.NaN);
			Assert.Equal(RunFacilityCosts.DefaultOperationCost, RunFacilityCosts.OperationCost, 5);
			Assert.Equal(RunFacilityCosts.DefaultForestForageCost, RunFacilityCosts.ForestForageCost, 5);

			// 两项各自独立判定：一项坏不影响另一项取到表值。
			RunFacilityCosts.Apply(-1f, 0.7f);
			Assert.Equal(RunFacilityCosts.DefaultOperationCost, RunFacilityCosts.OperationCost, 5);
			Assert.Equal(0.7f, RunFacilityCosts.ForestForageCost, 5);
		}
		finally
		{
			RunFacilityCosts.Reset();
		}
	}
}
