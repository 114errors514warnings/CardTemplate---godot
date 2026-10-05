// VillageLayoutTests.cs
// 村庄版图的六条不变式 + 配表一致性（村庄地图交互案 §2.2 / §2.3 / §十一 第 1–2 条）。
// 这些断言就是「配表改一行就红灯」的那道闸：表头 / 格数 / 占格 / 入口格 / 内外圈 / 连通性。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

public class VillageLayoutTests
{
	private static string[] ReadTable(string relativePath)
	{
		string path = Path.Combine(AppContext.BaseDirectory, relativePath);
		Assert.True(File.Exists(path), $"缺表：{path}（检查 Tests.csproj 的 None Include 是否拷贝）");
		return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
	}

	private static VillageLayoutData Load() =>
		VillageLayoutCatalog.ParseLines(ReadTable(Path.Combine("WorldMap", "VillageLayout.csv")));

	private static int[] NodesOf(VillageLayoutData data, VillagePlotKind kind) =>
		data.Find(kind).NodeIds.OrderBy(x => x).ToArray();

	private static readonly VillagePlotKind[] FacilityKinds =
	{
		VillagePlotKind.Inn, VillagePlotKind.Guesthouse, VillagePlotKind.Smithy, VillagePlotKind.Restaurant, VillagePlotKind.Forest,
	};

	// ── ① 定稿表逐格一致 + 六条不变式 ──

	[Fact]
	public void LayoutTable_PassesSixInvariants_AndMatchesSignedOffTable()
	{
		VillageLayoutData data = Load();

		Assert.Equal(string.Empty, VillageLayout.Validate(data));   // 六条全过
		Assert.Equal(37, VillageLayout.TileCount);
		Assert.Equal(37, VillageLayout.AllCoords.Count);
		Assert.Equal(18, VillageLayout.OuterTileCount);
		Assert.Equal(19, VillageLayout.InnerTileCount);

		// 村庄地图交互案 §2.2 定稿：占格（升序）+ 入口格
		Assert.Equal(new[] { 15, 22, 28, 33 }, NodesOf(data, VillagePlotKind.Inn));
		Assert.Equal(23, data.Find(VillagePlotKind.Inn).EntranceNodeId);
		Assert.Equal(new[] { 1, 2, 3 }, NodesOf(data, VillagePlotKind.Guesthouse));
		Assert.Equal(7, data.Find(VillagePlotKind.Guesthouse).EntranceNodeId);
		Assert.Equal(new[] { 0, 4, 9 }, NodesOf(data, VillagePlotKind.Smithy));
		Assert.Equal(10, data.Find(VillagePlotKind.Smithy).EntranceNodeId);
		Assert.Equal(new[] { 8, 14, 21 }, NodesOf(data, VillagePlotKind.Restaurant));
		Assert.Equal(13, data.Find(VillagePlotKind.Restaurant).EntranceNodeId);
		Assert.Equal(new[] { 27, 32, 34, 35, 36 }, NodesOf(data, VillagePlotKind.Forest));
		Assert.Equal(31, data.Find(VillagePlotKind.Forest).EntranceNodeId);
		Assert.Equal(new[] { 5 }, NodesOf(data, VillagePlotKind.Entrance));
		Assert.Equal(new[] { 30 }, NodesOf(data, VillagePlotKind.Exit));

		// 设施 18 格恰好铺满外圈
		List<int> facilityTiles = new List<int>();
		foreach (VillagePlotKind kind in FacilityKinds)
		{
			facilityTiles.AddRange(NodesOf(data, kind));
		}

		Assert.Equal(18, facilityTiles.Count);
		Assert.All(facilityTiles, nodeId => Assert.True(VillageLayout.IsOuterRing(nodeId), $"设施格 {nodeId} 应在最外圈"));
		Assert.Equal(Enumerable.Range(0, 37).Where(VillageLayout.IsOuterRing).ToArray(), facilityTiles.OrderBy(x => x).ToArray());

		// 内圈 19 格 = 5 个门 + 入口格 + 离开格 + 12 空地
		HashSet<int> occupied = new HashSet<int>(facilityTiles);
		foreach (VillagePlotKind kind in FacilityKinds)
		{
			occupied.Add(data.Find(kind).EntranceNodeId);
		}

		occupied.Add(data.Find(VillagePlotKind.Entrance).NodeIds[0]);
		occupied.Add(data.Find(VillagePlotKind.Exit).NodeIds[0]);

		int[] emptyTiles = Enumerable.Range(0, 37).Where(id => VillageLayout.IsInnerRing(id) && !occupied.Contains(id)).ToArray();
		Assert.Equal(new[] { 6, 11, 12, 16, 17, 18, 19, 20, 24, 25, 26, 29 }, emptyTiles);   // 12 空地（§2.2 定稿）

		Assert.Equal(19, VillageLayout.PassableTiles().Count);
		Assert.Equal(18, VillageLayout.BlockedTiles(data).Count);
	}

	// ── ② NodeId 口径 = MapGeometry 的生成序（配表直接写 NodeId 的前提）──

