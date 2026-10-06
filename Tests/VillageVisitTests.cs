// VillageVisitTests.cs
// 村庄场景状态机的走法 / 触发 / 抑制 / 存档恢复（村庄地图交互案 §四 / §五 / §六 / §八 / §十一 第 3、4、7、8 条）。
// 口径：**局内移动不消耗时间点**（§四，2026-10-05 口径纠偏）—— 移动入口不收时间点参数即可证明。
// 与 `RunFacilityCostsTests` 同集合：后者会临时改写设施代价的全局运行期取值，同集合 = 串行，不互相踩。
using System;
using System.IO;
using System.Linq;
using Xunit;

[Collection("RunFacilityCosts")]
public class VillageVisitTests
{
	private static string[] ReadTable(string relativePath)
	{
		string path = Path.Combine(AppContext.BaseDirectory, relativePath);
		Assert.True(File.Exists(path), $"缺表：{path}（检查 Tests.csproj 的 None Include 是否拷贝）");
		return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
	}

	private static VillageLayoutData LoadTable() =>
		VillageLayoutCatalog.ParseLines(ReadTable(Path.Combine("WorldMap", "VillageLayout.csv")));

	private static VillageVisit NewVisit()
	{
		VillageVisit visit = new VillageVisit(LoadTable());
		visit.PlaceAtEntrance();
		return visit;
	}

	// ── 落点与可走格 ──

	[Fact]
	public void PlaceAtEntrance_LandsOnEntranceTile_AndInnerRingIsWalkable()
	{
		VillageVisit visit = NewVisit();

		Assert.Equal(5, visit.EntranceNodeId);
		Assert.Equal(30, visit.ExitNodeId);
		Assert.Equal(5, visit.PlayerNodeId);
		Assert.Equal(VillageLayout.InnerTileCount, visit.PassableCount);

		// 内圈 19 格全可走、外圈 18 格（设施本体）全不可走。
		for (int nodeId = 0; nodeId < VillageLayout.TileCount; nodeId++)
		{
			Assert.Equal(VillageLayout.IsInnerRing(nodeId), visit.IsWalkable(nodeId));
		}
	}

	[Fact]
	public void FacilityTiles_AreNotWalkable_AndTheirEntrancesAre()
	{
		VillageVisit visit = NewVisit();

		foreach (VillagePlotKind kind in new[]
		{
			VillagePlotKind.Inn, VillagePlotKind.Guesthouse, VillagePlotKind.Smithy, VillagePlotKind.Restaurant, VillagePlotKind.Forest,
		})
		{
			VillagePlot plot = visit.Layout.Find(kind);
			foreach (int nodeId in plot.NodeIds)
			{
				Assert.False(visit.IsWalkable(nodeId), $"{kind} 的占格 {nodeId} 不该可走");
			}

			Assert.True(visit.IsWalkable(plot.EntranceNodeId), $"{kind} 的入口格 {plot.EntranceNodeId} 必须可走");
			Assert.Equal(kind, visit.PlotOfEntrance(plot.EntranceNodeId).Kind);
		}
	}

	// ── 走法校验 ──

	[Fact]
	public void TryStep_RejectsNonAdjacentAndBlockedTiles_WithoutMoving()
	{
		VillageVisit visit = NewVisit();

		// 与入口格 5 不相邻（但可走：17 = 内圈空地，距离 2 格）。
		Assert.False(visit.TryStep(17, out _, out _, out string far));
		Assert.Equal(VillageVisit.NotAdjacentText, far);
		Assert.Equal(5, visit.PlayerNodeId);

		// 相邻但是设施本体（1 = 民宿占格）：不可走优先于一切。
		Assert.False(visit.TryStep(1, out _, out _, out string blocked));
		Assert.Equal(VillageVisit.BlockedTileText, blocked);
		Assert.Equal(5, visit.PlayerNodeId);
	}

	[Fact]
	public void TryStep_IsFreeInsideTheVillage_AndNeverBlockedByTimePoints()
	{
		VillageVisit visit = NewVisit();

		// 村庄案 §四（2026-10-05 口径纠偏）：局内移动不消耗时间点、也不消耗能量 ——
		// 走法入口根本不收时间点参数，任何时间点余量（哪怕当天已耗尽）都走得动。
		Assert.True(visit.TryStep(6, out VillageTileTrigger trigger, out VillagePlot plot, out string error), error);
		Assert.Equal(VillageTileTrigger.None, trigger);
		Assert.Null(plot);
		Assert.Equal(6, visit.PlayerNodeId);

		// 再走一格也不因「时间点不足」被拒：位置只由走法决定。
		Assert.True(visit.TryStep(11, out _, out _, out error), error);
		Assert.Equal(11, visit.PlayerNodeId);
	}

