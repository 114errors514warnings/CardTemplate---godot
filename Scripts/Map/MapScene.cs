// MapScene.cs
// 地图界面：绘制 127 格大六边形版图、箭头当前位置、放大高亮可达相邻格、
// 格点类型图案与村庄/精英/Boss 特殊底纹；点击按「是否存在遭遇配置」分流进战斗。
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class MapScene : Control
{
	[Export] public bool EmbeddedMode;
	private bool readOnlyMode;
	public event Action<string> LevelRequested;
	public event Action<string> EventRequested;

	/// <summary>
	/// 时间点不足（当天剩余 &lt; 移动消耗，数值见 <see cref="MoveTimePointCost"/>）时触发：宿主应转场到营地休息（地图交互 §五）。
	/// 嵌入模式下由宿主 `RunFlowScene` 接管；独立场景模式直接切 `CampScenePath`。
	/// </summary>
	public event Action RestRequested;

	public void SetReadOnly(bool value)
	{
		readOnlyMode = value;
		// 只读地图仍需截获内容输入；只有 RunFlowScene 的全局按钮层可以在其上接收点击。
		MouseFilter = MouseFilterEnum.Stop;
	}
	/// <summary>只读地图（内容进行中）为 true；可选地图（等待选点）为 false。烟测与外部逻辑据此断言输入模式。</summary>
	public bool IsReadOnly => readOnlyMode;

	/// <summary>
	/// 节点进入前置闸门：宿主（RunFlowScene）可在进入前拦截（待领取态的放弃确认弹窗，§7.1）。
	/// 返回 false = 本次点击不进入、状态不做任何改动；玩家确认后由 <see cref="ReplayPendingNodeEnter"/> 重放。
	/// </summary>
	public Func<int, bool> EnterGate { get; set; }

	/// <summary>被闸门拦下的待进入节点 Id（-1 = 无）。</summary>
	public int PendingNodeEnterId => pendingNodeEnterId;

	private int pendingNodeEnterId = -1;

	/// <summary>重放被闸门拦下的那次节点进入（放弃确认通过后由宿主调用）；没有待进入节点时什么都不做。</summary>
	public void ReplayPendingNodeEnter()
	{
		int nodeId = pendingNodeEnterId;
		if (nodeId < 0)
		{
			return;
		}

		EnterNode(nodeId);
	}
	public const string MainMenuScenePath = "res://Scenes/MainMenu/MainMenuScene.tscn";
	public const string RunBattleScenePath = "res://Scenes/Run/RunBattleScene.tscn";
	public const string RunEventScenePath = "res://Scenes/Run/RunEventScene.tscn";
	public const string CampScenePath = "res://Scenes/Run/CampScene.tscn";

	[Export] public bool EnableDebugControls = true;

	[Export] public float HexSize = 40f;
	[Export] public Color EdgeColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

	private HexBoardData board;
	private readonly Dictionary<int, Vector2> centers = new Dictionary<int, Vector2>();
	private Label statusLabel;
	private Label infoLabel;
	/// <summary>顶部时间点显示（天数 + 当天剩余；嵌入模式下由宿主常驻栏显示，这里为独立场景模式）。</summary>
	private Label timePointLabel;
	private static readonly Color TimePointTextColor = new("f5d98c");
	private int currentNodeId = -1;

	/// <summary>
	/// 本次移动的时间点进程：读全局数据表 `DataBase/GameVariables.csv` 的 `MoveTimePointCost`
	/// （2026-10-05 用户口径 —— 数值放表里便于修改），表里未配置时回落 `RunTimePoints.MoveCost`（默认 0.3）。
	/// `_Ready` 里随 `LoadingSystem.EnsureAllDataLoaded()` 之后的那一次读表**一次填好**（同一份表只解析一次）；
	/// 保留 `NaN` 兜底：`_Ready` 未走到时会自己再读一次表。
	/// </summary>
	private float moveTimePointCost = float.NaN;

	private float MoveTimePointCost
	{
		get
		{
			if (float.IsNaN(moveTimePointCost))
			{
				moveTimePointCost = GameVariables.Load().MoveTimePointCost ?? RunTimePoints.MoveCost;
			}

			return moveTimePointCost;
		}
	}
	private CardSimulator.Battlefield.HexBattleDebugPanel debugPanel;

	private static readonly Dictionary<MapNodeType, Color> NodeColors = new Dictionary<MapNodeType, Color>
	{
		{ MapNodeType.Empty, new Color(0.30f, 0.30f, 0.32f) },
		{ MapNodeType.NormalCombat, new Color(0.62f, 0.34f, 0.32f) },
		{ MapNodeType.HighRiskCombat, new Color(0.55f, 0.18f, 0.20f) },
		{ MapNodeType.NormalEvent, new Color(0.62f, 0.52f, 0.28f) },
		{ MapNodeType.DangerousEvent, new Color(0.70f, 0.34f, 0.15f) },
		{ MapNodeType.Merchant, new Color(0.78f, 0.65f, 0.20f) },
		{ MapNodeType.Village, new Color(0.30f, 0.62f, 0.34f) },
		{ MapNodeType.Elite, new Color(0.55f, 0.30f, 0.68f) },
		{ MapNodeType.Boss, new Color(0.42f, 0.18f, 0.55f) },
		{ MapNodeType.Start, new Color(0.38f, 0.55f, 0.66f) },
	};

	private static readonly Dictionary<MapNodeType, string> NodeGlyphs = new Dictionary<MapNodeType, string>
	{
		{ MapNodeType.Empty, "空" },
		{ MapNodeType.NormalCombat, "战" },
		{ MapNodeType.HighRiskCombat, "危" },
		{ MapNodeType.NormalEvent, "事" },
		{ MapNodeType.DangerousEvent, "险" },
		{ MapNodeType.Merchant, "商" },
		{ MapNodeType.Village, "村" },
		{ MapNodeType.Elite, "精" },
		{ MapNodeType.Boss, "B" },
		{ MapNodeType.Start, "起" },
	};

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		BuildHud();
		RunSession session = RunSession.Instance;
		if (session == null || session.Current == null)
		{
			GD.PrintErr("[地图] 缺少进行中的本局，回到主菜单。");
			GetTree().ChangeSceneToFile(MainMenuScenePath);
			return;
		}

		LoadingSystem.EnsureAllDataLoaded();
		// 地点设施「操作 / 搜寻」的时间点代价（全局表第 7 / 8 列）：随这一次读表灌进纯逻辑层
		// `RunFacilityCosts`（2026-10-06 起由这里负责 —— 原调用点 `VillageScene._Ready` 随村庄专用场景
		// 撤除；地图流程是运行局的常驻入口，进局 / 读档 / 回地图都会经过这里）。
		GameVariables variables = GameVariables.Load();
		variables.ApplyFacilityCosts();
		moveTimePointCost = variables.MoveTimePointCost ?? RunTimePoints.MoveCost;
		RebuildBoard(session);
	}

	private void RebuildBoard(RunSession session)
	{
		RunMapStateSave state = session.Current.MapState;
		board = MapGeometry.Generate(HexBoardData.DefaultRadius, state.Seed);
		centers.Clear();

		// 依据新布局（中心间距 = 3R、flat-top 顶点朝东）按视口自动求合适半径
		double xNormMax = 0;
		double yNormMax = 0;
		foreach (MapBoardNode node in board.Nodes)
		{
			xNormMax = Math.Max(xNormMax, Math.Abs(node.Position.Q + node.Position.R * 0.5));
			yNormMax = Math.Max(yNormMax, Math.Abs(node.Position.R));
		}

		Vector2 viewport = GetViewportRect().Size;
		HexSize = (float)MapLayout.FitRadius((int)viewport.X, (int)viewport.Y, 110d, xNormMax, yNormMax);

		foreach (MapBoardNode node in board.Nodes)
		{
			centers[node.NodeId] = ToScreenPosition(node.Position);
		}

		// 恢复已访问标记
		HashSet<int> visited = new HashSet<int>(state.VisitedNodeIds);
		foreach (MapBoardNode node in board.Nodes)
		{
			node.Visited = visited.Contains(node.NodeId);
		}

		// 首次进入：位置置起点
		if (state.CurrentNodeId < 0)
		{
			state.CurrentNodeId = board.StartNodeId;
			session.Save();
		}

		currentNodeId = state.CurrentNodeId;
		UpdateInfoLabel();
		QueueRedraw();
	}

	private void BuildHud()
	{
		if (EmbeddedMode) return;
		// 顶部信息条
		PanelContainer topPanel = new PanelContainer();
		topPanel.SetAnchorsPreset(LayoutPreset.TopWide);
		topPanel.OffsetTop = 12;
		topPanel.OffsetBottom = 76;
		AddChild(topPanel);

		MarginContainer topMargin = new MarginContainer();
		topMargin.AddThemeConstantOverride("margin_left", 18);
		topMargin.AddThemeConstantOverride("margin_right", 18);
		topPanel.AddChild(topMargin);

		HBoxContainer topRow = new HBoxContainer();
		topRow.AddThemeConstantOverride("separation", 20);
		topMargin.AddChild(topRow);

		statusLabel = new Label { Text = string.Empty };
		statusLabel.AddThemeFontSizeOverride("font_size", 18);
		statusLabel.AddThemeColorOverride("font_color", Colors.White);
		topRow.AddChild(statusLabel);

		// 时间点显示（第 9 条）：天数 + 当天剩余，精度 0.1（`RunTimePoints.FormatDayAndRemaining`）。
		timePointLabel = new Label { Text = string.Empty };
		timePointLabel.AddThemeFontSizeOverride("font_size", 18);
		timePointLabel.AddThemeColorOverride("font_color", TimePointTextColor);
		timePointLabel.HorizontalAlignment = HorizontalAlignment.Right;
		timePointLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		topRow.AddChild(timePointLabel);

		// 底部信息条
		PanelContainer bottomPanel = new PanelContainer();
		bottomPanel.SetAnchorsPreset(LayoutPreset.BottomWide);
		bottomPanel.OffsetTop = -92;
		bottomPanel.OffsetBottom = -12;
		AddChild(bottomPanel);

		MarginContainer bottomMargin = new MarginContainer();
		bottomMargin.AddThemeConstantOverride("margin_left", 18);
		bottomMargin.AddThemeConstantOverride("margin_top", 8);
		bottomMargin.AddThemeConstantOverride("margin_right", 18);
		bottomMargin.AddThemeConstantOverride("margin_bottom", 8);
		bottomPanel.AddChild(bottomMargin);

		HBoxContainer bottomRow = new HBoxContainer();
		bottomRow.AddThemeConstantOverride("separation", 24);
		bottomMargin.AddChild(bottomRow);

		infoLabel = new Label { Text = string.Empty };
		infoLabel.AddThemeFontSizeOverride("font_size", 18);
		infoLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bottomRow.AddChild(infoLabel);

		Button backButton = new Button { Text = "返回主菜单" };
		backButton.Pressed += () => GetTree().ChangeSceneToFile(MainMenuScenePath);
		bottomRow.AddChild(backButton);
		if (EnableDebugControls) BuildDebugControls(bottomRow);
	}

	private void BuildDebugControls(Control parent)
	{
		var button = new Button { Text = "调试" }; button.Pressed += ToggleDebugPanel; parent.AddChild(button);
	}
	public void ToggleDebugPanel()
	{
		if (EmbeddedMode)
		{
			DebugRequested?.Invoke();
			return;
		}
		if (debugPanel == null)
		{
			debugPanel = CreateDebugPanel();
			AddChild(debugPanel); debugPanel.Visible = false;
		}
		debugPanel.ZAsRelative = false; debugPanel.ZIndex = 1000; debugPanel.MouseFilter = MouseFilterEnum.Stop; debugPanel.ToggleVisible();
	}

	public event Action DebugRequested;
	public CardSimulator.Battlefield.HexBattleDebugPanel CreateDebugPanel()
	{
		var packed = GD.Load<PackedScene>("res://Scenes/UI/HexBattleDebugPanel.tscn");
		var panel = packed.Instantiate<CardSimulator.Battlefield.HexBattleDebugPanel>();
		panel.Setup(null, null, SetStatus, id => StartDebugContent("Level", id), id => StartDebugContent("Event", id));
		return panel;
	}
	/// <summary>当前所在格点的节点类型（调试直达事件时用作结算来源类型，§5.7）；取不到返回 `Empty`（展示为「未知」）。</summary>
	private MapNodeType ResolveCurrentNodeType()
	{
		MapBoardNode node = board == null ? null : board.GetNode(currentNodeId);
		return node == null ? MapNodeType.Empty : node.Type;
	}
	private void StartDebugContent(string type, string id)
	{
		var session = RunSession.Instance; if (session?.Current == null || string.IsNullOrWhiteSpace(id)) return;
		if (type == "Event")
		{
			try { StoryEventCatalog.Load(id); } catch (System.Exception ex) { SetStatus(ex.Message); return; }
			session.BeginRunEvent(id, currentNodeId, ResolveCurrentNodeType());
			if (EmbeddedMode && EventRequested != null) { EventRequested.Invoke(id); return; }
			GetTree().ChangeSceneToFile(RunEventScenePath); return;
		}
		try
		{
			var level = CardSimulator.Battlefield.BattleLevelCatalog.Load(id);
			var row = new StageEncounterRow { LevelId = id, NodeType = MapNodeType.NormalCombat, DropTableId = level.DropTableId, MonsterIds = level.Objects.Where(x => x.ObjectType == "Monster").Select(x => int.Parse(x.DefinitionId)).ToArray() };
			session.BeginRunBattleEncounter("", row);
			if (EmbeddedMode && LevelRequested != null) { LevelRequested.Invoke(id); return; }
			GetTree().ChangeSceneToFile(RunBattleScenePath);
		}
		catch (System.Exception ex) { SetStatus(ex.Message); }
	}

	private Vector2 ToScreenPosition(AxialHex hex)
	{
		// 新布局：中心间距 = 3R；flat-top 顶点沿 0°/60°/…（东顶点朝右）
		(double x, double y) = MapLayout.CenterOf(hex.Q, hex.R, HexSize);
		Vector2 viewport = GetViewportRect().Size;
		return new Vector2(viewport.X * 0.5f + (float)x, viewport.Y * 0.5f + (float)y);
	}

	private void UpdateInfoLabel()
	{
		if (timePointLabel != null)
		{
			timePointLabel.Text = RunTimePoints.FormatDayAndRemaining(RunSession.Instance?.Current?.MapState.TimePoints ?? 0f);
		}

		if (infoLabel == null) return;
		RunSession session = RunSession.Instance;
		if (session == null || session.Current == null || board == null)
		{
			return;
		}

		MapBoardNode node = board.GetNode(currentNodeId);
		string nodeText = node == null ? "-" : $"{node.NodeId}（{NodeGlyphs[node.Type]}）";
		infoLabel.Text =
			$"当前格: {nodeText}　HP: {session.Current.CharacterSlots[0].CurrentHp}" +
			$"　金币: {session.Current.Gold}　钥匙: {session.Current.Keys}" +
			$"　普通敌袭已打: {session.Current.MapState.CurrentNormalEncounterCount}";
	}

	// ── 绘制 ──────────────────────────────────────────────
	private readonly HashSet<int> currentReachable = new HashSet<int>();

	private HashSet<int> ComputeReachable()
	{
		HashSet<int> reachable = new HashSet<int>();
		if (board == null || currentNodeId < 0)
		{
			return reachable;
		}

		MapBoardNode node = board.GetNode(currentNodeId);
		if (node == null)
		{
			return reachable;
		}

		foreach (int next in node.NextIds)
		{
			// 已访问格可再次经过，不做过滤（该格遭遇只在首次访问时结算）
			reachable.Add(next);
		}

		return reachable;
	}

	public override void _Draw()
	{
		if (board == null)
		{
			return;
		}

		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.015f, 0.025f, 0.04f, 0.82f));
		float minX = centers.Values.Min(x => x.X) - HexSize * 1.7f;
		float maxX = centers.Values.Max(x => x.X) + HexSize * 1.7f;
		float minY = centers.Values.Min(x => x.Y) - HexSize * 1.7f;
		float maxY = centers.Values.Max(x => x.Y) + HexSize * 1.7f;
		Rect2 mapPanel = new Rect2(minX, minY, maxX - minX, maxY - minY).Intersection(new Rect2(Vector2.Zero, Size));
		DrawRect(mapPanel, new Color(0.075f, 0.11f, 0.15f, 0.96f));
		DrawRect(mapPanel, new Color(0.30f, 0.40f, 0.48f, 0.85f), false, 2f);

		currentReachable.Clear();
		currentReachable.UnionWith(ComputeReachable());

		Font font = ThemeDB.FallbackFont;
		int glyphSize = Mathf.Max(14, (int)(HexSize * 0.5f));

		// 边线：连接线段填在相邻两格相对顶点之间，方向与中心连线平行，长度 = 边长 R
		HashSet<(int Lo, int Hi)> undirectedEdges = new HashSet<(int, int)>();
		foreach (MapBoardNode node in board.Nodes)
		{
			foreach (int nextId in node.NextIds)
			{
				int lo = node.NodeId < nextId ? node.NodeId : nextId;
				int hi = node.NodeId < nextId ? nextId : node.NodeId;
				undirectedEdges.Add((lo, hi));
			}
		}

		foreach ((int lo, int hi) in undirectedEdges)
		{
			if (!centers.TryGetValue(lo, out Vector2 centerA) || !centers.TryGetValue(hi, out Vector2 centerB))
			{
				continue;
			}

			// 端点 = 格心 + R·e / 格心 − R·e（e 为两格心连线单位方向）。
			// 必须用与格心同一坐标系（已含视口居中偏移）计算，否则线段与六边形错位。
			Vector2 delta = centerB - centerA;
			float dist = delta.Length();
			if (dist < 0.0001f)
			{
				continue;
			}

			Vector2 direction = delta / dist;
			DrawLine(centerA + direction * HexSize, centerB - direction * HexSize, EdgeColor, 2f, true);
		}

		// 格点
		foreach (MapBoardNode node in board.Nodes)
		{
			Vector2 center = centers[node.NodeId];
			bool isCurrent = node.NodeId == currentNodeId;
			bool isReachable = currentReachable.Contains(node.NodeId);
			bool isSpecialBg = node.Type == MapNodeType.Village
				|| node.Type == MapNodeType.Elite
				|| node.Type == MapNodeType.Boss;

			Color baseColor = NodeColors.TryGetValue(node.Type, out Color c) ? c : Colors.Gray;
			if (node.Visited)
			{
				baseColor = baseColor.Darkened(0.55f);
			}

			// 特殊节点底纹（外扩描边）
			if (isSpecialBg)
			{
				DrawPolygonHex(center, HexSize * 1.16f, baseColor.Lightened(0.25f), null, 2f);
			}

			float radius = HexSize;
			if (isReachable)
			{
				// 可达相邻格放大高亮
				radius = HexSize * 1.18f;
				DrawPolygonHex(center, radius, baseColor, Colors.Yellow, 5f);
			}
			else if (isCurrent)
			{
				DrawPolygonHex(center, radius, baseColor, Colors.White, 4f);
			}
			else
			{
				DrawPolygonHex(center, radius, baseColor, null, 1.5f);
			}

			// 探索状态与玩家当前位置相互独立：已经探索的节点即使当前仍停留其上也显示对勾。
			bool showCheckMark = node.Visited;
			Vector2 sz = font.GetStringSize(NodeGlyphs[node.Type], HorizontalAlignment.Left, -1, glyphSize);
			Vector2 textPos = center - new Vector2(sz.X * 0.5f, sz.Y * 0.5f);
			if (showCheckMark)
			{
				// 已探索：文字上移一点，在节点中间画对勾；当前位置另由白框和箭头表示。
				textPos -= new Vector2(0, HexSize * 0.30f);
			}

			DrawString(font, textPos, NodeGlyphs[node.Type], HorizontalAlignment.Left, -1, glyphSize, Colors.White);

			if (showCheckMark)
			{
				DrawCheckMark(center, HexSize);
			}
		}

		// 当前位置箭头
		if (centers.TryGetValue(currentNodeId, out Vector2 arrowCenter))
		{
			DrawArrowAbove(arrowCenter);
		}
	}

	private void DrawPolygonHex(Vector2 center, float radius, Color fill, Color? border, float borderWidth)
	{
		Vector2[] points = BuildHexPoints(center, radius);
		DrawColoredPolygon(points, fill);
		if (border.HasValue && borderWidth > 0f)
		{
			for (int i = 0; i < points.Length; i++)
			{
				Vector2 a = points[i];
				Vector2 b = points[(i + 1) % points.Length];
				DrawLine(a, b, border.Value, borderWidth, true);
			}
		}
	}

	private static Vector2[] BuildHexPoints(Vector2 center, float radius)
	{
		Vector2[] points = new Vector2[6];
		for (int i = 0; i < 6; i++)
		{
			// flat-top：顶点 0° 起算（东向顶点），60° 步进
			double angleRad = Math.PI / 180.0 * (60 * i);
			points[i] = center + new Vector2((float)Math.Cos(angleRad), (float)Math.Sin(angleRad)) * radius;
		}

		return points;
	}

	private void DrawCheckMark(Vector2 center, float radius)
	{
		float s = radius;
		Vector2 a = center + new Vector2(-0.30f * s, 0.02f * s);
		Vector2 b = center + new Vector2(-0.06f * s, 0.24f * s);
		Vector2 c = center + new Vector2(0.34f * s, -0.24f * s);
		Color checkColor = new Color(0.35f, 0.95f, 0.35f, 1f);
		float width = Mathf.Max(3f, radius * 0.16f);
		DrawLine(a, b, checkColor, width, true);
		DrawLine(b, c, checkColor, width, true);
	}

	private void DrawArrowAbove(Vector2 center)
	{
		Vector2 tip = center - new Vector2(0, HexSize * 1.05f);
		Vector2 left = tip + new Vector2(-HexSize * 0.35f, -HexSize * 0.55f);
		Vector2 right = tip + new Vector2(HexSize * 0.35f, -HexSize * 0.55f);
		DrawColoredPolygon(new[] { tip, left, right }, Colors.Gold);
		DrawRect(new Rect2(tip.X - HexSize * 0.12f, tip.Y - HexSize * 0.55f, HexSize * 0.24f, HexSize * 0.7f), Colors.Gold);
	}

	// ── 交互 ──────────────────────────────────────────────
	public override void _GuiInput(InputEvent inputEvent)
	{
		if (!IsInsideTree())
		{
			return;
		}

		if (inputEvent is InputEventMouseButton button
			&& button.ButtonIndex == MouseButton.Left
			&& button.Pressed)
		{
			int hit = FindNodeAt(button.Position);
			if (hit < 0)
			{
				return;
			}

			OnNodeClicked(hit);

			Viewport viewport = GetViewport();
			if (viewport != null)
			{
				viewport.SetInputAsHandled();
			}
		}
	}

	private int FindNodeAt(Vector2 localPosition)
	{
		if (board == null)
		{
			return -1;
		}

		// 优先在可达格中查找，其次全图
		foreach (int candidate in currentReachable)
		{
			if (centers.TryGetValue(candidate, out Vector2 center)
				&& center.DistanceTo(localPosition) <= HexSize * 0.86f)
			{
				return candidate;
			}
		}

		return -1;
	}

	private void OnNodeClicked(int nodeId)
	{
		if (readOnlyMode) { SetStatus("当前内容进行中，地图仅可查看。"); return; }
		RunSession session = RunSession.Instance;
		if (session == null || session.Current == null || board == null)
		{
			return;
		}

		MapBoardNode node = board.GetNode(nodeId);
		if (node == null)
		{
			return;
		}

		// Boss 需要钥匙门槛（草案 2）
		if (node.Type == MapNodeType.Boss && session.Current.Keys < 2)
		{
			SetStatus("Boss 格需要至少 2 把钥匙才能进入（当前钥匙不足）。");
			return;
		}

		// 前置闸门（§7.1）：宿主可在进入前拦截（待领取态的放弃确认弹窗）；
		// 被拦截时**不得改动任何状态**，节点 Id 留下来等玩家确认后由 ReplayPendingNodeEnter() 重放。
		pendingNodeEnterId = nodeId;
		if (EnterGate != null && !EnterGate(nodeId))
		{
			return;
		}

		EnterNode(nodeId);
	}

	/// <summary>
	/// 真正进入节点（唯一的状态改动入口）：移动当前位置 → 已访问格幂等处理 → 按内容分流进战斗 / 事件 / 停留。
	/// 点击（含闸门）与「放弃确认后重放」都走这里，保证两条路径行为完全一致。
	/// </summary>
	private void EnterNode(int nodeId)
	{
		RunSession session = RunSession.Instance;
		MapBoardNode node = board == null ? null : board.GetNode(nodeId);
		if (readOnlyMode || session?.Current == null || node == null)
		{
			return;
		}

		pendingNodeEnterId = -1;

		// 0) 时间点闸门（地图交互 §五）：当天已耗尽（`PendingRestDay`，进程在战斗 / 移动中跨过日界）必须先休息；
		//    当天剩余不足以支付这次移动时同样禁止前往。两种情形都**不改动任何状态**（位置 / 访问标记 / 时间点）。
		if (session.Current.MapState.PendingRestDay)
		{
			SetStatus("当天时间点已耗尽，必须先进营地休息。");
			RequestRest();
			return;
		}

		if (!session.Current.MapState.CanSpendTimePoints(MoveTimePointCost))
		{
			SetStatus($"时间点不足（当天剩余 {RunTimePoints.Format(session.Current.MapState.RemainingToday)}，"
				+ $"移动需要 {RunTimePoints.Format(MoveTimePointCost)}），转入营地休息。");
			RequestRest();
			return;
		}

		// 1) 支付本次移动的时间点进程（表值 MoveTimePointCost，默认 0.3）；整笔成功才移动，避免"位置已动、时间点没花"。
		if (!session.TrySpendTimePoints(MoveTimePointCost, out string timePointError))
		{
			SetStatus($"时间点不足，转入营地休息：{timePointError}");
			RequestRest();
			return;
		}

		// 2) 移动当前位置（位置与时间点一起落档：支付已落过一次，这里补位置）
		session.SetCurrentNode(nodeId);
		currentNodeId = nodeId;
		session.Save();
		UpdateInfoLabel();

		// 已结算（访问过）的格：允许再次经过，但不重复触发该格遭遇/奖励
		if (node.Visited)
		{
			session.MarkCurrentNodeVisitedAndAdvanceEncounter(); // 幂等：记录位置并落盘，不重复计数
			SetStatus("已访问格：可再次经过，不重复触发。");
			UpdateInfoLabel();
			QueueRedraw();
			return;
		}

		// 2) 首次到达：按「该类型此时能否解析出配置行」分流（与格点类型无关）
		ResolvedMapContent content = WorldMapContentResolver.Resolve(session.Current.MapState.Act, node, board, session.Current);

		// 2.0) 地点场景（村庄 / 商人）：2026-10-06 撤除专用分流（方案甲）—— `ContentType = Village / Merchant`
		//      的行不再请求专用场景，落到下面「无配置」分支（标记完成 + 停留地图）。
		//      「统一关卡通道」批落地后，这里会按「是否已到达过」重新分流（已到达 → 进局内地图，非战斗）。

		if (content?.Type == "Level")
		{
			CardSimulator.Battlefield.BattleLevelConfig level;
			try { level = CardSimulator.Battlefield.BattleLevelCatalog.Load(content.Id); }
			catch (System.Exception ex) { SetStatus($"关卡配置加载失败：{ex.Message}"); return; }
			var row = new StageEncounterRow { LevelId = content.Id, NodeType = node.Type, DropTableId = level.DropTableId,
				MonsterIds = level.Objects.Where(x => x.ObjectType == "Monster").Select(x => int.Parse(x.DefinitionId)).ToArray() };
			session.BeginRunBattleEncounter(MapNodeTypeUtil.GetLayerNameByAct(session.Current.MapState.Act), row);
			SetStatus($"进入关卡：{content.Id}。");
			QueueRedraw();
			if (EmbeddedMode) LevelRequested?.Invoke(content.Id); else GetTree().ChangeSceneToFile(RunBattleScenePath);
			return;
		}
		if (content?.Type == "Event")
		{
			try { StoryEventCatalog.Load(content.Id); }
			catch (System.Exception ex) { SetStatus($"事件配置加载失败：{ex.Message}"); return; }
			session.BeginRunEvent(content.Id, node.NodeId, node.Type);
			SetStatus($"进入事件：{content.Id}。"); QueueRedraw(); if (EmbeddedMode) EventRequested?.Invoke(content.Id); else GetTree().ChangeSceneToFile(RunEventScenePath); return;
		}

		// 3) 无配置：标记完成并停留地图
		node.Visited = true;
		session.MarkCurrentNodeVisitedAndAdvanceEncounter();
		SetStatus($"已到达（无配置遭遇），停留地图。");
		UpdateInfoLabel();
		QueueRedraw();
	}

	/// <summary>
	/// 烟测用：按玩家左键点击的同一入口点一个**可达格**（走 `OnNodeClicked` → 前置闸门 → `EnterNode`）。
	/// 返回 false = 当前没有可达格（地图不可选或没有相邻格）。
	/// </summary>
	public bool SimulateClickReachableNode()
	{
		foreach (int candidate in currentReachable)
		{
			OnNodeClicked(candidate);
			return true;
		}

		return false;
	}

	// ── AI 接口访问面（2026-10-02）────────────────────────────────────────
	// 边界：`TryEnterReachableNode` = 玩家口径（点可达格）；`TryForceEnterNode` / `TryJumpToNextCombat`
	// 属**调试通道**（无视可达 / 只读闸门），只有 `DebugApiRun` 会调。

	/// <summary>当前可达格点（玩家这一回合点得到的格）。</summary>
	public IReadOnlyCollection<int> ReachableNodeIds => currentReachable;

	/// <summary>
	/// 从存档的已访问集合刷新版图上的「已访问」标记（幂等，顺手重绘）。
	/// 用途（2026-10-05，2026-10-06 修订）：访问标记不一定由本场景写 —— 地点场景（村庄）曾由
	/// `RunSession.CompletePendingPlaceToMap` 在离开时才落标记，而本场景只在建版图时读一次存档，
	/// 不刷新的话同一会话里再点该格会**重复进入**（一次性节点失效）。
	/// 专用地点场景已撤除，本方法保留给「统一关卡通道」批（关卡结束回地图时同样要刷一次）。
	/// </summary>
	public void RefreshVisitedFlags()
	{
		if (board == null)
		{
			return;
		}

		List<int> visitedIds = RunSession.Instance?.Current?.MapState.VisitedNodeIds;
		HashSet<int> visited = new HashSet<int>(visitedIds ?? new List<int>());
		foreach (MapBoardNode node in board.Nodes)
		{
			node.Visited = visited.Contains(node.NodeId);
		}

		QueueRedraw();
	}

	/// <summary>当前所在格点 Id。</summary>
	public int CurrentNodeId => currentNodeId;

	/// <summary>起点格 Id。</summary>
	public int StartNodeId => board?.StartNodeId ?? -1;

	/// <summary>
	/// 固定地点格（村庄）的 NodeId（-1 = 本图没有该点）；内容按 `FixedNode.csv` 的 `Village` 行解析。
	/// 供 AI 接口 / 烟测直接落到地点关（§33 统一关卡通道：地点关与战斗关同级走 `Level` 通道）。
	/// </summary>
	public int VillageNodeId => board?.VillageNodeId ?? -1;

	/// <summary>商人格的 NodeId（-1 = 本图没有该点）；内容按 `FixedNode.csv` 的 `Merchant` 行解析。</summary>
	public int MerchantNodeId => board?.MerchantNodeId ?? -1;

	/// <summary>按玩家口径进入一个可达格（走 `OnNodeClicked` → 前置闸门 → `EnterNode`）。</summary>
	public bool TryEnterReachableNode(int nodeId)
	{
		if (!currentReachable.Contains(nodeId))
		{
			return false;
		}

		OnNodeClicked(nodeId);
		return true;
	}

	/// <summary>点第一个可达格（= 玩家鼠标点击的同一入口）。</summary>
	public bool TryEnterFirstReachableNode() => SimulateClickReachableNode();

	/// <summary>
	/// **调试通道**：无视可达判定与只读闸门直接进入指定格（位置 / 访问标记 / 时间点照常结算；
	/// 时间点不足或当天耗尽仍会按规则转入营地）。
	/// </summary>
	public bool TryForceEnterNode(int nodeId)
	{
		if (board?.GetNode(nodeId) == null)
		{
			return false;
		}

		bool previousReadOnly = readOnlyMode;
		readOnlyMode = false;
		try { EnterNode(nodeId); }
		// 进入内容时 `RunFlowScene.StartLevel` 会把地图设成只读（内容进行中）—— 那一笔**不能**被还原：
		// 否则调试通道一进内容，地图就停在可点状态，与「玩家自己点格进入」的结果不一致
		// （2026-10-07 地点关烟测实测：进村庄后 `IsReadOnly` 仍是 false）。
		finally { readOnlyMode = previousReadOnly || readOnlyMode; }
		return true;
	}

	/// <summary>
	/// **调试通道**：一键跳到最近的**未访问战斗格**（普通敌袭 / 高危敌袭 / 精英 / Boss）。
	/// 无视可达判定与只读闸门，但**不改**时间点（不足时按规则转营地；要完全绕过先设时间点）。
	/// </summary>
	public bool TryJumpToNextCombat(out int nodeId, out string error)
	{
		nodeId = -1;
		error = string.Empty;
		if (board == null || RunSession.Instance?.Current == null)
		{
			error = "地图未就绪。";
			return false;
		}

		MapBoardNode current = board.GetNode(currentNodeId);
		int bestDistance = int.MaxValue;
		foreach (MapBoardNode node in board.Nodes)
		{
			if (node.Visited || !IsCombatNode(node.Type)) continue;
			int distance = current == null ? 0 : AxialHex.Distance(node.Position, current.Position);
			if (distance >= bestDistance) continue;
			bestDistance = distance;
			nodeId = node.NodeId;
		}

		if (nodeId < 0)
		{
			error = "全图没有未访问的战斗格。";
			return false;
		}

		return TryForceEnterNode(nodeId);
	}

	/// <summary>调试：地图全景（节点 / 类型 / 是否已访问 / 是否可达 / 当前位置）。</summary>
	public object ApiMapState() => new
	{
		act = RunSession.Instance?.Current?.MapState.Act ?? 0,
		board = board == null ? null : new
		{
			radius = board.Radius,
			startNodeId = board.StartNodeId,
			bossNodeId = board.BossNodeId,
			currentNodeId,
		},
		nodes = BuildNodeStates(),
	};

	private List<object> BuildNodeStates()
	{
		var list = new List<object>();
		if (board == null) return list;
		foreach (MapBoardNode node in board.Nodes)
		{
			list.Add(new
			{
				nodeId = node.NodeId,
				q = node.Position.Q,
				r = node.Position.R,
				type = node.Type.ToString(),
				visited = node.Visited,
				reachable = currentReachable.Contains(node.NodeId),
				current = node.NodeId == currentNodeId,
			});
		}

		return list;
	}

	private static bool IsCombatNode(MapNodeType type) =>
		type == MapNodeType.NormalCombat || type == MapNodeType.HighRiskCombat
		|| type == MapNodeType.Elite || type == MapNodeType.Boss;

	/// <summary>转入营地休息（时间点不足 / 主动结束当天）：嵌入模式交给宿主 `RunFlowScene`，独立场景模式直接切营地场景。</summary>
	private void RequestRest()
	{
		if (EmbeddedMode)
		{
			if (RestRequested != null)
			{
				RestRequested.Invoke();
			}

			return;
		}

		GetTree().ChangeSceneToFile(CampScenePath);
	}

	/// <summary>剧情模式新局位于起点时，按 Start 节点事件池启动开始事件。</summary>
	public void TriggerStartEventIfNeeded()
	{
		var session = RunSession.Instance;
		if (!EmbeddedMode || readOnlyMode || session?.Current == null || board == null || session.Current.MapState.VisitedNodeIds.Count > 0) return;
		MapBoardNode start = board.GetNode(board.StartNodeId);
		if (start == null || currentNodeId != start.NodeId) return;
		ResolvedMapContent content = WorldMapContentResolver.Resolve(session.Current.MapState.Act, start, board, session.Current);
		if (content?.Type != "Event") return;
		try { StoryEventCatalog.Load(content.Id); }
		catch (Exception ex) { SetStatus($"开始事件加载失败：{ex.Message}"); return; }
		session.BeginRunEvent(content.Id, start.NodeId, start.Type);
		EventRequested?.Invoke(content.Id);
	}

	private StageEncounterRow TryResolveEncounter(RunSession session, MapBoardNode node)
	{
		string layer = MapNodeTypeUtil.GetLayerNameByAct(session.Current.MapState.Act);
		if (string.IsNullOrEmpty(layer))
		{
			return null;
		}

		StageDifficulty? rule = null;
		if (node.Type == MapNodeType.NormalCombat)
		{
			rule = StageEncounterPicker.ResolveNormalCombatDifficultyByEncounterCount(session.Current.MapState.CurrentNormalEncounterCount);
		}

		return LoadingSystem.TryPickStageEncounter(layer, node.Type, rule, new Random(session.Current.MapState.Seed + node.NodeId));
	}

	private void SetStatus(string text)
	{
		if (statusLabel != null)
		{
			statusLabel.Text = text;
		}
	}
}