	[Fact]
	public void NodeIdOrder_MatchesMapGeometryGenerationOrder_AndNeighborsAgree()
	{
		HexBoardData board = MapGeometry.Generate(VillageLayout.Radius, 20261005);
		Assert.Equal(VillageLayout.TileCount, board.Nodes.Count);

		foreach (MapBoardNode node in board.Nodes)
		{
			Assert.Equal(node.NodeId, VillageLayout.NodeIdOf(node.Position.Q, node.Position.R));
			Assert.True(VillageLayout.TryCoordOf(node.NodeId, out AxialHex hex));
			Assert.Equal(node.Position.Q, hex.Q);
			Assert.Equal(node.Position.R, hex.R);
			Assert.Equal(
				node.NextIds.OrderBy(x => x).ToArray(),
				VillageLayout.NeighborsOf(node.NodeId).OrderBy(x => x).ToArray());
		}

		// 反查：越界坐标 / NodeId 都得被拦下
		Assert.Equal(-1, VillageLayout.NodeIdOf(4, 0));
		Assert.Equal(-1, VillageLayout.NodeIdOf(0, 4));
		Assert.False(VillageLayout.TryCoordOf(-1, out _));
		Assert.False(VillageLayout.TryCoordOf(37, out _));
	}

	// ── ③ 六条不变式真的会拦下坏表（每条各破坏一次）──

	[Fact]
	public void Invariants_RejectBrokenTables()
	{
		VillageLayoutData overlap = Load();
		overlap.Find(VillagePlotKind.Forest).NodeIds.Add(15);                       // ② 与旅馆占格重叠
		Assert.NotEqual(string.Empty, VillageLayout.Validate(overlap));

		VillageLayoutData disconnected = Load();
		disconnected.Find(VillagePlotKind.Inn).NodeIds = new List<int> { 15, 22, 28, 36 };   // ③ 36 与 28 不相邻 → 不连通
		Assert.NotEqual(string.Empty, VillageLayout.Validate(disconnected));

		VillageLayoutData innerBody = Load();
		innerBody.Find(VillagePlotKind.Smithy).NodeIds = new List<int> { 0, 4, 18 };          // ③ 18 是内圈格
		Assert.NotEqual(string.Empty, VillageLayout.Validate(innerBody));

		VillageLayoutData entranceOnBody = Load();
		entranceOnBody.Find(VillagePlotKind.Restaurant).EntranceNodeId = 21;                  // ④ 门落进本设施占格
		Assert.NotEqual(string.Empty, VillageLayout.Validate(entranceOnBody));

		VillageLayoutData farEntrance = Load();
		farEntrance.Find(VillagePlotKind.Inn).EntranceNodeId = 12;                            // ④ 门与本设施不相邻
		Assert.NotEqual(string.Empty, VillageLayout.Validate(farEntrance));

		VillageLayoutData duplicatedEntrance = Load();
		duplicatedEntrance.Find(VillagePlotKind.Forest).EntranceNodeId = 23;                  // ④ 门与旅馆的门重复
		Assert.NotEqual(string.Empty, VillageLayout.Validate(duplicatedEntrance));

		VillageLayoutData sameExit = Load();
		sameExit.Find(VillagePlotKind.Exit).NodeIds = new List<int> { 5 };                    // ⑥ 入口格 = 离开格
		Assert.NotEqual(string.Empty, VillageLayout.Validate(sameExit));

		VillageLayoutData outOfRange = Load();
		outOfRange.Find(VillagePlotKind.Inn).NodeIds = new List<int> { 15, 22, 28, 99 };      // ① 越界 NodeId
		Assert.NotEqual(string.Empty, VillageLayout.Validate(outOfRange));
	}

	// ── ④ 解析器不静默降级 ──

	[Fact]
	public void Catalog_RejectsMalformedRows()
	{
		Assert.Throws<FormatException>(() => VillageLayoutCatalog.ParseLines(new[]
		{
			"FacilityId,DisplayName,NodeIds",
			"Inn,旅馆,15;22;28;33",
		}));

		Assert.Throws<FormatException>(() => VillageLayoutCatalog.ParseLines(new[]
		{
			"FacilityId,DisplayName,NodeIds,EntranceNodeId",
			"Inn,旅馆,33;22;28;15,23",     // 非升序
		}));

		Assert.Throws<FormatException>(() => VillageLayoutCatalog.ParseLines(new[]
		{
			"FacilityId,DisplayName,NodeIds,EntranceNodeId",
			"Inn,旅馆,15;22;28;33,23",
			"Inn,旅馆甲,0;4;9,10",          // 同类目重复
		}));

		Assert.Throws<FormatException>(() => VillageLayoutCatalog.ParseLines(new[]
		{
			"FacilityId,DisplayName,NodeIds,EntranceNodeId",
			"Inn,旅馆,15;22;28;33,",        // 设施缺入口格
		}));

		Assert.Throws<FormatException>(() => VillageLayoutCatalog.ParseLines(new[]
		{
			"FacilityId,DisplayName,NodeIds,EntranceNodeId",
			"Inn,旅馆,15;x;28;33,23",       // NodeId 非整数
		}));

		Assert.Throws<FormatException>(() => VillageLayoutCatalog.ParseLines(new[]
		{
			"FacilityId,DisplayName,NodeIds,EntranceNodeId",
			"Entrance,入口格,5,23",          // 入口格行不得带入口格列
		}));
	}

	// ── ⑤ 昼夜阈值边界（村庄案 §八）──

	[Fact]
	public void NightThreshold_Boundaries()
	{
		Assert.True(VillageLayout.IsNight(1.0f));
		Assert.True(VillageLayout.IsNight(0.4f));
		Assert.False(VillageLayout.IsNight(1.1f));
		Assert.False(VillageLayout.IsNight(4.0f));
		Assert.Equal(1.0f, VillageLayout.NightRemainingThreshold);
	}
}
