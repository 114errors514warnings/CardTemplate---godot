// VillageVisit.cs
// 村庄场景的「一次访问」状态机（**纯逻辑**，无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/村庄地图交互案.md
//   §四（**局内移动不消耗时间点、也不消耗能量**：时间点只在世界地图「节点 → 相邻节点」那一跳消耗）、
//   §五（踏入入口格即触发 / 走开再走回才能重新触发）、
//   §2.2（设施本体不可通行、入口格是唯一例外、内圈 19 格全可走）、
//   §六（旅馆 / 民宿 / 树林走确认 tips，锻铁铺 / 餐厅走专用界面）、
//   §八（晚上 = 当天剩余 ≤ 1.0）、§十二（待拍板默认值：不自动转营地、`稍后` 不原地重弹）。
// 分工：本文件只判「能不能走 / 踏上去触发什么 / 拒绝原因文案」；
//   **局内移动不扣任何代价**（不扣时间点、不扣能量）—— 只有「设施进入代价」的时间点在宿主侧扣
//   （`RunSession.Place.cs` / `VillageScene`）；本文件不碰 Godot、不碰存档对象。
using System;
using System.Collections.Generic;

/// <summary>踏上某格之后的触发分类（村庄案 §五：入口格 = 触发点、离开格 = 回世界地图）。</summary>
public enum VillageTileTrigger
{
	/// <summary>空地：什么也不触发（设施本体走不进去，见 `VillageLayout.BlockedTiles`）。</summary>
	None = 0,

	/// <summary>设施入口格：按设施分流（旅馆 / 民宿 / 树林 → 确认 tips；锻铁铺 / 餐厅 → 专用界面）。</summary>
	Facility = 1,

	/// <summary>离开格：回世界地图并把村庄节点标记为已访问（村庄案 §十一 第 7 条）。</summary>
	Exit = 2,
}

/// <summary>
/// 村庄内的一次访问：所在格、可走格、移动校验、入口触发与「稍后」抑制。
/// 全部判断只读传入的 `VillageLayoutData`（= `VillageLayout.csv` 定稿表的解析结果）。
/// </summary>
public sealed class VillageVisit
{
	/// <summary>点不动的原因文案（设施本体 / 越界格）。</summary>
	public const string BlockedTileText = "那里是建筑，走不进去。";

	/// <summary>不相邻的原因文案。</summary>
	public const string NotAdjacentText = "只能走到相邻的格子。";

	/// <summary>没有落位的原因文案。</summary>
	public const string NotPlacedText = "还没进入村庄。";

	private readonly VillageLayoutData data;
	private readonly HashSet<int> passable;
	private readonly Dictionary<int, VillagePlot> plotByEntrance = new Dictionary<int, VillagePlot>();
	private readonly Dictionary<int, VillagePlot> plotByTile = new Dictionary<int, VillagePlot>();

	public VillageVisit(VillageLayoutData data)
	{
		this.data = data ?? throw new ArgumentNullException(nameof(data));

		// 可走格 = 内圈 19 格（判据与 `VillageLayout.Validate` 的第 ⑤ 条同源）。
		passable = VillageLayout.PassableTiles();
		foreach (VillagePlot plot in data.Plots)
		{
			foreach (int nodeId in plot.NodeIds)
			{
				plotByTile[nodeId] = plot;
			}

			if (plot.HasEntrance)
			{
				plotByEntrance[plot.EntranceNodeId] = plot;
			}
		}
	}

	/// <summary>版图数据（渲染侧要读每个单位的占格与名称牌）。</summary>
	public VillageLayoutData Layout => data;

	/// <summary>村庄入口格（世界地图进来时的落点，村庄案 §2.2）。</summary>
	public int EntranceNodeId => FirstNodeOf(VillagePlotKind.Entrance);

	/// <summary>离开格（踏入即回世界地图，村庄案 §2.2）。</summary>
	public int ExitNodeId => FirstNodeOf(VillagePlotKind.Exit);

	/// <summary>角色当前所在格 NodeId；-1 = 还没落位（应由 `RestoreFrom` / `PlaceAtEntrance` 落位）。</summary>
	public int PlayerNodeId { get; private set; } = -1;

	/// <summary>
	/// 已经处理过的入口格（点过 `进入` / `稍后` 的那一格）：站在它上面不会再弹第二次，
	/// 走到别的格（`TryStep` 落到非本格）即自动解除 → 「走开再走回才能重新触发」（村庄案 §五 / §十二 第 3 条）。
	/// 与存档字段 `RunVillageStateSave.LastTriggeredEntranceNodeId` 同名同义。
	/// </summary>
	public int LastTriggeredEntranceNodeId { get; private set; } = -1;

	/// <summary>可走格数（应为 19 = 内圈；与 `VillageLayout.InnerTileCount` 互证）。</summary>
	public int PassableCount => passable.Count;

	/// <summary>
	/// 可走格集合（升序 NodeId；`run.village.state` 与点打脚本据此免去猜格号）。
	/// 只读快照，调用方改不到内部集合。
	/// </summary>
	public IReadOnlyCollection<int> WalkableNodeIds
	{
		get
		{
			List<int> sorted = new List<int>(passable);
			sorted.Sort();
			return sorted;
		}
	}

