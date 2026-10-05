// VillageLayout.cs
// 村庄内景版图的**纯逻辑**（无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/村庄地图交互案.md §2.1 / §2.2 / §2.3 / §八。
//   · R = 3 的正六边形（3R(R+1)+1 = 37 格）：外圈 18 格 + 内圈 19 格，与 `MapGeometry` 同一套轴向
//     坐标（max(|q|,|r|,|q+r|) ≤ R）与邻接规则（六向），只是半径不同（局内世界地图是 R=6 / 127 格）；
//   · **NodeId = `MapGeometry.Generate` 的生成序（q 升序 + r 升序）** —— 与 `MapBoardNode.NodeId` 同号，
//     所以 `VillageLayout.csv` 里可以直接写 NodeId，运行时不必再做坐标换算；
//   · 5 个设施（旅馆 4 / 民宿 3 / 锻铁铺 3 / 餐厅 3 / 树林 5 = 18 格）**全部铺在外圈**，
//     内圈 19 格 = 5 个设施入口格 + 世界地图入口格 + 离开格 + 12 空地，本身就是半径 2 的完整六边形 → 天生连通；
//   · 六条不变式收在 `Validate`，配表改一行就红灯（村庄案 §2.3 的落档自查，落地时即本文件的单测）。
using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>村庄版图上一块「图纸单位」的种类（村庄案 §2.2 的 7 类行）。</summary>
public enum VillagePlotKind
{
	/// <summary>旅馆：北边横 4 格，入口格 1 个（确认 tips）。</summary>
	Inn = 0,
	/// <summary>民宿：西南边竖 3 格，入口格 1 个（确认 tips）。</summary>
	Guesthouse = 1,
	/// <summary>锻铁铺：西北边斜 3 格，入口格 1 个（专用界面）。</summary>
	Smithy = 2,
	/// <summary>餐厅：南边横 3 格，入口格 1 个（专用界面）。</summary>
	Restaurant = 3,
	/// <summary>树林：东边折线 5 格，入口格 1 个（确认 tips）。</summary>
	Forest = 4,
	/// <summary>村庄入口格：世界地图进来时角色的落点（不是设施）。</summary>
	Entrance = 5,
	/// <summary>离开格：踏入即回世界地图（不是设施）。</summary>
	Exit = 6,
}

/// <summary>`VillageLayout.csv` 的一行：一块占格单位（设施 / 入口格 / 离开格）。</summary>
public sealed class VillagePlot
{
	/// <summary>单位种类（7 类之一）。</summary>
	public VillagePlotKind Kind;

	/// <summary>图上名称牌文案（旅馆 / 民宿 / 锻铁铺 / 餐厅 / 树林 / 入口格 / 离开格）。</summary>
	public string DisplayName = string.Empty;

	/// <summary>占格 NodeId（升序；入口格 / 离开格各 1 个）。</summary>
	public List<int> NodeIds = new List<int>();

	/// <summary>入口格 NodeId；无入口格的单位（入口格 / 离开格）为 -1。</summary>
	public int EntranceNodeId = -1;

	/// <summary>是否是 5 个设施之一（入口格 / 离开格不算设施）。</summary>
	public bool IsFacility => Kind <= VillagePlotKind.Forest;

	/// <summary>该单位是否带入口格。</summary>
	public bool HasEntrance => EntranceNodeId >= 0;
}

/// <summary>
/// 村庄版图的生成与不变式（纯逻辑）。全部 NodeId 都由半径推导，不写死数字：
/// `NodeId = 该坐标在 (q 升序 + r 升序) 排列中的下标`，与 `MapGeometry.Generate` 的生成序一致。
/// </summary>
public static class VillageLayout
{
	/// <summary>村庄版图半径（村庄案 §2.1 定稿：R = 3）。</summary>
	public const int Radius = 3;

	/// <summary>总格数 = 3R(R+1)+1 = 37。</summary>
	public const int TileCount = 37;

	/// <summary>外圈格数（每条外边 R+1 个、6 条边、扣掉 6 个共享角格 = 6R = 18）。</summary>
	public const int OuterTileCount = 18;

	/// <summary>内圈格数（半径 ≤ R−1 的盘面 = 3(R−1)R+1 = 19）。</summary>
	public const int InnerTileCount = 19;