	// ── 入口触发与「稍后」抑制 ──

	[Fact]
	public void InnEntrance_Triggers_ThenSuppressedUntilWalkingAway()
	{
		VillageVisit visit = NewVisit();

		// 入口格 5 → 6（空地）→ 11 → 17 → 23（旅馆入口）—— 逐格相邻的内圈通路。
		Assert.True(visit.TryStep(6, out _, out _, out _));
		Assert.True(visit.TryStep(11, out _, out _, out _));
		Assert.True(visit.TryStep(17, out _, out _, out _));
		Assert.True(visit.TryStep(23, out VillageTileTrigger trigger, out VillagePlot plot, out _));
		Assert.Equal(VillageTileTrigger.Facility, trigger);
		Assert.Equal(VillagePlotKind.Inn, plot.Kind);

		// 点 `稍后`：站在入口格上不再弹（村庄案 §十二 第 3 条）。
		Assert.True(visit.NoteEntranceHandled());
		Assert.Equal(23, visit.LastTriggeredEntranceNodeId);
		Assert.Null(visit.PendingEntranceAt(23));

		// 走开一格（17）→ 抑制解除 → 走回来重新触发。
		Assert.True(visit.TryStep(17, out _, out _, out _));
		Assert.Equal(-1, visit.LastTriggeredEntranceNodeId);
		Assert.True(visit.TryStep(23, out trigger, out plot, out _));
		Assert.Equal(VillageTileTrigger.Facility, trigger);
		Assert.Equal(VillagePlotKind.Inn, plot.Kind);
	}

	[Fact]
	public void ExitTile_TriggersExit()
	{
		VillageVisit visit = NewVisit();

		// 入口格 5 → 6 → 12 → 18 → 25 → 30（离开格）—— 逐格相邻的内圈通路。
		int[] path = { 6, 12, 18, 25, 30 };
		for (int i = 0; i < path.Length; i++)
		{
			Assert.True(visit.TryStep(path[i], out VillageTileTrigger trigger, out VillagePlot plot, out string error), error);
			if (i < path.Length - 1)
			{
				Assert.Equal(VillageTileTrigger.None, trigger);
			}
			else
			{
				Assert.Equal(VillageTileTrigger.Exit, trigger);
				Assert.Equal(VillagePlotKind.Exit, plot.Kind);
			}
		}
	}

	[Fact]
	public void EveryFacilityEntrance_IsReachableFromTheEntranceTile()
	{
		VillageVisit visit = NewVisit();

		// 内圈从入口格 BFS 全覆盖（与 `VillageLayout.Validate` 的第 ⑤ 条同源）：5 个门都在可达集内。
		var reached = VillageLayout.ReachableFrom(visit.EntranceNodeId, VillageLayout.PassableTiles());
		foreach (VillagePlotKind kind in new[]
		{
			VillagePlotKind.Inn, VillagePlotKind.Guesthouse, VillagePlotKind.Smithy, VillagePlotKind.Restaurant, VillagePlotKind.Forest,
		})
		{
			int entrance = visit.Layout.Find(kind).EntranceNodeId;
			Assert.Contains(entrance, reached);
			Assert.NotNull(visit.PlotOfEntrance(entrance));
		}
	}

	// ── 设施代价与形态表 ──