	// ── 设施代价与形态（纯查询：界面 / 烟测 / 单测都读这里）──

	/// <summary>
	/// 在设施里**执行一次操作**的时间点代价（村庄案 §四，2026-10-05 第四轮口径）：
	/// **默认 0.1**，正式数值来自全局数据表 `DataBase/GameVariables.csv` 的 `VillageOperationTimePointCost`
	/// （由 `VillageScene._Ready` 里的 `GameVariables.ApplyFacilityCosts()` 灌进 `RunFacilityCosts`；
	/// 表没接上时走 `RunFacilityCosts.DefaultOperationCost`）。
	/// 计入的操作 = 搜寻 / 锻造 / 烹饪 / **点菜（餐厅买入）**等「每次操作」；树林搜寻**单独配**一个值，
	/// 见 `VillageForage.TimePointCost`。**进入设施本身不是操作**：不看、不扣时间点
	/// （旅馆的 1 金币是过夜费，见 `EntryGoldCost`）；出售食物等纯交易也不计。
	/// 部分操作在时间点之外还收少量金币：锻造（配方金币）、点菜（菜价）。
	/// </summary>
	public static float OperationTimePointCost => RunFacilityCosts.OperationCost;

	/// <summary>
	/// 时间点不足、不能执行设施操作时的去处指引（村庄案 §四：不足一次操作的量必须先去休息）。
	/// </summary>
	public const string RestHint = "请前往旅馆或民宿过夜，或回营地结束当天。";

	/// <summary>
	/// 能否再执行一次设施操作（村庄案 §四）：当天剩余 ≥ 一次操作的代价才可执行；
	/// 不足 → 拒绝操作并引导去旅馆 / 民宿过夜（过夜 = 推进到新一天 → 时间点补满 4.0）。
	/// </summary>
	public static bool CanOperate(float totalTimePoints) =>
		RunTimePoints.CanSpend(totalTimePoints, OperationTimePointCost);

	/// <summary>
	/// 设施操作被时间点卡住的一行原因（含去处指引）：
	/// `时间点不足：需要 0.1，当前剩余 0.0。请前往旅馆或民宿过夜，或回营地结束当天。`
	/// </summary>
	public static string OperationTimePointShortText(float totalTimePoints) =>
		RunTimePoints.ShortRestText(OperationTimePointCost, RunTimePoints.RemainingToday(totalTimePoints), RestHint);

	/// <summary>踏入入口格的金币代价：只有旅馆收 1 金币（旅馆案 §一），其余免费。</summary>
	public static int EntryGoldCost(VillagePlotKind kind) => kind == VillagePlotKind.Inn ? VillageLodging.InnGold : 0;

	/// <summary>
	/// 该设施是否有专用界面（用户口径 2026-10-03：只有树林 / 民宿 / 旅馆是确认 tips，
	/// 锻铁铺 / 餐厅要各自的界面；见村庄案 §六）。
	/// </summary>
	public static bool HasDedicatedUi(VillagePlotKind kind) =>
		kind == VillagePlotKind.Smithy || kind == VillagePlotKind.Restaurant;

	/// <summary>是「晚上」（村庄案 §八：当天剩余 ≤ 1.0；浮点容差与 `RunTimePoints.CanSpend` 同口径）。</summary>
	public static bool IsNight(float totalTimePoints) => VillageLayout.IsNight(RunTimePoints.RemainingToday(totalTimePoints));

	/// <summary>时间点不足的一行原因统一由 `RunTimePoints.ShortRestText` 出（见 `OperationTimePointShortText`）。</summary>

	// ── 位置与走法 ──

	/// <summary>该格是否可踏（内圈 19 格；设施本体恒不可踏，村庄案 §2.2）。</summary>
	public bool IsWalkable(int nodeId) => passable.Contains(nodeId);

	/// <summary>该格与本格是否相邻（六向，与 `MapGeometry` 同口径）。</summary>
	public bool IsAdjacentToPlayer(int nodeId) =>
		PlayerNodeId >= 0 && VillageLayout.NeighborsOf(PlayerNodeId).Contains(nodeId);

	/// <summary>该格所属的图纸单位（设施本体 / 入口格 / 离开格）；空地返回 null。</summary>
	public VillagePlot PlotOfTile(int nodeId) =>
		plotByTile.TryGetValue(nodeId, out VillagePlot plot) ? plot : null;

	/// <summary>该格若是某设施的入口格，返回那个设施；否则 null。</summary>
	public VillagePlot PlotOfEntrance(int nodeId) =>
		plotByEntrance.TryGetValue(nodeId, out VillagePlot plot) ? plot : null;

	/// <summary>本格上是否还有「待处理」的入口（站上去之后是否能弹提示）。</summary>
	public VillagePlot PendingEntranceAtPlayer => PendingEntranceAt(PlayerNodeId);