	/// <summary>「晚上」的时间点阈值（村庄案 §八：当天剩余 ≤ 1.0 视为晚上）。</summary>
	public const float NightRemainingThreshold = 1.0f;

	/// <summary>六向邻接偏移（与 `MapGeometry` 的 `AllNeighborOffsets` 同口径）。</summary>
	private static readonly (int Q, int R)[] NeighborOffsets =
	{
		(1, 0), (-1, 0), (0, 1), (0, -1), (1, -1), (-1, 1),
	};

	private static readonly List<AxialHex> Coords = BuildCoords();
	private static readonly Dictionary<AxialHex, int> NodeIdByCoord = BuildIndex();

	/// <summary>版图内全部坐标，按 NodeId 顺序（下标 = NodeId）。</summary>
	public static IReadOnlyList<AxialHex> AllCoords => Coords;

	private static List<AxialHex> BuildCoords()
	{
		List<AxialHex> list = new List<AxialHex>(TileCount);
		for (int q = -Radius; q <= Radius; q++)
		{
			for (int r = RMin(q); r <= RMax(q); r++)
			{
				list.Add(new AxialHex(q, r));
			}
		}

		return list;
	}

	private static Dictionary<AxialHex, int> BuildIndex()
	{
		Dictionary<AxialHex, int> index = new Dictionary<AxialHex, int>(TileCount);
		for (int i = 0; i < Coords.Count; i++)
		{
			index[Coords[i]] = i;
		}

		return index;
	}

	private static int RMin(int q) => Math.Max(-Radius, -Radius - q);

	private static int RMax(int q) => Math.Min(Radius, Radius - q);

	/// <summary>该坐标是否在版图内（与 `MapGeometry.IsInsideBoard` 同一判据）。</summary>
	public static bool IsInside(int q, int r) =>
		Math.Abs(q) <= Radius && Math.Abs(r) <= Radius && Math.Abs(q + r) <= Radius;

	/// <summary>坐标 → NodeId；版图外返回 -1。</summary>
	public static int NodeIdOf(int q, int r) => NodeIdByCoord.TryGetValue(new AxialHex(q, r), out int nodeId) ? nodeId : -1;

	/// <summary>NodeId → 坐标；越界返回 false。</summary>
	public static bool TryCoordOf(int nodeId, out AxialHex hex)
	{
		if (nodeId < 0 || nodeId >= Coords.Count)
		{
			hex = default;
			return false;
		}

		hex = Coords[nodeId];
		return true;
	}

	/// <summary>该格所在圈层（0 = 正中，R = 最外圈）；越界返回 -1。</summary>
	public static int RingOf(int nodeId)
	{
		if (!TryCoordOf(nodeId, out AxialHex hex))
		{
			return -1;
		}

		return Math.Max(Math.Abs(hex.Q), Math.Max(Math.Abs(hex.R), Math.Abs(hex.Q + hex.R)));
	}

	/// <summary>是否外圈格（设施只能占外圈）。</summary>
	public static bool IsOuterRing(int nodeId) => RingOf(nodeId) == Radius;

	/// <summary>是否内圈格（可走区：入口格 / 离开格 / 空地）。</summary>
	public static bool IsInnerRing(int nodeId) => RingOf(nodeId) >= 0 && RingOf(nodeId) <= Radius - 1;

	/// <summary>版图内的相邻格 NodeId（升序无关紧要；越界返回空集）。</summary>
	public static List<int> NeighborsOf(int nodeId)
	{
		List<int> neighbors = new List<int>(6);
		if (!TryCoordOf(nodeId, out AxialHex hex))
		{
			return neighbors;
		}

		foreach ((int dq, int dr) in NeighborOffsets)
		{
			int id = NodeIdOf(hex.Q + dq, hex.R + dr);
			if (id >= 0)
			{
				neighbors.Add(id);
			}
		}

		return neighbors;
	}

