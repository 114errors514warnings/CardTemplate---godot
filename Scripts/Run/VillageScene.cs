// VillageScene.cs
// 村庄地点场景（村庄地图交互案 §2.1–§2.3 / §五 / §六 / §七 / §十）：
//   37 格棋盘（R=3）+ 5 个设施单位 + 角色棋子 + 逐格移动（扣 0.3 时间点）+ 入口格触发 + 离开格回世界地图。
// 分层与输入：本场景挂在 `RunFlowScene` 的内容宿主（`RunUiLayers.Content`）里（`EmbeddedMode = true`）；
//   设施界面自己挂 Modal 层（`RunUiLayers.Modal = 40`），与背包 / 装备 / 结算同一套「同时只开一个」口径。
// 规则不住这里：走法 / 触发 / 抑制在 `VillageVisit`，设施数值与文案在 `VillageLodging` / `VillageForage` /
//   `SmithyCrafting` / `RestaurantTrade`，存档写在 `RunSession.Place.cs`；本文件只做「画出来 + 收输入 + 摆结果」。
using Godot;
using System;
using System.Collections.Generic;

public partial class VillageScene : Control
{
	/// <summary>村庄版图配表（`FilePathRegistry` 的 `Data.WorldMap.VillageLayout`）。</summary>
	public const string LayoutPath = "res://DataBase/WorldMap/VillageLayout.csv";

	/// <summary>标题行开头的场景名。</summary>
	public const string TitleText = "村庄";

	/// <summary>点 `稍后` 之后的一行提示（村庄案 §十二 第 3 条：要走开再走回才会重新弹）。</summary>
	public const string DeclinedText = "先不进去了（走开再回来可以重新选择）。";

	/// <summary>走离开格时的一行提示。</summary>
	public const string ExitText = "离开村庄，回到世界地图。";

	/// <summary>不是入口格 / 不是可走格时点一下的提示。</summary>
	public const string NoEntranceText = "这格没有可以进入的设施。";

	/// <summary>嵌入模式（由 `RunFlowScene` 装载）——与 `MapScene` / `CampScene` 同一约定。</summary>
	[Export] public bool EmbeddedMode = true;

	/// <summary>走到离开格：回世界地图（`RunFlowScene` 收，负责标记节点已访问）。</summary>
	public event Action PlaceExited;

	/// <summary>一行需要宿主知道的状态（常驻栏文案 / 日志）；本场景自己也显示在底部提示行。</summary>
	public event Action<string> Notice;

	private VillageVisit visit;
	private readonly Dictionary<int, Vector2> centers = new Dictionary<int, Vector2>();
	private float hexSize = 60f;
	private Control playerToken;
	private Label headerLabel;
	private Label hintLabel;
	private PlaceConfirmTips tips;
	private SmithyUi smithyUi;
	private RestaurantUi restaurantUi;
	private readonly List<FloatText> floats = new List<FloatText>();
	private int hoveredNodeId = -1;

	/// <summary>本次 tips 的归属回调（`PlaceConfirmTips` 只回一个 bool，具体处理由场景按设施决定）。</summary>
	private Action<bool> tipsCallback;

	/// <summary>版图数据（烟测断言占格与名称牌用）。</summary>
	public VillageLayoutData Layout => visit?.Layout;

	/// <summary>村庄访问状态机（烟测与 API 读它）。</summary>
	public VillageVisit Visit => visit;

	public int PlayerNodeId => visit?.PlayerNodeId ?? -1;
	public int EntranceNodeId => visit?.EntranceNodeId ?? -1;
	public int ExitNodeId => visit?.ExitNodeId ?? -1;

	/// <summary>底部提示行当前文案。</summary>
	public string HintText => hintLabel?.Text ?? string.Empty;

	/// <summary>确认 tips 是否正在显示。</summary>
	public bool TipsOpen => tips != null && tips.IsOpen;

	/// <summary>当前格上的格心（画布坐标；烟测 / 点击换算用）。</summary>
	public Vector2 CenterOfTile(int nodeId) => centers.TryGetValue(nodeId, out Vector2 center) ? center : Vector2.Zero;

	/// <summary>格半径（烟测点击换算用）。</summary>
	public float HexRadius => hexSize;

	/// <summary>设施界面是否打开。</summary>
	public bool SmithyOpen => smithyUi != null && smithyUi.IsOpen;
	public bool RestaurantOpen => restaurantUi != null && restaurantUi.IsOpen;