	[Fact]
	public void FacilityCosts_EntryIsNotAnOperation_AndEachOperationIsSharedAcrossFacilities()
	{
		// 2026-10-05 第四轮口径（用户裁定）：进入设施**本身不是操作**（不消耗时间点）；
		// **每次操作固定消耗少量时间点**（默认 0.1，来自全局数据表 `VillageOperationTimePointCost`）；
		// 树林搜寻**另配**一个值（`ForestForageTimePointCost`）；
		// 部分操作额外收少量金币（锻造 = 配方金币、点菜 = 菜价）。
		float operation = VillageVisit.OperationTimePointCost;
		Assert.True(operation > 0f);
		Assert.Equal(operation, SmithyCrafting.CraftTimePointCost);
		Assert.Equal(operation, RestaurantTrade.CookTimePointCost);
		Assert.Equal(operation, RestaurantTrade.OrderTimePointCost);
		Assert.NotEqual(operation, VillageForage.TimePointCost);   // 树林与村庄其他操作**分开配**

		// 门槛边界（`CanOperate` 收的是**进程**）：剩余正好 = 一次操作的量 → 可操作。
		// 注：当前 0.1 = 时间点的最小计量单位（1 回合），进程里不存在「比一次操作更小、又非 0」的剩余，
		// 因此「不够」状态只在表值被调大到 ≥ 0.2 时出现 —— 这一档按条件断言，改表不用改测试。
		Assert.True(VillageVisit.CanOperate(RunTimePoints.PointsPerDay - operation));
		if (operation >= RunTimePoints.Step * 2f && operation < RunTimePoints.PointsPerDay)
		{
			Assert.False(VillageVisit.CanOperate(RunTimePoints.PointsPerDay - (operation - RunTimePoints.Step)));
		}

		// 文案含去处指引（用户口径「必须前往旅馆或民宿进行休息」）。
		Assert.Equal($"时间点不足：需要 {RunTimePoints.Format(operation)}，当前剩余 0.4。请前往旅馆或民宿过夜，或回营地结束当天。",
			VillageVisit.OperationTimePointShortText(RunTimePoints.PointsPerDay - 0.4f));

		Assert.Equal(1, VillageVisit.EntryGoldCost(VillagePlotKind.Inn));
		Assert.Equal(0, VillageVisit.EntryGoldCost(VillagePlotKind.Guesthouse));
		Assert.Equal(0, VillageVisit.EntryGoldCost(VillagePlotKind.Forest));
		Assert.Equal(0, VillageVisit.EntryGoldCost(VillagePlotKind.Smithy));
		Assert.Equal(0, VillageVisit.EntryGoldCost(VillagePlotKind.Restaurant));

		// 专用界面：锻铁铺 / 餐厅；确认 tips：旅馆 / 民宿 / 树林（村庄案 §六）。
		Assert.True(VillageVisit.HasDedicatedUi(VillagePlotKind.Smithy));
		Assert.True(VillageVisit.HasDedicatedUi(VillagePlotKind.Restaurant));
		Assert.False(VillageVisit.HasDedicatedUi(VillagePlotKind.Inn));
		Assert.False(VillageVisit.HasDedicatedUi(VillagePlotKind.Guesthouse));
		Assert.False(VillageVisit.HasDedicatedUi(VillagePlotKind.Forest));
	}

	[Fact]
	public void IsNight_UsesTheOnePointZeroThreshold()
	{
		Assert.False(VillageVisit.IsNight(2.4f));   // 剩余 1.6 → 白天
		Assert.True(VillageVisit.IsNight(3.0f));    // 剩余 1.0 → 晚上（村庄案 §八：≤ 1.0）
		Assert.True(VillageVisit.IsNight(3.9f));    // 剩余 0.1 → 晚上
		Assert.False(VillageVisit.IsNight(4.0f));   // 新一天开始，剩余 4.0 → 白天
	}

	// ── 存档恢复 ──

	[Fact]
	public void SaveRoundTrip_KeepsPositionAndSuppression()
	{
		VillageVisit visit = NewVisit();
		Assert.True(visit.TryStep(6, out _, out _, out _));
		Assert.True(visit.TryStep(11, out _, out _, out _));
		Assert.True(visit.TryStep(17, out _, out _, out _));
		Assert.True(visit.TryStep(23, out _, out _, out _));
		Assert.True(visit.NoteEntranceHandled());

		RunVillageStateSave state = new RunVillageStateSave();
		visit.WriteTo(state);
		Assert.Equal(23, state.PlayerNodeId);
		Assert.Equal(23, state.LastTriggeredEntranceNodeId);

		// 读档重进：位置与抑制标记都保留（村庄案 §十一 第 8 条）。
		VillageVisit reloaded = new VillageVisit(LoadTable());
		reloaded.RestoreFrom(state);
		Assert.Equal(23, reloaded.PlayerNodeId);
		Assert.Null(reloaded.PendingEntranceAt(23));
	}

	[Fact]
	public void RestoreFrom_InvalidSavedTile_FallsBackToEntrance()
	{
		VillageVisit visit = new VillageVisit(LoadTable());

		// 空档（-1）→ 入口格。
		visit.RestoreFrom(new RunVillageStateSave());
		Assert.Equal(visit.EntranceNodeId, visit.PlayerNodeId);

		// 存了设施本体（外圈，不可走）→ 兜底回入口格。
		visit.RestoreFrom(new RunVillageStateSave { PlayerNodeId = 15, LastTriggeredEntranceNodeId = 23 });
		Assert.Equal(visit.EntranceNodeId, visit.PlayerNodeId);
		Assert.Equal(-1, visit.LastTriggeredEntranceNodeId);
	}

	[Fact]
	public void NullLayout_IsRejected()
	{
		Assert.Throws<ArgumentNullException>(() => new VillageVisit(null));
	}
}
