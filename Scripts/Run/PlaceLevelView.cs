// PlaceLevelView.cs
// **统一的「非战斗地点关卡」视图**（2026-10-07）：村庄 / 商人 / 将来的任何地点关共用这一份实现。
// 口径出处：README/施工文档/2026/2026.10/10月施工文档.md §33（地点一律走关卡统一流程，不得有专用场景）、
//   §33.2.1（地点内一切可交互单位 = 同一个 `ObjectType = InteractPoint`，靠 `DefinitionId` 区分）、
//   村庄地图交互案 §二–§六（R=3 / 37 格 / 设施占格 + 门口格 / 局内移动不消耗时间点 / 确认 tips）、
//   商人交互案 §二（空白底图 + 商人 3 格 + 入口 / 离开格 + 走到相邻格自动开界面）。
// 它不是「村庄场景 / 商人场景」：版图来自 `BattleMap/MapIndex.csv` + `Maps/<MapId>.json`，
//   可交互单位来自关卡 CSV（`Level/Config/<LevelId>.csv`）的 `InteractPoint` 行，
//   由统一关卡宿主 `RunBattleScene` 按 `LevelType`（`PlaceLevelTypes.IsPlace`）挂到本视图上。
// 分工：本文件只做**走格 / 触发 / 显示**；设施规则（旅馆过夜、搜寻、锻造、餐厅）在各规则层与 `RunSession`，
//   界面（商人 / 锻铁铺 / 餐厅 / 确认 tips）各归自己；「离开格 → 关卡完成」只发 `Finished` 事件，落档由宿主做。
using Godot;
using System;
using System.Collections.Generic;
using CardSimulator.Battlefield;

public partial class PlaceLevelView : Control
{
	/// <summary>踏入离开格：本关卡完成（宿主据此标记节点已访问并回世界地图）。</summary>
	public event Action Finished;

	/// <summary>走一格的时间（纯表现；局内移动不消耗时间点，村庄案 §四）。</summary>
	public const float StepSeconds = 0.12f;

	public const string TitlePrefix = "地点";
	public const string PartyText = "队伍";

	/// <summary>相邻触发的间隔去抖：同一次踏格只触发一次。</summary>
	private static readonly AxialHex[] NeighborOffsets =
	{
		new AxialHex(1, 0), new AxialHex(-1, 0), new AxialHex(0, 1),
		new AxialHex(0, -1), new AxialHex(1, -1), new AxialHex(-1, 1),
	};

	private static readonly Color EmptyColor = new Color("1b2430");
	private static readonly Color EmptyEdgeColor = new Color(0.35f, 0.42f, 0.5f, 0.7f);
	private static readonly Color DoorColor = new Color("4a6b8a");
	private static readonly Color BlockedColor = new Color("3b4a5a");
	private static readonly Color ExitColor = new Color("7a3f3f");
	private static readonly Color EntranceColor = new Color("3f6b52");
	private static readonly Color PartyColor = new Color("f5d98c");
	private static readonly Color HighlightColor = new Color("8fd0ff");

	private BattleLevelConfig level;
	private BattleMapDefinition map;
	private List<AxialHex> cells = new List<AxialHex>();
	private readonly Dictionary<AxialHex, InteractCellInfo> cellInfo = new Dictionary<AxialHex, InteractCellInfo>();
	private readonly List<PlaceGroup> groups = new List<PlaceGroup>();
	private readonly HashSet<AxialHex> walkable = new HashSet<AxialHex>();
	private readonly Dictionary<string, Label> namePlates = new Dictionary<string, Label>();

	private AxialHex party;
	private readonly List<AxialHex> path = new List<AxialHex>();
	private float stepTimer;
	private float cellRadius = 42f;
	private Vector2 origin;
	private int lastTriggeredNodeId = -1;
	private string floatText = string.Empty;
	private float floatTimer;

	private MerchantUi merchantUi;
	private SmithyUi smithyUi;
	private RestaurantUi restaurantUi;
	private PlaceConfirmTips tips;
	private Control partyMarker;
	private Label floatLabel;
	private string pendingTipsKey = string.Empty;

	private sealed class InteractCellInfo
	{
		public InteractPointDefinition Definition;
		public bool IsDoor;
	}

	private sealed class PlaceGroup
	{
		public InteractPointDefinition Definition;
		public readonly List<AxialHex> Cells = new List<AxialHex>();
		public AxialHex Door;
		public bool HasDoor;
		public string Key => Definition?.DefinitionId ?? string.Empty;
	}

	/// <summary>关卡类型（`Village` / `Merchant`；宿主按它分流时用）。</summary>
	public string LevelType => level?.LevelType ?? string.Empty;

	public string LevelId => level?.LevelId ?? string.Empty;