	/// <summary>锻铁铺界面实例（AI 接口读状态 / 点按钮；未创建时为 null）。</summary>
	public SmithyUi Smithy => smithyUi;

	/// <summary>餐厅界面实例（同上）。</summary>
	public RestaurantUi Restaurant => restaurantUi;

	/// <summary>确认 tips 的四行文案与按钮可用性（未打开时为空 / false）。</summary>
	public string TipsTitleText => tips != null && tips.IsOpen ? tips.TitleText : string.Empty;
	public string TipsEffectText => tips != null && tips.IsOpen ? tips.EffectText : string.Empty;
	public string TipsCostText => tips != null && tips.IsOpen ? tips.CostText : string.Empty;
	public bool TipsEnterEnabled => tips != null && tips.IsOpen && tips.EnterEnabled;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsPreset(LayoutPreset.FullRect);

		LoadingSystem.EnsureAllDataLoaded();

		// 地点设施的操作代价来自全局数据表（第 7 / 8 列）：进村庄时灌一次，之后
		// `VillageVisit` / `VillageForage` / `SmithyCrafting` / `RestaurantTrade` 都读这份运行期取值。
		GameVariables.Load().ApplyFacilityCosts();

		if (!LoadLayout(out string error))
		{
			GD.PrintErr($"[村庄] {error}");
			return;
		}

		RunSession run = RunSession.Instance;
		if (run?.Current == null)
		{
			GD.PrintErr("[村庄] 没有进行中的本局，无法进入村庄。");
			return;
		}

		// 读档恢复所在格（首次进村 = -1 → 落到入口格，村庄案 §十一 第 1 / 8 条）。
		visit.RestoreFrom(run.Current.VillageState);
		SaveVisitState();