	/// <summary>
	/// 从 `startNodeId` 出发、**只走 `passable` 内的格**做 BFS，返回可达集合（含起点本身）。
	/// 村庄的走法 = 「设施本体不可通行、入口格与空地可通行」，故可达性判据由调用方给白名单。
	/// </summary>
	public static HashSet<int> ReachableFrom(int startNodeId, ISet<int> passable)
	{
		HashSet<int> visited = new HashSet<int>();
		if (passable == null || !passable.Contains(startNodeId))
		{
			return visited;
		}

		Queue<int> queue = new Queue<int>();
		queue.Enqueue(startNodeId);
		visited.Add(startNodeId);
		while (queue.Count > 0)
		{
			foreach (int next in NeighborsOf(queue.Dequeue()))
			{
				if (passable.Contains(next) && visited.Add(next))
				{
					queue.Enqueue(next);
				}
			}
		}

		return visited;
	}

	/// <summary>是否「晚上」（村庄案 §八：当天剩余 ≤ 1.0）。浮点容差与 `RunTimePoints.CanSpend` 同口径（正好 1.0 也算晚上）。</summary>
	public static bool IsNight(float remainingToday) => remainingToday <= NightRemainingThreshold + 1e-4f;

	/// <summary>
	/// 可通行格集合（村庄案 §2.2：设施本体不可通行、入口格是唯一例外，内圈 19 格全可走）。
	/// 5 个设施的入口格本身就在内圈，所以这里就是「内圈全部格」。
	/// </summary>
	public static HashSet<int> PassableTiles()
	{
		HashSet<int> passable = new HashSet<int>();
		for (int nodeId = 0; nodeId < Coords.Count; nodeId++)
		{
			if (IsInnerRing(nodeId))
			{
				passable.Add(nodeId);
			}
		}

		return passable;
	}

	/// <summary>含设施本体的「不可通行格」集合（供场景画灰 / 拒绝点击）。</summary>
	public static HashSet<int> BlockedTiles(VillageLayoutData data)
	{
		HashSet<int> blocked = new HashSet<int>();
		if (data == null)
		{
			return blocked;
		}

		foreach (VillagePlot plot in data.Plots)
		{
			if (plot.IsFacility)
			{
				foreach (int nodeId in plot.NodeIds)
				{
					blocked.Add(nodeId);
				}
			}
		}

		return blocked;
	}