	/// <summary>队伍当前格的 NodeId（= `MapGeometry` 生成序下标，与配置表的 NodeId 同号）。</summary>
	public int PartyNodeId => NodeIdOf(party);

	public int CellCount => cells.Count;

	/// <summary>是否正在沿路径走（走完才结算触发）。</summary>
	public bool IsMoving => path.Count > 0;

	/// <summary>有没有开着的模态界面（商人 / 锻铁铺 / 餐厅 / 确认 tips）。</summary>
	public bool HasOpenModal =>
		(merchantUi?.IsOpen ?? false) || (smithyUi?.IsOpen ?? false) || (restaurantUi?.IsOpen ?? false) || (tips?.IsOpen ?? false);

	public bool IsMerchantOpen => merchantUi?.IsOpen ?? false;
	public bool IsSmithyOpen => smithyUi?.IsOpen ?? false;
	public bool IsRestaurantOpen => restaurantUi?.IsOpen ?? false;
	public bool IsTipsOpen => tips?.IsOpen ?? false;

	public MerchantUi Merchant => merchantUi;
	public SmithyUi Smithy => smithyUi;
	public RestaurantUi Restaurant => restaurantUi;
	public PlaceConfirmTips Tips => tips;

	/// <summary>交互点分组数（村庄 = 5 设施 + 入口 + 离开；商人 = 商人 + 入口 + 离开）。</summary>
	public int GroupCount => groups.Count;

	public int DoorCount
	{
		get
		{
			int count = 0;
			foreach (PlaceGroup group in groups)
			{
				if (group.HasDoor)
				{
					count++;
				}
			}

			return count;
		}
	}