		BuildUi();
		Relayout();
		RefreshHeader();
		SetHint("走到相邻格移动（局内移动不消耗时间点）；踏入设施门口的格即可交互，踏入离开格回世界地图。");
		GD.Print($"[村庄] 进入：位于格 {PlayerNodeId}（入口 {EntranceNodeId} / 离开 {ExitNodeId}），"
			+ $"时间点 {RunTimePoints.Format(run.RemainingToday)}，金币 {run.Current.Gold}。");
	}

	/// <summary>读村庄版图并跑六条不变式（配表改坏就在这里报错，不静默降级）。</summary>
	private bool LoadLayout(out string error)
	{
		error = string.Empty;
		try
		{
			// 口径（2026-10-05 修正）：`VillageLayoutCatalog.ParseLines` 吃的是**含表头**的行序列
			// （表头由它自己校验，见其类注释）。`LoadCsv.LoadCSVDataLines` 会**跳过表头**
			// （同 `LoadAreaObjectCsv` 的注释），喂进去会把第一行数据当成表头 → 版图恒加载失败，
			// 村庄场景只剩标题与提示行。这里必须用 `LoadCSVLines`。
			VillageLayoutData data = VillageLayoutCatalog.ParseLines(LoadCsv.LoadCSVLines(LayoutPath));
			string invariant = VillageLayout.Validate(data);
			if (!string.IsNullOrEmpty(invariant))
			{
				error = $"版图不变式不过：{invariant}";
				return false;
			}

			visit = new VillageVisit(data);
			return true;
		}
		catch (Exception ex)
		{
			error = $"读 VillageLayout.csv 失败：{ex.Message}";
			return false;
		}
	}

	/// <summary>按视口重算格半径与全部格心（整图一屏居中可见，村庄案 §2.1：不做平移 / 缩放）。</summary>
	private void Relayout()
	{
		if (visit == null)
		{
			return;
		}

		double xNormMax = 0d;
		double yNormMax = 0d;
		foreach (AxialHex hex in VillageLayout.AllCoords)
		{
			xNormMax = Math.Max(xNormMax, Math.Abs(hex.Q + hex.R * 0.5));
			yNormMax = Math.Max(yNormMax, Math.Abs(hex.R));
		}

		Vector2 viewport = GetViewportRect().Size;
		hexSize = (float)MapLayout.FitRadius((int)viewport.X, (int)viewport.Y, 150d, xNormMax, yNormMax);

		centers.Clear();
		for (int nodeId = 0; nodeId < VillageLayout.TileCount; nodeId++)
		{
			if (!VillageLayout.TryCoordOf(nodeId, out AxialHex hex))
			{
				continue;
			}

			(double x, double y) = MapLayout.CenterOf(hex.Q, hex.R, hexSize);
			centers[nodeId] = new Vector2(viewport.X * 0.5f + (float)x, viewport.Y * 0.5f + (float)y);
		}

		QueueRedraw();
	}

	/// <summary>把位置与抑制标记落档（村庄案 §十 第 6 条）。</summary>
	private void SaveVisitState()
	{
		RunSession run = RunSession.Instance;
		if (run?.Current == null || visit == null)
		{
			return;
		}

		visit.WriteTo(run.Current.VillageState);
		run.Save();
	}

	/// <summary>标题行刷新：村庄 · 第 N 天 · 剩余 X.X / 4 · 金币 G。</summary>
	private void RefreshHeader()
	{
		if (headerLabel == null)
		{
			return;
		}

		RunSession run = RunSession.Instance;
		headerLabel.Text = run?.Current == null
			? TitleText
			: $"{TitleText}　·　{RunTimePoints.FormatDayAndRemaining(run.Current.MapState.TimePoints)}　·　金币 {run.Current.Gold}";
	}

	// ── 界面构建 ──

	private void BuildUi()
	{
		PanelContainer header = new PanelContainer();
		header.SetAnchorsPreset(LayoutPreset.TopWide);
		header.OffsetTop = 8f;
		header.OffsetBottom = 46f;
		AddChild(header);

		MarginContainer headerMargin = new MarginContainer();
		headerMargin.AddThemeConstantOverride("margin_left", 16);
		headerMargin.AddThemeConstantOverride("margin_right", 16);
		header.AddChild(headerMargin);

		headerLabel = new Label { Text = TitleText };
		headerLabel.AddThemeFontSizeOverride("font_size", 18);
		headerMargin.AddChild(headerLabel);

		PanelContainer footer = new PanelContainer();
		footer.SetAnchorsPreset(LayoutPreset.BottomWide);
		footer.OffsetTop = -58f;
		footer.OffsetBottom = -8f;
		AddChild(footer);

		MarginContainer footerMargin = new MarginContainer();
		footerMargin.AddThemeConstantOverride("margin_left", 16);
		footerMargin.AddThemeConstantOverride("margin_right", 16);
		footer.AddChild(footerMargin);

		hintLabel = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		hintLabel.AddThemeFontSizeOverride("font_size", 14);
		footerMargin.AddChild(hintLabel);

		// 角色棋子：只提供「位置 + 尺寸」当锚点（本体画在 `_Draw` 里），确认 tips 跟随它。
		playerToken = new Control { MouseFilter = MouseFilterEnum.Ignore };
		playerToken.CustomMinimumSize = new Vector2(48f, 48f);
		AddChild(playerToken);

		tips = new PlaceConfirmTips();
		AddChild(tips);
		tips.Resolved += OnTipsResolved;
	}

	private void ShowTips(VillagePlot plot, string effectText, string costText, bool enterEnabled, string enterButtonText, Action<bool> resolved)
	{
		tipsCallback = resolved;
		tips.Follow(playerToken);
		tips.ShowTips(plot.DisplayName, effectText, costText, enterEnabled, enterButtonText);
	}

	private void OnTipsResolved(bool accepted)
	{
		Action<bool> callback = tipsCallback;
		tipsCallback = null;
		callback?.Invoke(accepted);
	}

	// ── 渲染 ──

	public override void _Draw()
	{
		if (visit == null || centers.Count == 0)
		{
			return;
		}

		Font font = ThemeDB.FallbackFont;

		// 1) 格间连线（与 `MapScene` 同一口径：两格心之间画 R 长的线段，两端各让出 R）。
		for (int nodeId = 0; nodeId < VillageLayout.TileCount; nodeId++)
		{
			if (!centers.TryGetValue(nodeId, out Vector2 centerA))
			{
				continue;
			}

			foreach (int next in VillageLayout.NeighborsOf(nodeId))
			{
				if (next <= nodeId || !centers.TryGetValue(next, out Vector2 centerB))
				{
					continue;
				}

				Vector2 delta = centerB - centerA;
				float distance = delta.Length();
				if (distance < 0.0001f)
				{
					continue;
				}

				Vector2 direction = delta / distance;
				DrawLine(centerA + direction * hexSize, centerB - direction * hexSize, EdgeColor, 2f, true);
			}
		}

		// 2) 格点：底色按「设施 / 入口格 / 离开格 / 空地」，可走格加黄框、所在格加白框。
		for (int nodeId = 0; nodeId < VillageLayout.TileCount; nodeId++)
		{
			if (!centers.TryGetValue(nodeId, out Vector2 center))
			{
				continue;
			}

			VillagePlot plot = visit.PlotOfTile(nodeId);
			bool isPlayer = nodeId == visit.PlayerNodeId;
			bool canStep = visit.IsWalkable(nodeId) && visit.IsAdjacentToPlayer(nodeId);
			float radius = canStep ? hexSize * 1.06f : hexSize;
			Color? border = isPlayer ? Colors.White : canStep ? Colors.Yellow : null;
			DrawPolygonHex(center, radius, TileColor(nodeId, plot), border, isPlayer ? 4f : 3f);

			string glyph = nodeId == visit.ExitNodeId ? "出" : nodeId == visit.EntranceNodeId ? "入"
				: visit.PlotOfEntrance(nodeId) != null ? "门" : string.Empty;
			if (glyph.Length > 0)
			{
				int glyphSize = Mathf.Max(16, (int)(hexSize * 0.45f));
				Vector2 glyphMeasure = font.GetStringSize(glyph, HorizontalAlignment.Left, -1, glyphSize);
				DrawString(font, center - new Vector2(glyphMeasure.X * 0.5f, -glyphMeasure.Y * 0.3f), glyph,
					HorizontalAlignment.Left, -1, glyphSize, Colors.White);
			}
		}

		// 3) 设施名称牌（村庄案 §2.2：名称牌可见；用过 / 暂不接待时转灰）。
		DrawFacilityPlates(font);

		// 4) 角色棋子（所在格中央的圆点）。
		if (centers.TryGetValue(visit.PlayerNodeId, out Vector2 playerCenter))
		{
			DrawCircle(playerCenter, hexSize * 0.30f, PlayerColor);
			DrawArc(playerCenter, hexSize * 0.30f, 0f, Mathf.Tau, 24, Colors.White, 3f, true);
		}

		// 5) 浮字（回复量 / 材料产出），随寿命淡出并上浮。
		foreach (FloatText item in floats)
		{
			Vector2 measure = font.GetStringSize(item.Text, HorizontalAlignment.Left, -1, 16);
			DrawString(font, item.Position - new Vector2(measure.X * 0.5f, 0f), item.Text, HorizontalAlignment.Left, -1, 16,
				new Color(1f, 0.95f, 0.6f, Mathf.Clamp(item.Life, 0f, 1f)));
		}
	}

	private void SetHint(string text)
	{
		if (hintLabel != null)
		{
			hintLabel.Text = text ?? string.Empty;
		}

		if (!string.IsNullOrEmpty(text))
		{
			Notice?.Invoke(text);
		}
	}

	private void DrawFacilityPlates(Font font)
	{
		RunSaveData data = RunSession.Instance?.Current;
		foreach (VillagePlot plot in visit.Layout.Plots)
		{
			if (!plot.IsFacility)
			{
				continue;
			}

			Vector2 sum = Vector2.Zero;
			foreach (int nodeId in plot.NodeIds)
			{
				sum += CenterOfTile(nodeId);
			}

			Vector2 plateCenter = sum / Mathf.Max(1, plot.NodeIds.Count);
			string suffix = FacilityStateSuffix(plot.Kind, data);
			string label = plot.DisplayName + suffix;
			int fontSize = Mathf.Max(14, (int)(hexSize * 0.34f));
			Vector2 measure = font.GetStringSize(label, HorizontalAlignment.Left, -1, fontSize);
			DrawString(font, plateCenter - new Vector2(measure.X * 0.5f, measure.Y * 0.5f), label,
				HorizontalAlignment.Left, -1, fontSize, suffix.Length > 0 ? Colors.Gray : Colors.White);
		}
	}

	/// <summary>名称牌后缀（旅馆案 §五 / 民宿案 §五）：用过 / 暂不接待时给灰态理由。</summary>
	private static string FacilityStateSuffix(VillagePlotKind kind, RunSaveData data)
	{
		if (data?.VillageState == null)
		{
			return string.Empty;
		}

		if (kind == VillagePlotKind.Inn && data.VillageState.InnUsed)
		{
			return "（已住过）";
		}

		if (kind == VillagePlotKind.Guesthouse && !string.IsNullOrEmpty(VillageLodging.ValidateGuesthouse(data)))
		{
			return "（暂不接待）";
		}

		return string.Empty;
	}

	private static Color TileColor(int nodeId, VillagePlot plot)
	{
		if (plot != null && plot.IsFacility)
		{
			return FacilityColors[(int)plot.Kind];
		}

		if (plot != null && plot.Kind == VillagePlotKind.Entrance)
		{
			return EntranceColor;
		}

		if (plot != null && plot.Kind == VillagePlotKind.Exit)
		{
			return ExitColor;
		}

		return VillageLayout.IsOuterRing(nodeId) ? EmptyOuterColor : EmptyColor;
	}

	private void DrawPolygonHex(Vector2 center, float radius, Color fill, Color? border, float borderWidth)
	{
		Vector2[] points = new Vector2[6];
		for (int i = 0; i < 6; i++)
		{
			(double x, double y) = MapLayout.VertexLocal(i, radius);
			points[i] = center + new Vector2((float)x, (float)y);
		}

		DrawColoredPolygon(points, fill);
		if (!border.HasValue || borderWidth <= 0f)
		{
			return;
		}

		for (int i = 0; i < points.Length; i++)
		{
			DrawLine(points[i], points[(i + 1) % points.Length], border.Value, borderWidth, true);
		}
	}

	public override void _Process(double delta)
	{
		if (visit != null && playerToken != null && centers.TryGetValue(visit.PlayerNodeId, out Vector2 center))
		{
			playerToken.Position = center - playerToken.CustomMinimumSize * 0.5f;
		}

		if (floats.Count == 0)
		{
			return;
		}

		for (int i = floats.Count - 1; i >= 0; i--)
		{
			FloatText item = floats[i];
			item.Life -= (float)delta;
			item.Position -= new Vector2(0f, 24f * (float)delta);
			floats[i] = item;
			if (item.Life <= 0f)
			{
				floats.RemoveAt(i);
			}
		}

		QueueRedraw();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationResized)
		{
			Relayout();
		}
	}

	// ── 配色（与 `MapScene` / `CampScene` 同料：深底 + 高对比边框） ──

	private static readonly Color EdgeColor = new Color(0.32f, 0.36f, 0.42f, 0.9f);
	private static readonly Color EmptyColor = new Color(0.16f, 0.18f, 0.22f, 1f);
	private static readonly Color EmptyOuterColor = new Color(0.12f, 0.13f, 0.16f, 1f);
	private static readonly Color EntranceColor = new Color(0.20f, 0.34f, 0.48f, 1f);
	private static readonly Color ExitColor = new Color(0.46f, 0.30f, 0.18f, 1f);
	private static readonly Color PlayerColor = new Color(0.30f, 0.72f, 0.95f, 1f);

	/// <summary>5 个设施的底色（索引 = `VillagePlotKind`）。</summary>
	private static readonly Color[] FacilityColors =
	{
		new Color(0.52f, 0.44f, 0.22f, 1f), // 旅馆
		new Color(0.40f, 0.34f, 0.44f, 1f), // 民宿
		new Color(0.46f, 0.32f, 0.24f, 1f), // 锻铁铺
		new Color(0.44f, 0.40f, 0.20f, 1f), // 餐厅
		new Color(0.20f, 0.42f, 0.26f, 1f), // 树林
	};

	/// <summary>一条上浮的文字（回复量 / 材料产出）。</summary>
	private struct FloatText
	{
		public string Text;
		public Vector2 Position;
		public float Life;
	}

	// ── 输入：点相邻格 = 走一步 ──

	public override void _GuiInput(InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left && button.Pressed)
		{
			int hit = FindTileAt(button.Position);
			if (hit >= 0)
			{
				TryMoveTo(hit);
			}
			else
			{
				SetHint(VillageVisit.BlockedTileText);
			}

			GetViewport()?.SetInputAsHandled();
		}
	}

	/// <summary>画布坐标 → 格 NodeId（取最近格心；偏出 0.9 格半径算没点中）。</summary>
	public int FindTileAt(Vector2 localPosition)
	{
		int best = -1;
		float bestDistance = float.MaxValue;
		foreach (KeyValuePair<int, Vector2> pair in centers)
		{
			float distance = pair.Value.DistanceTo(localPosition);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = pair.Key;
			}
		}

		return bestDistance <= hexSize * 0.9f ? best : -1;
	}

	/// <summary>
	/// 走到相邻格（玩家口径：与点鼠标同一个入口）。
	/// 顺序 = `TryStep`（走法判定 + 落位 + 触发分类）→ 落档 → 处理触发。
	/// 村庄案 §四：**局内移动不消耗时间点、也不消耗能量**（时间点只在世界地图「节点 → 相邻节点」那一跳扣），
	/// 所以这里没有任何扣款步骤。
	/// </summary>
	public bool TryMoveTo(int nodeId)
	{
		RunSession run = RunSession.Instance;
		if (run?.Current == null || visit == null)
		{
			return false;
		}

		if (!visit.TryStep(nodeId, out VillageTileTrigger trigger, out VillagePlot plot, out string error))
		{
			SetHint(error);
			return false;
		}

		SaveVisitState();
		RefreshHeader();
		QueueRedraw();

		switch (trigger)
		{
			case VillageTileTrigger.Exit:
				OnExitTile();
				break;
			case VillageTileTrigger.Facility:
				TriggerFacility(plot);
				break;
			default:
				SetHint(string.Empty);
				break;
		}

		return true;
	}

	/// <summary>踏入离开格：回世界地图（「节点标记已访问」由 `RunSession.CompletePendingPlaceToMap` 负责）。</summary>
	private void OnExitTile()
	{
		SetHint(ExitText);
		PlaceExited?.Invoke();
	}

	/// <summary>踏上入口格之后按设施分流（带名称牌的拒绝原因也在这里，村庄案 §五）。</summary>
	public void TriggerFacility(VillagePlot plot)
	{
		if (plot == null)
		{
			SetHint(NoEntranceText);
			return;
		}

		switch (plot.Kind)
		{
			case VillagePlotKind.Inn:
				OfferInn(plot);
				break;
			case VillagePlotKind.Guesthouse:
				OfferGuesthouse(plot);
				break;
			case VillagePlotKind.Forest:
				OfferForest(plot);
				break;
			case VillagePlotKind.Smithy:
				OpenFacility(plot, OpenSmithy);
				break;
			case VillagePlotKind.Restaurant:
				OpenFacility(plot, OpenRestaurant);
				break;
			default:
				SetHint(NoEntranceText);
				break;
		}
	}

	/// <summary>入口被拒的统一出口：给原因 + 记「本格已处理」+ 落档（避免站在门口反复触发）。</summary>
	private void RejectEntrance(string reason)
	{
		SetHint(reason);
		visit.NoteEntranceHandled();
		SaveVisitState();
	}

	private void OfferInn(VillagePlot plot)
	{
		RunSession run = RunSession.Instance;
		RunSaveData data = run.Current;
		string reason = VillageLodging.ValidateInn(data);
		if (!string.IsNullOrEmpty(reason))
		{
			RejectEntrance(reason);
			return;
		}

		bool enough = data.Gold >= VillageLodging.InnGold;
		string effect = VillageLodging.DescribeEffect(data, inn: true);
		string cost = VillageLodging.DescribeCost(inn: true)
			+ (enough ? string.Empty : $"（需要 {VillageLodging.InnGold}，当前 {data.Gold}）");
		ShowTips(plot, effect, cost, enough, enough ? PlaceConfirmTips.EnterText : "金币不足", accepted =>
		{
			if (!accepted)
			{
				RejectEntrance(DeclinedText);
				return;
			}

			if (run.TryRestAtInn(out List<int> healed, out string error))
			{
				AfterLodging(plot, healed, "旅馆");
			}
			else
			{
				RejectEntrance(error);
			}
		});
	}

	private void OfferGuesthouse(VillagePlot plot)
	{
		RunSession run = RunSession.Instance;
		RunSaveData data = run.Current;
		string reason = VillageLodging.ValidateGuesthouse(data);
		if (!string.IsNullOrEmpty(reason))
		{
			RejectEntrance(reason);
			return;
		}

		string effect = VillageLodging.DescribeEffect(data, inn: false);
		string cost = VillageLodging.DescribeCost(inn: false);
		ShowTips(plot, effect, cost, true, PlaceConfirmTips.EnterText, accepted =>
		{
			if (!accepted)
			{
				RejectEntrance(DeclinedText);
				return;
			}

			if (run.TryRestAtGuesthouse(out List<int> healed, out string error))
			{
				AfterLodging(plot, healed, "民宿");
			}
			else
			{
				RejectEntrance(error);
			}
		});
	}

	private void OfferForest(VillagePlot plot)
	{
		RunSession run = RunSession.Instance;
		// 进入树林本身**不是操作**（不扣时间点）；时间点只在点 `进入` 执行一次搜寻时按**树林专属**表值扣 ——
		// 不足时由 `TryForageMaterials` 拒绝并给「去旅馆 / 民宿过夜」的一行原因。
		ShowTips(plot, $"搜寻：材料 ×{VillageForage.PicksPerSearch}",
			$"代价：每次搜寻 {RunTimePoints.Format(VillageForage.TimePointCost)} 时间点（点「进入」才收）",
			true, PlaceConfirmTips.EnterText, accepted =>
			{
				if (!accepted)
				{
					RejectEntrance(DeclinedText);
					return;
				}

				if (run.TryForageMaterials(VillagePlaceData.ForagePool(),
					out List<(int MaterialId, int Count)> gained, out string error))
				{
					visit.NoteEntranceHandled();
					SaveVisitState();
					RefreshHeader();
					ShowForageResult(gained);
				}
				else
				{
					RejectEntrance(error);
				}
			});
	}

	/// <summary>
	/// 专用界面类设施（锻铁铺 / 餐厅）：**进入设施本身不是操作**（村庄案 §四，2026-10-05 第四轮口径）——
	/// 打开界面不看、不扣时间点；时间点只在界面里的每次操作（打造 / 烹饪 / 点菜）按全局数据表的
	/// `VillageOperationTimePointCost`（默认 0.1）收，界面内不足时按钮禁用。
	/// </summary>
	private void OpenFacility(VillagePlot plot, Action openUi)
	{
		visit.NoteEntranceHandled();
		SaveVisitState();
		RefreshHeader();
		SetHint($"{plot.DisplayName}：每次操作消耗 {RunTimePoints.Format(VillageVisit.OperationTimePointCost)} 时间点（进入本身不消耗）。");
		openUi();
	}

	private void OpenSmithy()
	{
		if (smithyUi == null || !GodotObject.IsInstanceValid(smithyUi))
		{
			smithyUi = new SmithyUi();
			AddChild(smithyUi);
		}

		smithyUi.Open(SmithyContext.VillageSmithy, () => SetHint("离开锻铁铺。"));
	}

	private void OpenRestaurant()
	{
		if (restaurantUi == null || !GodotObject.IsInstanceValid(restaurantUi))
		{
			restaurantUi = new RestaurantUi();
			AddChild(restaurantUi);
		}

		restaurantUi.Open(RefreshHeader, () => SetHint("离开餐厅。"));
	}

	/// <summary>过夜类设施结算完成：浮字 + 提示 + 标题行刷新（旅馆案 §四 第 7 步 / 民宿案 §六 第 5 步）。</summary>
	private void AfterLodging(VillagePlot plot, List<int> healed, string name)
	{
		visit.NoteEntranceHandled();
		SaveVisitState();
		RefreshHeader();
		QueueRedraw();

		string detail = string.Join(" / ", healed.ConvertAll(x => x.ToString()));
		PushFloat($"{name}：回复 {detail}", CenterOfTile(PlayerNodeId));
		SetHint($"{name}过夜：各角色回复 {detail} 生命；已推进到第 {RunSession.Instance?.CurrentDay ?? 1} 天。");
	}

	private void ShowForageResult(List<(int MaterialId, int Count)> gained)
	{
		if (gained == null || gained.Count == 0)
		{
			SetHint("搜寻：这次什么也没找到。");
			return;
		}

		List<string> parts = new List<string>();
		foreach ((int materialId, int count) in gained)
		{
			parts.Add($"{VillagePlaceData.MaterialName(materialId)} ×{count}");
		}

		string text = string.Join(" / ", parts);
		PushFloat(text, CenterOfTile(PlayerNodeId));
		SetHint($"搜寻到：{text}（已入背包）。");
	}

	private void PushFloat(string text, Vector2 position)
	{
		floats.Add(new FloatText { Text = text, Position = position - new Vector2(0f, hexSize * 0.5f), Life = 2f });
		QueueRedraw();
	}

	// ── AI 接口窄口（2026-10-05，`run.village.*`）────────────────
	// 口径同 [in-run-api-poking]：这里只是「等同玩家点一下」——逐格走、关闭界面；
	// 走法判定 / 触发分类仍在 `VillageVisit.TryStep`，操作结算仍在 `RunSession.Place.cs`。

	/// <summary>
	/// 走到指定格（= 玩家连续点相邻格）：BFS 找一条只走可走格的最短路径，**逐步**走 <see cref="TryMoveTo"/>
	/// （每步仍走 `CanStep` 判定与触发分流；踏到离开格会自然走退出流程）。中途被拒立即停手并返回 false。
	/// </summary>
	public bool TryWalkTo(int targetNodeId)
	{
		if (visit == null)
		{
			return false;
		}

		List<int> path = FindPathTo(targetNodeId);
		if (path == null)
		{
			SetHint(VillageVisit.NotAdjacentText + "（目标格不在可走格里）");
			return false;
		}

		foreach (int step in path)
		{
			if (!TryMoveTo(step))
			{
				return false;
			}
		}

		return visit.PlayerNodeId == targetNodeId;
	}

	/// <summary>BFS 最短相邻路径（只走可走格；不含起点；不可达返回空列表，目标本身不可走返回 null）。</summary>
	private List<int> FindPathTo(int targetNodeId)
	{
		if (!visit.IsWalkable(targetNodeId))
		{
			return null;
		}

		int start = visit.PlayerNodeId;
		if (start == targetNodeId)
		{
			return new List<int>();
		}

		Dictionary<int, int> previous = new Dictionary<int, int> { [start] = -1 };
		Queue<int> frontier = new Queue<int>();
		frontier.Enqueue(start);
		while (frontier.Count > 0)
		{
			int current = frontier.Dequeue();
			foreach (int next in VillageLayout.NeighborsOf(current))
			{
				if (!visit.IsWalkable(next) || previous.ContainsKey(next))
				{
					continue;
				}

				previous[next] = current;
				if (next == targetNodeId)
				{
					frontier.Clear();
					break;
				}

				frontier.Enqueue(next);
			}
		}

		if (!previous.ContainsKey(targetNodeId))
		{
			return new List<int>();
		}

		List<int> path = new List<int>();
		for (int node = targetNodeId; node >= 0 && node != start; node = previous[node])
		{
			path.Add(node);
		}

		path.Reverse();
		return path;
	}

	/// <summary>关闭当前打开的设施界面（= `Esc` / `关闭`）；没有打开的界面返回 false。</summary>
	public bool TryCloseFacilityUi()
	{
		if (smithyUi != null && GodotObject.IsInstanceValid(smithyUi) && smithyUi.IsOpen)
		{
			smithyUi.Close();
			return true;
		}

		if (restaurantUi != null && GodotObject.IsInstanceValid(restaurantUi) && restaurantUi.IsOpen)
		{
			restaurantUi.Close();
			return true;
		}

		return false;
	}

	// ── 烟测 / 调试通道（口径同 `MapScene.TryEnterReachableNode`：走玩家同一条路）──

	/// <summary>**烟测用**：按玩家口径走到相邻格（`TryMoveTo`：门槛 → 扣时间点 → 触发）。</summary>
	public bool TryMoveToAdjacent(int nodeId) => TryMoveTo(nodeId);

	/// <summary>**烟测用**：把角色直接放到某格（不扣时间点），随后可 `TryTriggerPendingEntrance`。</summary>
	public bool DebugPlaceAt(int nodeId)
	{
		if (visit == null || !visit.DebugPlaceAt(nodeId))
		{
			return false;
		}

		RefreshHeader();
		QueueRedraw();
		return true;
	}

	/// <summary>**烟测用**：处理当前格上待触发的入口（站在门口 = 玩家此刻会看到 tips / 界面）。</summary>
	public bool TryTriggerPendingEntrance()
	{
		VillagePlot plot = visit?.PendingEntranceAtPlayer;
		if (plot == null)
		{
			SetHint(NoEntranceText);
			return false;
		}

		TriggerFacility(plot);
		return true;
	}

	/// <summary>**烟测用**：确认 tips 的 `进入`（等价于点按钮）。</summary>
	public bool AcceptTips()
	{
		if (!TipsOpen)
		{
			return false;
		}

		tips.Close();
		OnTipsResolved(true);
		return true;
	}

	/// <summary>**烟测用**：确认 tips 的 `稍后`。</summary>
	public bool DeclineTips()
	{
		if (!TipsOpen)
		{
			return false;
		}

		tips.Close();
		OnTipsResolved(false);
		return true;
	}

	/// <summary>**烟测用**：直接走离开格流程。</summary>
	public void DebugExit() => OnExitTile();
}