	/// <summary>
	/// 六条不变式（村庄案 §2.3 的落档自查）：**空串 = 全过**，否则返回第一条失败的原因
	/// （可直接进单测 / 烟测断言，配表改一行就红灯）。
	/// ① 版图恰 37 格（18 外圈 + 19 内圈）；② 七行齐全、占格互不重叠且各设施自身连通；
	/// ③ 设施占格全在外圈；④ 入口格在内圈、与本设施占格相邻、不与别的入口格重复、不落在任何占格里；
	/// ⑤ 内圈 19 格从入口格 BFS 全覆盖（含离开格与 5 个门）；⑥ 入口格 ≠ 离开格。
	/// </summary>
	public static string Validate(VillageLayoutData data)
	{
		if (data == null)
		{
			return "版图数据为空。";
		}

		// ① 版图恰 37 格 = 18 外圈 + 19 内圈
		if (TileCount != 3 * Radius * (Radius + 1) + 1 || Coords.Count != TileCount)
		{
			return $"版图格数不符：期望 {TileCount}，实际 {Coords.Count}。";
		}

		int outer = 0;
		for (int nodeId = 0; nodeId < Coords.Count; nodeId++)
		{
			if (IsOuterRing(nodeId))
			{
				outer++;
			}
		}

		if (outer != OuterTileCount || TileCount - outer != InnerTileCount)
		{
			return $"内外圈格数不符：外圈 {outer}（应 {OuterTileCount}）/ 内圈 {TileCount - outer}（应 {InnerTileCount}）。";
		}

		// ② 七类单位齐全 + 占格不重复 + 设施自身连通
		if (!data.IsComplete)
		{
			return "版图单位不齐：需要 5 个设施 + 入口格 + 离开格这七行。";
		}

		HashSet<int> used = new HashSet<int>();
		foreach (VillagePlot plot in data.Plots)
		{
			if (plot.NodeIds == null || plot.NodeIds.Count == 0)
			{
				return $"{plot.DisplayName} 没有占格。";
			}

			foreach (int nodeId in plot.NodeIds)
			{
				if (!TryCoordOf(nodeId, out _))
				{
					return $"{plot.DisplayName} 的占格 NodeId 越界：{nodeId}。";
				}

				if (!used.Add(nodeId))
				{
					return $"{plot.DisplayName} 的占格与别的单位重叠：NodeId {nodeId}。";
				}
			}
		}

		// ③ 设施占格全在外圈
		foreach (VillagePlot plot in data.Plots)
		{
			if (!plot.IsFacility)
			{
				continue;
			}

			foreach (int nodeId in plot.NodeIds)
			{
				if (!IsOuterRing(nodeId))
				{
					return $"{plot.DisplayName} 的占格不在外圈：NodeId {nodeId}。";
				}
			}

			HashSet<int> reached = ReachableFrom(plot.NodeIds[0], new HashSet<int>(plot.NodeIds));
			if (reached.Count != plot.NodeIds.Count)
			{
				return $"{plot.DisplayName} 的占格自身不连通（{reached.Count} / {plot.NodeIds.Count}）。";
			}
		}

		// ④ 入口格：5 个设施各 1 个、在内圈、与本设施占格相邻、不重复、不落在任何占格里
		HashSet<int> entrances = new HashSet<int>();
		foreach (VillagePlot plot in data.Plots)
		{
			if (!plot.IsFacility)
			{
				continue;
			}

			int entrance = plot.EntranceNodeId;
			if (entrance < 0)
			{
				return $"{plot.DisplayName} 没有入口格。";
			}

			if (!IsInnerRing(entrance))
			{
				return $"{plot.DisplayName} 的入口格不在内圈：NodeId {entrance}。";
			}

			if (used.Contains(entrance))
			{
				return $"{plot.DisplayName} 的入口格落在占格里：NodeId {entrance}。";
			}

			if (!entrances.Add(entrance))
			{
				return $"{plot.DisplayName} 的入口格与别的设施重复：NodeId {entrance}。";
			}

			if (!NeighborsOf(entrance).Exists(nodeId => plot.NodeIds.Contains(nodeId)))
			{
				return $"{plot.DisplayName} 的入口格与本设施占格不相邻：NodeId {entrance}。";
			}
		}

		// ⑤ 内圈 19 格从入口格 BFS 全覆盖（含离开格与 5 个门）
		HashSet<int> passable = PassableTiles();
		if (passable.Count != InnerTileCount)
		{
			return $"可走格数不符：实际 {passable.Count}，应 {InnerTileCount}。";
		}

		int start = data.Find(VillagePlotKind.Entrance).NodeIds[0];
		HashSet<int> reachedAll = ReachableFrom(start, passable);
		if (reachedAll.Count != passable.Count)
		{
			return $"内圈从入口格不可全覆盖：可达 {reachedAll.Count} / {passable.Count}。";
		}

		// ⑥ 入口格 ≠ 离开格
		int exit = data.Find(VillagePlotKind.Exit).NodeIds[0];
		if (start == exit)
		{
			return "入口格与离开格是同一格。";
		}

		return string.Empty;
	}

	/// <summary>把一组 NodeId 转成升序的 `;` 串（日志 / 烟测断言用，与配表写法同一格式）。</summary>
	public static string DescribeNodeIds(IEnumerable<int> nodeIds)
	{
		List<int> sorted = new List<int>(nodeIds ?? Array.Empty<int>());
		sorted.Sort();
		return string.Join(";", sorted.ConvertAll(x => x.ToString(CultureInfo.InvariantCulture)));
	}
}


public sealed class VillageLayoutData
{
	public List<VillagePlot> Plots { get; } = new List<VillagePlot>();

	public VillagePlot Find(VillagePlotKind kind)
	{
		foreach (VillagePlot plot in Plots)
		{
			if (plot.Kind == kind)
			{
				return plot;
			}
		}

		return null;
	}

	/// <summary>是否 5 个设施 + 入口格 + 离开格齐全。</summary>
	public bool IsComplete
	{
		get
		{
			for (int i = 0; i <= (int)VillagePlotKind.Exit; i++)
			{
				if (Find((VillagePlotKind)i) == null)
				{
					return false;
				}
			}

			return true;
		}
	}
}
