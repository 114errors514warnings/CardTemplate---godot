// MapScene.cs
// 地图界面：绘制 127 格大六边形版图、箭头当前位置、放大高亮可达相邻格、
// 格点类型图案与村庄/精英/Boss 特殊底纹；点击按「是否存在遭遇配置」分流进战斗。
using Godot;
using System;
using System.Collections.Generic;

public partial class MapScene : Control
{
	public const string MainMenuScenePath = "res://Scenes/MainMenu/MainMenuScene.tscn";
	public const string RunBattleScenePath = "res://Scenes/Run/RunBattleScene.tscn";

	[Export] public float HexSize = 40f;
	[Export] public Color EdgeColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

	private HexBoardData board;
	private readonly Dictionary<int, Vector2> centers = new Dictionary<int, Vector2>();
	private Label statusLabel;
	private Label infoLabel;
	private int currentNodeId = -1;

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
			$"　普通敌袭已打: {session.Current.MapState.NormalEncounterIndex}";
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

			bool showCheckMark = node.Visited && node.NodeId != currentNodeId;
			Vector2 sz = font.GetStringSize(NodeGlyphs[node.Type], HorizontalAlignment.Left, -1, glyphSize);
			Vector2 textPos = center - new Vector2(sz.X * 0.5f, sz.Y * 0.5f);
			if (showCheckMark)
			{
				// 已结算且已离开：文字上移一点，在节点中间画对勾
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

		// 1) 先移动当前位置
		session.SetCurrentNode(nodeId);
		currentNodeId = nodeId;

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
		StageEncounterRow row = TryResolveEncounter(session, node);
		if (row != null)
		{
			session.BeginRunBattleEncounter(MapNodeTypeUtil.GetLayerNameByAct(session.Current.MapState.Act), row);
			SetStatus($"进入战斗：{row.Name}。");
			QueueRedraw();
			GetTree().ChangeSceneToFile(RunBattleScenePath);
			return;
		}

		// 3) 无配置：标记完成并停留地图
		node.Visited = true;
		session.MarkCurrentNodeVisitedAndAdvanceEncounter();
		SetStatus($"已到达（无配置遭遇），停留地图。");
		UpdateInfoLabel();
		QueueRedraw();
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
			rule = StageEncounterPicker.ResolveNormalCombatDifficultyByEncounterCount(session.Current.MapState.NormalEncounterIndex);
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