	/// <summary>指定格上是否还有「待处理」的入口（已处理过的那一格返回 null）。</summary>
	public VillagePlot PendingEntranceAt(int nodeId) =>
		nodeId == LastTriggeredEntranceNodeId ? null : PlotOfEntrance(nodeId);

	/// <summary>
	/// 能不能走这一格（**只判走法** —— 村庄案 §四：局内移动不消耗时间点、也不消耗能量）；
	/// 返回 false 时 `error` 是可直接显示的一行原因。
	/// </summary>
	public bool CanStep(int nodeId, out string error)
	{
		error = string.Empty;
		if (PlayerNodeId < 0)
		{
			error = NotPlacedText;
			return false;
		}

		if (!VillageLayout.TryCoordOf(nodeId, out _))
		{
			error = "那一格不在村庄里。";
			return false;
		}

		if (!passable.Contains(nodeId))
		{
			error = BlockedTileText;
			return false;
		}

		if (!IsAdjacentToPlayer(nodeId))
		{
			error = NotAdjacentText;
			return false;
		}

		return true;
	}

	/// <summary>
	/// 走一格：**只改位置、不收任何代价**（村庄案 §四：局内移动不消耗时间点，也不消耗能量 ——
	/// 时间点只在世界地图「节点 → 相邻节点」那一跳由世界地图侧扣）。
	/// `trigger` = 踏上去之后该做什么；`plot` = 触发对象（Exit 时为离开格行、None 时为 null）。
	/// 返回 false 时位置不变、`error` 给原因。
	/// </summary>
	public bool TryStep(int nodeId, out VillageTileTrigger trigger, out VillagePlot plot, out string error)
	{
		trigger = VillageTileTrigger.None;
		plot = null;
		if (!CanStep(nodeId, out error))
		{
			return false;
		}

		PlayerNodeId = nodeId;

		// 走到「已处理入口格」以外的任何格 → 解除抑制（村庄案 §五「要再触发需走开再走回」）。
		if (LastTriggeredEntranceNodeId != nodeId)
		{
			LastTriggeredEntranceNodeId = -1;
		}

		if (nodeId == ExitNodeId)
		{
			trigger = VillageTileTrigger.Exit;
			plot = PlotOfTile(nodeId);
			return true;
		}

		VillagePlot entrance = PlotOfEntrance(nodeId);
		if (entrance != null)
		{
			trigger = VillageTileTrigger.Facility;
			plot = entrance;
		}

		return true;
	}

	/// <summary>
	/// 记下「本格入口已处理」（点过 `进入` 或 `稍后`）：站在它上面不会再弹，走开再走回才重新触发。
	/// 返回 false = 当前所在格不是入口格（重复调用可忽略）。
	/// </summary>
	public bool NoteEntranceHandled()
	{
		if (PlotOfEntrance(PlayerNodeId) == null)
		{
			return false;
		}

		LastTriggeredEntranceNodeId = PlayerNodeId;
		return true;
	}

	// ── 存档同步（村庄案 §十 第 6 条：所在格 + 已处理入口格）──

	/// <summary>把位置与抑制标记写进存档字段（`RunSession.Save()` 由调用方负责）。</summary>
	public void WriteTo(RunVillageStateSave state)
	{
		if (state == null)
		{
			return;
		}

		state.PlayerNodeId = PlayerNodeId;
		state.LastTriggeredEntranceNodeId = LastTriggeredEntranceNodeId;
	}

	/// <summary>
	/// 读档恢复位置（村庄案 §十一 第 8 条：村庄内退出再读档 → 回到村庄、设施标记保留）。
	/// 存档里的格非法（-1 / 越界 / 设施本体）时落到入口格。
	/// </summary>
	public void RestoreFrom(RunVillageStateSave state)
	{
		int nodeId = state == null ? -1 : state.PlayerNodeId;
		if (passable.Contains(nodeId))
		{
			PlayerNodeId = nodeId;
			LastTriggeredEntranceNodeId = state.LastTriggeredEntranceNodeId;
			return;
		}

		PlayerNodeId = EntranceNodeId;
		LastTriggeredEntranceNodeId = -1;
	}

	/// <summary>落到入口格（首次进村；村庄案 §十一 第 1 条）。</summary>
	public void PlaceAtEntrance()
	{
		PlayerNodeId = EntranceNodeId;
		LastTriggeredEntranceNodeId = -1;
	}

	/// <summary>
	/// **烟测 / 调试通道**：直接把角色放到某格（不校验走法），并清掉入口抑制。
	/// 生产路径一律走 `CanStep` → `TryStep`；这里只给烟测与 API 调试口用。
	/// </summary>
	public bool DebugPlaceAt(int nodeId)
	{
		if (!passable.Contains(nodeId))
		{
			return false;
		}

		PlayerNodeId = nodeId;
		LastTriggeredEntranceNodeId = -1;
		return true;
	}

	private int FirstNodeOf(VillagePlotKind kind)
	{
		VillagePlot plot = data.Find(kind);
		return plot != null && plot.NodeIds.Count > 0 ? plot.NodeIds[0] : -1;
	}
}