	/// <summary>按关卡配置与地图定义建好版图（宿主在 `AddChild` 之前调）。</summary>
	public void Configure(BattleLevelConfig levelConfig, BattleMapDefinition mapDefinition)
	{
		level = levelConfig;
		map = mapDefinition;
	}

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsPreset(LayoutPreset.FullRect);
		BuildBoard();
		BuildModals();
		BuildNamePlates();
		RestorePartyPosition();
		UpdateCellRadius();
		GD.Print($"[地点] {PlaceLevelTypes.DisplayName(LevelType)} 关卡 {LevelId}：{cells.Count} 格 / "
			+ $"{groups.Count} 个交互点（含 {DoorCount} 个门口格），队伍落在 NodeId {PartyNodeId}。");
	}

	/// <summary>按关卡 CSV 的 `InteractPoint` 行 + 地图定义建版图：格表、交互点分组、可走格。</summary>
	private void BuildBoard()
	{
		cells = BuildCellList();
		List<InteractPointDefinition> table = InteractPointCatalog.Load();
		foreach (BattleLevelObject obj in level.Objects)
		{
			if (obj == null || !string.Equals(obj.ObjectType, "InteractPoint", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			AxialHex hex = new AxialHex(obj.Q, obj.R);
			if (!cells.Contains(hex))
			{
				throw new InvalidOperationException($"关卡 {LevelId} 的交互点 {obj.InstanceId} 落在版图外（{obj.Q},{obj.R}）。");
			}

			InteractPointDefinition definition = InteractPointCatalog.Find(table, obj.DefinitionId);
			if (definition == null)
			{
				throw new InvalidOperationException(
					$"关卡 {LevelId} 的交互点 {obj.InstanceId} 引用了未定义的定义 ID `{obj.DefinitionId}`（见 {InteractPointCatalog.TablePath}）。");
			}

			bool isDoor = string.Equals(obj.Extra, InteractPointCatalog.DoorExtraValue, StringComparison.OrdinalIgnoreCase);
			cellInfo[hex] = new InteractCellInfo { Definition = definition, IsDoor = isDoor };

			PlaceGroup group = null;
			foreach (PlaceGroup candidate in groups)
			{
				if (string.Equals(candidate.Key, definition.DefinitionId, StringComparison.OrdinalIgnoreCase))
				{
					group = candidate;
					break;
				}
			}

			if (group == null)
			{
				group = new PlaceGroup { Definition = definition };
				groups.Add(group);
			}

			group.Cells.Add(hex);
			if (isDoor)
			{
				group.Door = hex;
				group.HasDoor = true;
			}
		}

		// 可走格 = 版图内、且不是「本体格」（`Extra` 非 Door 的交互点格不可通行）；门口格与空地可走。
		foreach (AxialHex cell in cells)
		{
			if (cellInfo.TryGetValue(cell, out InteractCellInfo info) && !info.IsDoor)
			{
				continue;
			}

			walkable.Add(cell);
		}
	}

	private List<AxialHex> BuildCellList()
	{
		if (map != null && string.Equals(map.Shape, "Explicit", StringComparison.OrdinalIgnoreCase)
			&& map.Cells != null && map.Cells.Count > 0)
		{
			List<AxialHex> explicitCells = new List<AxialHex>();
			foreach (BattleCellData cell in map.Cells)
			{
				explicitCells.Add(new AxialHex(cell.Q, cell.R));
			}

			return explicitCells;
		}

		return MapGeometry.EnumerateCells(map?.Radius ?? 3);
	}

	/// <summary>地点内的界面：商人界面 / 锻铁铺 / 餐厅 / 确认 tips（都是既有组件，不新造）。</summary>
	private void BuildModals()
	{
		merchantUi = new MerchantUi();
		AddChild(merchantUi);

		smithyUi = new SmithyUi();
		AddChild(smithyUi);

		restaurantUi = new RestaurantUi();
		AddChild(restaurantUi);

		tips = new PlaceConfirmTips();
		AddChild(tips);
		tips.Resolved += OnTipsResolved;
	}

	/// <summary>交互点的名称牌（村庄案 §2.2：设施图上有名称牌，不可用时变暗）。</summary>
	private void BuildNamePlates()
	{
		foreach (PlaceGroup group in groups)
		{
			if (group.Definition == null || string.IsNullOrWhiteSpace(group.Definition.Name))
			{
				continue;
			}

			Label label = new Label { Text = group.Definition.Name, MouseFilter = MouseFilterEnum.Ignore };
			label.AddThemeFontSizeOverride("font_size", 16);
			AddChild(label);
			namePlates[group.Key] = label;
		}
	}

	// ── 版图几何与绘制 ────────────────────────────────────────────────

	private void UpdateCellRadius()
	{
		double xMax = 0d;
		double yMax = 0d;
		foreach (AxialHex cell in cells)
		{
			xMax = Math.Max(xMax, Math.Abs(cell.Q + cell.R * 0.5));
			yMax = Math.Max(yMax, Math.Abs(cell.R));
		}

		Vector2 viewport = GetViewportRect().Size;
		cellRadius = (float)MapLayout.FitRadius(
			Math.Max(320, (int)viewport.X), Math.Max(240, (int)viewport.Y), 120d, xMax, yMax);
		origin = new Vector2(viewport.X * 0.5f, viewport.Y * 0.5f + 24f);
		LayoutNamePlates();
		QueueRedraw();
	}

	private Vector2 ToScreen(AxialHex cell)
	{
		(double x, double y) = MapLayout.CenterOf(cell.Q, cell.R, cellRadius);
		return origin + new Vector2((float)x, (float)y);
	}

	private Vector2[] PolygonAt(Vector2 center)
	{
		Vector2[] points = new Vector2[6];
		for (int k = 0; k < 6; k++)
		{
			(double vx, double vy) = MapLayout.VertexLocal(k, cellRadius * 0.94d);
			points[k] = center + new Vector2((float)vx, (float)vy);
		}

		return points;
	}

	/// <summary>格 → NodeId（= 生成序下标，与配置表的 NodeId 同号）。</summary>
	public int NodeIdOf(AxialHex cell) => cells.IndexOf(cell);

	private bool CellAtNodeId(int nodeId, out AxialHex cell)
	{
		if (nodeId >= 0 && nodeId < cells.Count)
		{
			cell = cells[nodeId];
			return true;
		}

		cell = default;
		return false;
	}

	/// <summary>
	/// 该格是不是「离开格」（`Exit` 交互点；踏入即该关卡完成）。
	/// **它只作寻路目的地、不作中转** —— 允许穿过去 = 点远处的格会被半路送出地点（2026-10-07 烟测实测）。
	/// </summary>
	private bool IsExitCell(AxialHex cell) =>
		cellInfo.TryGetValue(cell, out InteractCellInfo info)
		&& string.Equals(info.Definition?.DefinitionId, InteractPointCatalog.ExitId, StringComparison.OrdinalIgnoreCase);

	private List<AxialHex> Neighbors(AxialHex cell)
	{
		List<AxialHex> found = new List<AxialHex>();
		foreach (AxialHex offset in NeighborOffsets)
		{
			AxialHex target = new AxialHex(cell.Q + offset.Q, cell.R + offset.R);
			if (cells.Contains(target))
			{
				found.Add(target);
			}
		}

		return found;
	}

	public override void _Draw()
	{
		foreach (AxialHex cell in cells)
		{
			Vector2 center = ToScreen(cell);
			int nodeId = NodeIdOf(cell);
			foreach (AxialHex neighbor in Neighbors(cell))
			{
				if (NodeIdOf(neighbor) > nodeId)
				{
					DrawLine(center, ToScreen(neighbor), EmptyEdgeColor, 2f);
				}
			}

			Vector2[] polygon = PolygonAt(center);
			DrawColoredPolygon(polygon, FillColorOf(cell));
			Vector2[] outline = new Vector2[7];
			Array.Copy(polygon, outline, 6);
			outline[6] = polygon[0];
			DrawPolyline(outline, EdgeColorOf(cell), 2f);
		}

		// 这一步走得到的格：小圆点提示（点它即走过去）。
		foreach (int nodeId in ReachableNodeIds)
		{
			if (CellAtNodeId(nodeId, out AxialHex cell))
			{
				DrawCircle(ToScreen(cell), Math.Max(3f, cellRadius * 0.12f), HighlightColor);
			}
		}

		Vector2 partyCenter = ToScreen(party);
		DrawCircle(partyCenter, cellRadius * 0.30f, PartyColor);
		DrawCircle(partyCenter, cellRadius * 0.30f, new Color(0f, 0f, 0f, 0.8f), false, 2f);
	}

	private Color FillColorOf(AxialHex cell)
	{
		if (cellInfo.TryGetValue(cell, out InteractCellInfo info))
		{
			if (!info.IsDoor)
			{
				return GroupColor(info.Definition?.DefinitionId);
			}

			string id = info.Definition?.DefinitionId ?? string.Empty;
			if (string.Equals(id, InteractPointCatalog.ExitId, StringComparison.OrdinalIgnoreCase))
			{
				return ExitColor;
			}

			return string.Equals(id, InteractPointCatalog.EntranceId, StringComparison.OrdinalIgnoreCase)
				? EntranceColor
				: DoorColor;
		}

		return cell == party ? EmptyColor.Lerp(PartyColor, 0.22f) : EmptyColor;
	}

	private static Color EdgeColorOf(AxialHex cell) => EmptyEdgeColor;

	/// <summary>设施色（纯表现：按 `DefinitionId` 给一个底色，新增交互点不改这里也不影响功能）。</summary>
	private static Color GroupColor(string definitionId) => definitionId switch
	{
		"Inn" => new Color("6b5a8a"),
		"Guesthouse" => new Color("4d6b8a"),
		"Smithy" => new Color("8a6b4a"),
		"Restaurant" => new Color("8a4a5a"),
		"Forest" => new Color("3f7a4a"),
		"Merchant" => new Color("8a7a3f"),
		_ => BlockedColor,
	};

	// ── 队伍位置与走格（局内移动**不消耗时间点**，村庄案 §四）────────────

	private void RestorePartyPosition()
	{
		party = EntranceCell();
		RunSession session = RunSession.Instance;
		int saved = session?.PlacePlayerNodeId ?? -1;
		// 只在**同一地点关**内沿用落点：格号属于关卡版图，跨关沿用会落到另一个地点版图的同号格
		// （2026-10-07 实测：从村庄出来再进商人，队伍落在商人格 5 而不是入口格 0）。
		bool sameLevel = string.Equals(session?.PlacePlayerLevelId, LevelId, StringComparison.OrdinalIgnoreCase);
		if (sameLevel && saved >= 0 && CellAtNodeId(saved, out AxialHex cell) && walkable.Contains(cell))
		{
			party = cell;
		}

		lastTriggeredNodeId = -1;
		SavePartyPosition();
		EnsurePartyMarker();
	}

	/// <summary>落点 = `Entrance` 交互点的格（没有时退地图定义的第 1 个出生格）。</summary>
	private AxialHex EntranceCell()
	{
		foreach (PlaceGroup group in groups)
		{
			if (string.Equals(group.Key, InteractPointCatalog.EntranceId, StringComparison.OrdinalIgnoreCase)
				&& group.Cells.Count > 0)
			{
				return group.Cells[0];
			}
		}

		if (map?.PlayerSpawnCoords != null && map.PlayerSpawnCoords.Count > 0)
		{
			return new AxialHex(map.PlayerSpawnCoords[0].Q, map.PlayerSpawnCoords[0].R);
		}

		return cells.Count > 0 ? cells[0] : new AxialHex(0, 0);
	}

	private void SavePartyPosition() => RunSession.Instance?.SetPlacePlayerNodeId(LevelId, PartyNodeId);

	/// <summary>这一格点得到的格（BFS 走可走格；按 NodeId 升序）。</summary>
	public List<int> ReachableNodeIds
	{
		get
		{
			List<int> ids = new List<int>();
			foreach (AxialHex cell in Bfs(party)) 
			{
				if (cell != party)
				{
					ids.Add(NodeIdOf(cell));
				}
			}

			ids.Sort();
			return ids;
		}
	}

	/// <summary>从起点可达的可走格（含起点）。</summary>
	private List<AxialHex> Bfs(AxialHex from)
	{
		HashSet<AxialHex> visited = new HashSet<AxialHex> { from };
		Queue<AxialHex> queue = new Queue<AxialHex>();
		queue.Enqueue(from);
		while (queue.Count > 0)
		{
			AxialHex current = queue.Dequeue();
			foreach (AxialHex next in Neighbors(current))
			{
				if (walkable.Contains(next) && visited.Add(next))
				{
					queue.Enqueue(next);
				}
			}
		}

		return new List<AxialHex>(visited);
	}

	/// <summary>**AI 接口用**：等同点一下某格（可达即沿路径一格格走过去；不可达 / 有界面开着返回 false）。</summary>
	public bool TryMoveToNode(int nodeId)
	{
		if (IsMoving || HasOpenModal)
		{
			return false;
		}

		if (!CellAtNodeId(nodeId, out AxialHex target) || !walkable.Contains(target) || target == party)
		{
			return false;
		}

		List<AxialHex> route = FindPath(party, target);
		if (route.Count == 0)
		{
			return false;
		}

		path.Clear();
		path.AddRange(route);
		stepTimer = StepSeconds; // 立刻走第一步，点打时不必等一帧
		return true;
	}

	private List<AxialHex> FindPath(AxialHex from, AxialHex to)
	{
		Dictionary<AxialHex, AxialHex> previous = new Dictionary<AxialHex, AxialHex>();
		HashSet<AxialHex> visited = new HashSet<AxialHex> { from };
		Queue<AxialHex> queue = new Queue<AxialHex>();
		queue.Enqueue(from);
		bool found = false;
		while (queue.Count > 0 && !found)
		{
			AxialHex current = queue.Dequeue();
			// 离开格只作**目的地**：它一旦当前沿，最短路径就会「穿过」离开格 ——
			// 走格是 `_Process` 里逐格触发 `OnEnteredCell`，穿过它等于半路结算离开
			// （2026-10-07 烟测实测：从旅馆门口格点树林门口格，最短路径正是穿过离开格）。
			// 它仍会被发现 / 入队（所以仍能作为目的地被找到），只是不再往外扩展。
			if (IsExitCell(current))
			{
				continue;
			}

			foreach (AxialHex next in Neighbors(current))
			{
				if (!walkable.Contains(next) || !visited.Add(next))
				{
					continue;
				}

				previous[next] = current;
				if (next == to)
				{
					found = true;
					break;
				}

				queue.Enqueue(next);
			}
		}

		List<AxialHex> route = new List<AxialHex>();
		if (!found)
		{
			return route;
		}

		AxialHex step = to;
		while (step != from)
		{
			route.Insert(0, step);
			step = previous[step];
		}

		return route;
	}

	public override void _Process(double delta)
	{
		if (path.Count > 0)
		{
			stepTimer += (float)delta;
			if (stepTimer >= StepSeconds)
			{
				stepTimer = 0f;
				AxialHex next = path[0];
				path.RemoveAt(0);
				party = next;
				SyncPartyMarker();
				SavePartyPosition();
				OnEnteredCell(party);
				QueueRedraw();
			}
		}

		if (floatTimer > 0f)
		{
			floatTimer -= (float)delta;
			if (floatTimer <= 0f && floatLabel != null)
			{
				floatLabel.Visible = false;
			}
		}
	}

	public override void _UnhandledInput(InputEvent inputEvent)
	{
		if (IsMoving || HasOpenModal)
		{
			return;
		}

		if (inputEvent is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Left)
		{
			return;
		}

		int nodeId = CellUnder(GetLocalMousePosition());
		if (nodeId >= 0 && TryMoveToNode(nodeId))
		{
			GetViewport()?.SetInputAsHandled();
		}
	}

	/// <summary>鼠标下的格（半径内取最近；没有则 -1）。</summary>
	private int CellUnder(Vector2 position)
	{
		int best = -1;
		float bestDistance = cellRadius * 0.92f;
		for (int i = 0; i < cells.Count; i++)
		{
			float distance = ToScreen(cells[i]).DistanceTo(position);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = i;
			}
		}

		return best;
	}

	// ── 踏入格子后的触发（村子案 §二 / 商人案 §二）────────────────────

	/// <summary>
	/// 每走一格都走这里：**离开格 → 关卡完成**；**门口格 → 触发该交互点**；随后检查「相邻自动触发」（商人）。
	/// </summary>
	private void OnEnteredCell(AxialHex cell)
	{
		if (lastTriggeredNodeId >= 0 && CellAtNodeId(lastTriggeredNodeId, out AxialHex last) && last != cell)
		{
			lastTriggeredNodeId = -1; // 走开即解除抑制：走回来能重新触发（村庄案 §十二 第 3 条）。
		}

		if (cellInfo.TryGetValue(cell, out InteractCellInfo info))
		{
			string id = info.Definition?.DefinitionId ?? string.Empty;
			if (string.Equals(id, InteractPointCatalog.ExitId, StringComparison.OrdinalIgnoreCase))
			{
				FinishLevel();
				return;
			}

			if (info.IsDoor)
			{
				TriggerDoor(info);
			}
		}

		CheckAdjacentTrigger(cell);
		RefreshNamePlateStates();
	}

	private void FinishLevel()
	{
		path.Clear();
		ShowFloat(ExitFloatText);
		GD.Print($"[地点] 踏入离开格（NodeId {PartyNodeId}）：{PlaceLevelTypes.DisplayName(LevelType)} 关卡 {LevelId} 完成。");
		Finished?.Invoke();
	}

	/// <summary>离开格的一句话（浮字 / 日志共用）。</summary>
	public const string ExitFloatText = "离开该地点";

	/// <summary>门口格触发：按定义表的 `Ui` 开对应界面 / tips（`None` = 入口格，不做事）。</summary>
	private void TriggerDoor(InteractCellInfo info)
	{
		int nodeId = PartyNodeId;
		if (lastTriggeredNodeId == nodeId)
		{
			return;
		}

		lastTriggeredNodeId = nodeId;
		string ui = info.Definition?.Ui ?? InteractPointCatalog.UiNone;
		switch (ui)
		{
			case "Inn":
				ShowLodgingTips(true);
				break;
			case "Guesthouse":
				ShowLodgingTips(false);
				break;
			case "Forest":
				ShowForestTips();
				break;
			case "Smithy":
				OpenSmithy();
				break;
			case "Restaurant":
				OpenRestaurant();
				break;
			default:
				break;
		}
	}

	/// <summary>相邻自动触发（商人口径）：踏入与它相邻的格即开界面；已开着则不重复。</summary>
	private void CheckAdjacentTrigger(AxialHex cell)
	{
		foreach (PlaceGroup group in groups)
		{
			if (group.Definition == null || group.Definition.Trigger != InteractTriggerKind.Adjacent)
			{
				continue;
			}

			if (!IsAdjacentToGroup(cell, group) || IsGroupModalOpen(group.Key))
			{
				continue;
			}

			OpenGroupModal(group);
			return;
		}
	}

	private static bool IsAdjacentToGroup(AxialHex cell, PlaceGroup group)
	{
		foreach (AxialHex occupied in group.Cells)
		{
			if (AxialHex.Distance(cell, occupied) == 1)
			{
				return true;
			}
		}

		return false;
	}

	private bool IsGroupModalOpen(string definitionId)
	{
		if (string.Equals(definitionId, InteractPointCatalog.MerchantId, StringComparison.OrdinalIgnoreCase))
		{
			return merchantUi?.IsOpen ?? false;
		}

		return HasOpenModal;
	}

	private void OpenGroupModal(PlaceGroup group)
	{
		if (string.Equals(group.Key, InteractPointCatalog.MerchantId, StringComparison.OrdinalIgnoreCase))
		{
			merchantUi?.Open(null);
			return;
		}
	}

	// ── 设施触发：确认 tips 与专用界面 ────────────────────────────────

	/// <summary>旅馆 / 民宿：跟随队伍的确认 tips（4 行 + 两个按钮，村庄案 §六）。</summary>
	private void ShowLodgingTips(bool inn)
	{
		RunSaveData run = RunSession.Instance?.Current;
		if (run == null || tips == null)
		{
			return;
		}

		string error = inn ? VillageLodging.ValidateInn(run) : VillageLodging.ValidateGuesthouse(run);
		bool enabled = string.IsNullOrEmpty(error);
		EnsurePartyMarker();
		tips.Follow(partyMarker);
		tips.ShowTips(
			inn ? "旅馆" : "民宿",
			VillageLodging.DescribeEffect(run, inn),
			enabled ? VillageLodging.DescribeCost(inn) : error,
			enabled);
		pendingTipsKey = inn ? "Inn" : "Guesthouse";
	}

	/// <summary>树林：确认 tips（进入后自行点「搜寻」之外没有别的入口，搜寻走同一处 `RunSession` 结算）。</summary>
	private void ShowForestTips()
	{
		RunSaveData run = RunSession.Instance?.Current;
		if (run == null || tips == null)
		{
			return;
		}

		bool enabled = VillageForage.CanSearch(run.MapState.RemainingToday);
		string cost = enabled
			? $"代价：{RunTimePoints.Format(VillageForage.TimePointCost)} 时间点（一次搜寻）"
			: VillageForage.SearchTimePointShortText(run.MapState.RemainingToday);
		EnsurePartyMarker();
		tips.Follow(partyMarker);
		tips.ShowTips("树林", $"搜寻：获得 {VillageForage.PicksPerSearch} 件材料（同材料合并）", cost, enabled);
		pendingTipsKey = "Forest";
	}

	private void OnTipsResolved(bool accepted)
	{
		string key = pendingTipsKey;
		pendingTipsKey = string.Empty;
		if (!accepted)
		{
			return;
		}

		switch (key)
		{
			case "Inn":
				ApplyLodging(true);
				break;
			case "Guesthouse":
				ApplyLodging(false);
				break;
			case "Forest":
				ApplyForage();
				break;
			default:
				break;
		}
	}

	/// <summary>过夜结算（旅馆 / 民宿）：成功回复并推进新一天，失败只给一行浮字、不改状态。</summary>
	private void ApplyLodging(bool inn)
	{
		RunSession session = RunSession.Instance;
		if (session == null)
		{
			return;
		}

		List<int> healed;
		string error;
		bool ok = inn
			? session.TryRestAtInn(out healed, out error)
			: session.TryRestAtGuesthouse(out healed, out error);
		if (!ok)
		{
			ShowFloat(error);
			return;
		}

		int total = 0;
		foreach (int amount in healed)
		{
			total += amount;
		}

		ShowFloat($"{(inn ? "旅馆" : "民宿")}过夜：回复 {total} 点生命");
		RefreshNamePlateStates();
	}

	/// <summary>搜寻一次材料（树林）：结果按「同材料合并」列出，背包超载不拒绝（树林案 §四）。</summary>
	private void ApplyForage()
	{
		RunSession session = RunSession.Instance;
		if (session == null)
		{
			return;
		}

		List<(int MaterialId, int Count)> gained;
		string error;
		bool ok = session.TryForageMaterials(VillagePlaceData.ForagePool(), out gained, out error);
		if (!ok)
		{
			ShowFloat(error);
			return;
		}

		List<string> parts = new List<string>();
		foreach ((int materialId, int count) in gained)
		{
			parts.Add($"{VillagePlaceData.MaterialName(materialId)} ×{count}");
		}

		ShowFloat(parts.Count == 0 ? "搜寻：这次什么也没找到" : "搜寻：" + string.Join("、", parts));
		RefreshNamePlateStates();
	}

	/// <summary>锻铁铺（村庄设施）：开既有 `SmithyUi`，注入村庄预设（次数 2 / 金币 ×1）。</summary>
	private void OpenSmithy()
	{
		if (smithyUi == null || !GodotObject.IsInstanceValid(smithyUi))
		{
			return;
		}

		smithyUi.Open(SmithyContext.VillageSmithy, null);
	}

	/// <summary>餐厅（村庄设施）：开既有 `RestaurantUi`（金币刷新回调不需要 —— 界面自己会刷标题行）。</summary>
	private void OpenRestaurant()
	{
		if (restaurantUi == null || !GodotObject.IsInstanceValid(restaurantUi))
		{
			return;
		}

		restaurantUi.Open(null, null);
	}

	// ── 浮字 / 名称牌 / 公共访问面 ───────────────────────────────────

	/// <summary>跟随队伍的 2×2 空节点（确认 tips 靠 `Control` 定位，这里给它一个可跟随的锚）。</summary>
	private void EnsurePartyMarker()
	{
		if (partyMarker != null && GodotObject.IsInstanceValid(partyMarker))
		{
			SyncPartyMarker();
			return;
		}

		partyMarker = new Control { MouseFilter = MouseFilterEnum.Ignore };
		AddChild(partyMarker);
		SyncPartyMarker();
	}

	private void SyncPartyMarker()
	{
		if (partyMarker == null || !GodotObject.IsInstanceValid(partyMarker))
		{
			return;
		}

		partyMarker.Position = ToScreen(party) - new Vector2(1f, 1f);
		partyMarker.Size = new Vector2(2f, 2f);
	}

	/// <summary>一句话浮字（跟着队伍，约 1.6 秒后消失）。</summary>
	public void ShowFloat(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}

		if (floatLabel == null || !GodotObject.IsInstanceValid(floatLabel))
		{
			floatLabel = new Label { MouseFilter = MouseFilterEnum.Ignore };
			floatLabel.AddThemeFontSizeOverride("font_size", 16);
			AddChild(floatLabel);
		}

		floatLabel.Text = text;
		floatLabel.Position = ToScreen(party) + new Vector2(0f, -cellRadius * 1.25f);
		floatLabel.Visible = true;
		floatTimer = 1.6f;
		floatText = text;
	}

	/// <summary>当前浮字文案（空 = 没有浮字）。</summary>
	public string CurrentFloatText => floatTimer > 0f ? floatText : string.Empty;

	/// <summary>名称牌摆到该交互点占格的形心上方。</summary>
	private void LayoutNamePlates()
	{
		foreach (PlaceGroup group in groups)
		{
			if (!namePlates.TryGetValue(group.Key, out Label label) || group.Cells.Count == 0)
			{
				continue;
			}

			Vector2 sum = Vector2.Zero;
			foreach (AxialHex cell in group.Cells)
			{
				sum += ToScreen(cell);
			}

			Vector2 center = sum / group.Cells.Count;
			Vector2 size = label.GetCombinedMinimumSize();
			label.Position = center + new Vector2(-size.X * 0.5f, -cellRadius * 0.66f - size.Y);
		}

		RefreshNamePlateStates();
	}

	/// <summary>名称牌状态（村庄案 §2.2：不可用 → 变暗；原因在 tips 里给）。</summary>
	private void RefreshNamePlateStates()
	{
		foreach (PlaceGroup group in groups)
		{
			if (!namePlates.TryGetValue(group.Key, out Label label))
			{
				continue;
			}

			label.Modulate = IsGroupUsable(group) ? Colors.White : new Color(0.55f, 0.55f, 0.55f, 0.7f);
		}
	}

	private bool IsGroupUsable(PlaceGroup group)
	{
		RunSaveData run = RunSession.Instance?.Current;
		if (run?.MapState == null || group.Definition == null)
		{
			return true;
		}

		return group.Key switch
		{
			"Inn" => string.IsNullOrEmpty(VillageLodging.ValidateInn(run)),
			"Guesthouse" => string.IsNullOrEmpty(VillageLodging.ValidateGuesthouse(run)),
			"Forest" => VillageForage.CanSearch(run.MapState.RemainingToday),
			_ => true,
		};
	}

	// ── 公共访问面（AI 接口点打 / 烟测断言）────────────────────────────

	/// <summary>**AI 接口用**：等同点 tips 的「进入」。</summary>
	public bool AcceptTips() => tips != null && GodotObject.IsInstanceValid(tips) && tips.TriggerEnter();

	/// <summary>**AI 接口用**：等同点 tips 的「稍后」。</summary>
	public bool DeclineTips() => tips != null && GodotObject.IsInstanceValid(tips) && tips.TriggerLater();

	/// <summary>**AI 接口用**：关掉商人界面（等同点「关闭」）。</summary>
	public bool CloseMerchant()
	{
		if (merchantUi?.IsOpen != true)
		{
			return false;
		}

		merchantUi.Close();
		return true;
	}

	/// <summary>**AI 接口用**：把当前开着的界面逐层关掉（锻造炉 / 卡包详细 / 卡牌操作 / 商人 / 餐厅 / 锻铁铺 / tips）。</summary>
	public bool CloseOpenModals()
	{
		if (merchantUi?.IsForgeOpen == true)
		{
			merchantUi.Close();
			return true;
		}

		if (merchantUi?.IsPackDetailOpen == true)
		{
			merchantUi.ClosePackDetail();
			return true;
		}

		if (merchantUi?.IsDeckOpsOpen == true)
		{
			merchantUi.CloseDeckOps();
			return true;
		}

		if (merchantUi?.IsOpen == true)
		{
			merchantUi.Close();
			return true;
		}

		if (smithyUi?.IsOpen == true)
		{
			smithyUi.Close();
			return true;
		}

		if (restaurantUi?.IsOpen == true)
		{
			restaurantUi.Close();
			return true;
		}

		if (tips?.IsOpen == true)
		{
			tips.Close();
			return true;
		}

		return false;
	}

	/// <summary>某交互点的门口格 NodeId（-1 = 该交互点没有门口格，例如商人）。</summary>
	public int DoorNodeId(string definitionId)
	{
		foreach (PlaceGroup group in groups)
		{
			if (string.Equals(group.Key, definitionId, StringComparison.OrdinalIgnoreCase))
			{
				return group.HasDoor ? NodeIdOf(group.Door) : -1;
			}
		}

		return -1;
	}

	/// <summary>某交互点本体格里离队伍最近的一个（商人口径：走到它相邻格即自动开界面；-1 = 没有该交互点）。</summary>
	public int NearestBodyNodeId(string definitionId)
	{
		int best = -1;
		int bestDistance = int.MaxValue;
		foreach (PlaceGroup group in groups)
		{
			if (!string.Equals(group.Key, definitionId, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			foreach (AxialHex cell in group.Cells)
			{
				if (group.HasDoor && cell == group.Door)
				{
					continue;
				}

				int distance = AxialHex.Distance(party, cell);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = NodeIdOf(cell);
				}
			}
		}

		return best;
	}

	/// <summary>与某交互点相邻的可走格 NodeId（商人：走过去就会自动开界面）。</summary>
	public int AdjacentWalkableNodeId(string definitionId)
	{
		foreach (PlaceGroup group in groups)
		{
			if (!string.Equals(group.Key, definitionId, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			foreach (AxialHex cell in cells)
			{
				if (walkable.Contains(cell) && IsAdjacentToGroup(cell, group))
				{
					return NodeIdOf(cell);
				}
			}
		}

		return -1;
	}
}
