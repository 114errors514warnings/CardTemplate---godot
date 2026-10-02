// EquipmentUi.cs
// 运行局**装备界面**（《装备系统交互案》§一 / §二 / §三，2026-10-02 装备界面批，P0-18 界面半）。
//   入口：常驻顶栏**时间点行**里紧排 `背包` 之后的 `装备` 按钮（用户口径 2026-10-02「入口放在背包入口的右边」）；
//   界面挂 `RunUiLayers.Modal = 40`，单实例，与 `背包` **互斥**（打开一个自动关另一个），`关闭` / `Esc` 退出；
//   开合本身不改任何游戏状态，每次穿脱成功立刻由 `RunSession` 落档。
//
// 结构（案 §二）：上边栏 = 三名角色 Tab（取名口 `CharacterSlotNaming`，与结算面板同一份）；
//   主体 = **一行 3 个 × 共 2 行** = 头部 / 身体 / 脚部 + 饰品 × 配置数量（改 `EquipmentConfig.csv` 自动加减格位）；
//   下面是手位横条（左手 / 右手，与背包界面同一份状态、同一套规则）。
//   左侧列 = **背包装备列表**：案 §二 的画面没画它，但 §三 要求「背包 → 部位格」能拖，而两个界面是互斥模态、
//   不能跨窗口拖 —— 因此装备界面自带一个只列「装备」类目的背包格区（2026-10-02 实现口径，已回写案文 §二）。
//
// 拖动（案 §三）：拿起恒可用（2026-10-02 口径与背包界面一致）；落点一律由 `RunSession` 的整理 API 判定并给原因，
//   因此鼠标拖动（Godot 拖放栈）与烟测的 `SimulateDrop` 走**同一条**通路，界面不自带规则。
// 格控件复用 `BagUi.BagCell`（`IDragHost` 是两边共用的窄口）；面板底色 / 格底样式与背包界面同料。
using Godot;
using System;
using System.Collections.Generic;

public partial class EquipmentUi : Node, IDragHost
{
	public const string TitleText = "装备";
	public const string CloseText = "关闭";
	public const string BagColumnTitle = "背包（装备）";
	public const string SlotColumnTitle = "部位（头部 / 身体 / 脚部 / 饰品）";
	public const string HandColumnTitle = "手位（左手 / 右手）";
	public const string EmptyCellText = "空";
	public const string LoadLabelPrefix = "负荷 ";
	public const string DefaultHintText = "拖动换装：装备 → 头部 / 身体 / 脚部 / 饰品格或左手 / 右手位；拖回背包即卸下。";
	public const string MovedHintPrefix = "已移动：";
	public const string DropRejectedPrefix = "落点被拒：";

	/// <summary>每页格数（与背包界面同一套分页算术 `BagPageMath`：5 × 5 = 25）。</summary>
	public const int PageCapacity = BagPageMath.PageCapacity;

	public const string PreviousPageText = "上一页";
	public const string NextPageText = "下一页";

	/// <summary>部位格尺寸：**正方形**（同一口径：案 §二 要求 6 格等宽等高、两行对齐）。</summary>
	public const int SlotCellWidth = 96;
	public const int SlotCellHeight = SlotCellWidth;

	/// <summary>手位格尺寸（与背包界面的手位格同料）。</summary>
	public const int HandCellWidth = 104;
	public const int HandCellHeight = 62;

	private CanvasLayer modalLayer;
	private Control root;
	private Button closeButton;
	private readonly List<Button> characterTabs = new List<Button>();
	private Label loadLabel;
	private Label hintLabel;
	private Label pageLabel;
	private GridContainer bagGrid;
	private readonly BagCell[] bagSlotCells = new BagCell[PageCapacity];
	private GridContainer slotGrid;
	private readonly List<BagCell> slotCells = new List<BagCell>();
	private readonly BagCell[] handCells = new BagCell[RunEquipmentSystem.HandCount];
	private Button previousPageButton;
	private Button nextPageButton;
	private PanelContainer bannerPanel;
	private Label bannerLabel;

	private int activeSlotIndex;

	/// <summary>当前页（0 基；越界一律由 `BagPageMath.ClampPage` 夹回）。</summary>
	private int bagPage;

	/// <summary>当前页**有内容**的格数。</summary>
	private int bagFilledCount;

	/// <summary>背包装备类目的条目总数（分页夹取与文案用）。</summary>
	private int bagTotalEntries;

	private string lastHint = string.Empty;
	private bool lastRejected;
	private string bannerGateReason = string.Empty;

	private static RunSession Session => RunSession.Instance;
	private static RunSaveData Run => RunSession.Instance?.Current;

	/// <summary>界面上正在显示的角色槽位数（= `CharacterSlots.Count`）。</summary>
	private int SlotCount => Run == null ? 0 : Run.CharacterSlots.Count;

	/// <summary>界面是否打开（单实例判据）。</summary>
	public bool IsOpen => root != null && GodotObject.IsInstanceValid(root) && root.Visible;

	/// <summary>
	/// 落点是否受闸门限制（口径同背包界面 §三 的 2026-10-02 改判）：闸门只拦**落点**，不拦拿起。
	/// 与背包界面共用同一个运行时原因 `RunSession.BagArrangeBlockReason`（不入档）。
	/// </summary>
	public bool IsArrangeBlocked => Session?.Current == null || !string.IsNullOrEmpty(Session.BagArrangeBlockReason);

	/// <summary>`IsArrangeBlocked` 的旧名（语义同上）。</summary>
	public bool IsReadOnly => IsArrangeBlocked;

	/// <summary>负荷行文案（烟测断言「装上后负荷变化」用）。</summary>
	public string LoadText => loadLabel?.Text ?? string.Empty;

	/// <summary>提示行文案（成功 / 失败原因）。</summary>
	public string HintText => hintLabel?.Text ?? string.Empty;

	/// <summary>顶部横幅文案（空串 = 横幅隐藏）。</summary>
	public string BannerText => bannerPanel != null && bannerPanel.Visible ? bannerLabel?.Text ?? string.Empty : string.Empty;

	/// <summary>当前角色 Tab（部位格与手位归属）。</summary>
	public int ActiveSlotIndex => activeSlotIndex;

	/// <summary>背包（装备）当前页**有内容**的格数。</summary>
	public int BagCellCount => bagFilledCount;

	/// <summary>部位格总数（3 + 饰品格数）。</summary>
	public int BodySlotCount => slotCells.Count;

	/// <summary>当前角色槽的显示名（角色 Tab 文案；与结算面板同一取名口）。</summary>
	public string CharacterTabText(int slotIndex) => Session?.GetSlotDisplayName(slotIndex) ?? string.Empty;

	/// <summary>页码文案（`第 x / y 页`）。</summary>
	public string PageText => pageLabel?.Text ?? string.Empty;

	public int PageNumber => bagPage + 1;
	public int PageCount => BagPageMath.PageCountOf(bagTotalEntries);
	public bool CanGoPreviousPage => BagPageMath.HasPrevious(bagPage, bagTotalEntries);
	public bool CanGoNextPage => BagPageMath.HasNext(bagPage, bagTotalEntries);

	/// <summary>部位格的格标题（空位也显示部位名，案 §九 第 6 条）。</summary>
	public static string SlotCaption(int slotKind, int indexInKind) =>
		RunEquipmentSystem.SlotLabel(slotKind, indexInKind);

	/// <summary>部位格上的装备名（空位 = 空串；烟测断言拖动结果用）。</summary>
	public string BodySlotText(int slotKind, int indexInKind) =>
		RunEquipmentSystem.BodySlotDefinition(Run, activeSlotIndex, slotKind, indexInKind) ?? string.Empty;

	/// <summary>手位上的装备名（空位 = 空串）。</summary>
	public string HandText(int hand) => RunEquipmentSystem.HandDefinition(Run, activeSlotIndex, hand) ?? string.Empty;

	/// <summary>背包装备格第 `index` 格的主文案（空格 = 空串）。</summary>
	public string BagSlotText(int index)
	{
		if (index < 0 || index >= bagSlotCells.Length || !GodotObject.IsInstanceValid(bagSlotCells[index]))
		{
			return string.Empty;
		}

		return bagSlotCells[index].TitleText;
	}

	// ── 控件名与载荷（烟测按名字找控件；鼠标拖动与烟测共用载荷串） ─────────────

	public static string CharacterTabName(int slotIndex) => $"EquipCharTab_{slotIndex}";
	public static string BagCellName(int index) => $"EquipBagCell_{index}";
	public static string BodySlotName(int slotKind, int indexInKind) => $"EquipBodySlot_{slotKind}_{indexInKind}";
	public static string HandCellName(int slotIndex, int hand) => $"EquipHand_{slotIndex}_{hand}";
	public const string PreviousPageName = "EquipPrevPage";
	public const string NextPageName = "EquipNextPage";
	public const string PageLabelName = "EquipPageLabel";

	/// <summary>载荷：`bag|{实例键}`（从背包装备格拖出；与背包界面的载荷同形）。</summary>
	public static string BagPayload(string instanceId) => $"bag|{instanceId}";

	/// <summary>载荷：`hand|{角色槽}|{手位}`（与背包界面的载荷同形）。</summary>
	public static string HandPayload(int slotIndex, int hand) => $"hand|{slotIndex}|{hand}";

	/// <summary>载荷：`bodyslot|{部位}|{格序}`（从部位格拖出）。</summary>
	public static string BodySlotPayload(int slotKind, int indexInKind) => $"bodyslot|{slotKind}|{indexInKind}";

	public void Bind(CanvasLayer modal)
	{
		modalLayer = modal;
	}

	/// <summary>开关（入口按钮用）：已开 → 关闭；未开 → 打开（打开时不改任何游戏状态）。</summary>
	public void ToggleOpen()
	{
		if (IsOpen)
		{
			Close();
			return;
		}

		Open();
	}

	/// <summary>打开界面：重建面板（每次打开都按当刻存档重画，避免读到旧状态）。</summary>
	public void Open()
	{
		if (modalLayer == null || Session?.Current == null)
		{
			return;
		}

		BuildPanel();
		root.Visible = true;
		lastHint = string.Empty;
		lastRejected = false;
		bagPage = 0;
		Refresh();
	}

	/// <summary>关闭界面（关闭不改任何状态：部位格、手位、背包的当前值原样保留）。</summary>
	public void Close()
	{
		if (root == null || !GodotObject.IsInstanceValid(root))
		{
			return;
		}

		root.QueueFree();
		root = null;
		Array.Clear(bagSlotCells, 0, bagSlotCells.Length);
		Array.Clear(handCells, 0, handCells.Length);
		slotCells.Clear();
		characterTabs.Clear();
		previousPageButton = null;
		nextPageButton = null;
		pageLabel = null;
		bannerPanel = null;
		bannerLabel = null;
	}

	/// <summary>按当刻存档重画（打开 / 拖动 / 闸门变化时调用）。</summary>
	public void Refresh()
	{
		if (!IsOpen)
		{
			return;
		}

		if (activeSlotIndex >= SlotCount)
		{
			activeSlotIndex = 0;
		}

		RefreshLoad();
		RefreshCharacterTabs();
		RefreshBagColumn();
		RefreshSlotColumn();
		RefreshHint();
		RefreshBanner();
	}

	// ── 翻页与 Tab（与背包界面同一套算术与手感） ─────────────────

	public void NextPage() => GoToPage(bagPage + 1);
	public void PreviousPage() => GoToPage(bagPage - 1);

	public void GoToPage(int page)
	{
		if (!IsOpen)
		{
			return;
		}

		bagPage = BagPageMath.ClampPage(page, bagTotalEntries);
		Refresh();
	}

	/// <summary>切换角色 Tab（与点 Tab 同一条通路；界面未打开 / 槽位越界返回 false）。</summary>
	public bool SelectCharacterTab(int slotIndex)
	{
		if (!IsOpen || slotIndex < 0 || slotIndex >= SlotCount)
		{
			return false;
		}

		activeSlotIndex = slotIndex;
		Refresh();
		return true;
	}

	/// <summary>
	/// AI 接口 / 烟测：按「格名」取一次拖动（`fromCell` 的画面内容 → `toCell`）——
	/// 与鼠标拖动共用 `ApplyDrop`，因此这里能覆盖到的规则就是玩家真能动到的规则。
	/// </summary>
	public bool DragCell(string fromCell, string toCell)
	{
		string payload = PayloadOfCell(fromCell);
		return payload.Length > 0 && SimulateDrop(payload, toCell);
	}

	/// <summary>烟测入口：按控件名找落点并喂一次拖动。</summary>
	public bool SimulateDrop(string payload, string targetName)
	{
		if (root == null || !GodotObject.IsInstanceValid(root))
		{
			return false;
		}

		BagCell target = FindCell(root, targetName);
		return target != null && ApplyDrop(target, payload);
	}

	/// <summary>烟测入口：按控件名取该格的拖动载荷（空串 = 拿不起来）。</summary>
	public string PayloadOfCell(string cellName)
	{
		if (root == null || !GodotObject.IsInstanceValid(root))
		{
			return string.Empty;
		}

		BagCell cell = FindCell(root, cellName);
		return cell == null ? string.Empty : PayloadOf(cell);
	}

	// ── 面板构建（案 §二 的结构：角色 Tab → 部位格 → 手位横条） ─────────────

	private void BuildPanel()
	{
		Close();
		root = new Control { Name = "EquipPanelRoot", MouseFilter = Control.MouseFilterEnum.Stop };
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		modalLayer.AddChild(root);

		ColorRect dim = new ColorRect { Color = new Color(0, 0, 0, .55f), MouseFilter = Control.MouseFilterEnum.Ignore };
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(dim);

		CenterContainer center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(center);

		// 宽度按「左列 5 × 96 + 右列 3 × 96 + 手位 2 × 104」配；底板与格底沿用背包界面的深色料。
		PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(940, 0) };
		StyleBoxFlat panelStyle = new StyleBoxFlat
		{
			BgColor = new Color("141c24"),
			BorderColor = new Color("2b3a45"),
			ContentMarginLeft = 0,
			ContentMarginRight = 0,
			ContentMarginTop = 0,
			ContentMarginBottom = 0,
		};
		panelStyle.SetBorderWidthAll(2);
		panelStyle.SetCornerRadiusAll(6);
		panel.AddThemeStyleboxOverride("panel", panelStyle);
		center.AddChild(panel);

		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_bottom", 16);
		panel.AddChild(margin);

		VBoxContainer vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 10);
		margin.AddChild(vbox);

		BuildTitleRow(vbox);
		BuildBannerRow(vbox);
		BuildCharacterTabRow(vbox);

		HBoxContainer columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 22);
		vbox.AddChild(columns);
		BuildBagColumn(columns);
		BuildSlotColumn(columns);

		hintLabel = new Label { Text = DefaultHintText, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		hintLabel.AddThemeFontSizeOverride("font_size", 14);
		hintLabel.AddThemeColorOverride("font_color", new Color("8fa1ad"));
		vbox.AddChild(hintLabel);
	}

	private void BuildTitleRow(Control parent)
	{
		HBoxContainer row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);
		parent.AddChild(row);

		Label title = new Label { Text = TitleText, VerticalAlignment = VerticalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 22);
		title.AddThemeColorOverride("font_color", new Color("f5d98c"));
		row.AddChild(title);

		loadLabel = new Label
		{
			Text = string.Empty,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
		};
		loadLabel.AddThemeFontSizeOverride("font_size", 16);
		row.AddChild(loadLabel);

		closeButton = new Button { Name = "EquipClose", Text = CloseText, CustomMinimumSize = new Vector2(96, 34) };
		closeButton.Pressed += Close;
		row.AddChild(closeButton);
	}

	/// <summary>顶部横幅（与背包界面同一形态）：闸门有原因时常驻说明，规则拒绝时显示「落点被拒：原因」。</summary>
	private void BuildBannerRow(Control parent)
	{
		bannerPanel = new PanelContainer { Visible = false, Name = "EquipBanner" };
		StyleBoxFlat style = new StyleBoxFlat
		{
			BgColor = new Color("4a2530"),
			ContentMarginLeft = 12,
			ContentMarginRight = 12,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
		};
		style.SetCornerRadiusAll(4);
		bannerPanel.AddThemeStyleboxOverride("panel", style);
		parent.AddChild(bannerPanel);

		bannerLabel = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		bannerLabel.AddThemeFontSizeOverride("font_size", 15);
		bannerLabel.AddThemeColorOverride("font_color", new Color("ffb4a2"));
		bannerPanel.AddChild(bannerLabel);
	}

	/// <summary>上边栏：三名角色 Tab（顺序 = `CharacterSlots` 槽位序；文案 = 角色显示名，同名带槽位编号）。</summary>
	private void BuildCharacterTabRow(Control parent)
	{
		HBoxContainer row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);
		characterTabs.Clear(); // 每次重建面板都从空表起算（关闭再打开不累积旧 Tab）

		for (int i = 0; i < SlotCount; i++)
		{
			int slotIndex = i;
			Button button = new Button { Name = CharacterTabName(i), ToggleMode = true, CustomMinimumSize = new Vector2(120, 32) };
			button.Pressed += () =>
			{
				activeSlotIndex = slotIndex;
				Refresh();
			};
			row.AddChild(button);
			characterTabs.Add(button);
		}
	}

	private void BuildBagColumn(Control parent)
	{
		VBoxContainer column = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		column.AddThemeConstantOverride("separation", 6);
		parent.AddChild(column);

		column.AddChild(SectionLabel(BagColumnTitle));
		bagGrid = new GridContainer { Columns = BagPageMath.Columns, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
		bagGrid.AddThemeConstantOverride("h_separation", 8);
		bagGrid.AddThemeConstantOverride("v_separation", 8);
		column.AddChild(bagGrid);

		for (int i = 0; i < PageCapacity; i++)
		{
			BagCell cell = new BagCell(this, BagCellKind.BagEntry, i, -1, BagCellName(i));
			bagSlotCells[i] = cell;
			bagGrid.AddChild(cell);
		}

		BuildPagerRow(column);
	}

	private void BuildPagerRow(Control parent)
	{
		HBoxContainer row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);

		previousPageButton = new Button
		{
			Name = PreviousPageName,
			Text = PreviousPageText,
			CustomMinimumSize = new Vector2(88, 30),
		};
		previousPageButton.Pressed += PreviousPage;
		row.AddChild(previousPageButton);

		pageLabel = new Label
		{
			Name = PageLabelName,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		pageLabel.AddThemeFontSizeOverride("font_size", 14);
		pageLabel.AddThemeColorOverride("font_color", new Color("cfe3ef"));
		row.AddChild(pageLabel);

		nextPageButton = new Button
		{
			Name = NextPageName,
			Text = NextPageText,
			CustomMinimumSize = new Vector2(88, 30),
		};
		nextPageButton.Pressed += NextPage;
		row.AddChild(nextPageButton);
	}

	/// <summary>
	/// 主体（案 §二）：**一行 3 个**的部位格 —— 第 1 行头部 / 身体 / 脚部，第 2 行饰品 × 配置数量
	/// （饰品多于 3 时按同一格样式续排下一行，少于 3 时该行格数随之减少）；下面接手位横条（左右手居中）。
	/// </summary>
	private void BuildSlotColumn(Control parent)
	{
		VBoxContainer column = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
		column.AddThemeConstantOverride("separation", 6);
		parent.AddChild(column);

		column.AddChild(SectionLabel(SlotColumnTitle));
		slotGrid = new GridContainer { Columns = 3 };
		slotGrid.AddThemeConstantOverride("h_separation", 8);
		slotGrid.AddThemeConstantOverride("v_separation", 8);
		column.AddChild(slotGrid);
		slotCells.Clear();

		for (int kind = 0; kind < RunEquipmentSystem.BodySlotKindCount; kind++)
		{
			for (int index = 0; index < RunEquipmentSystem.SlotCountOf(kind); index++)
			{
				BagCell cell = new BagCell(this, BagCellKind.BodySlot, index, activeSlotIndex,
					BodySlotName(kind, index), kind, new Vector2(SlotCellWidth, SlotCellHeight));
				slotCells.Add(cell);
				slotGrid.AddChild(cell);
			}
		}

		column.AddChild(SectionLabel(HandColumnTitle));
		HBoxContainer handRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		handRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(handRow);
		for (int hand = 0; hand < handCells.Length; hand++)
		{
			BagCell cell = new BagCell(this, BagCellKind.Hand, hand, activeSlotIndex, HandCellName(activeSlotIndex, hand),
				-1, new Vector2(HandCellWidth, HandCellHeight));
			handCells[hand] = cell;
			handRow.AddChild(cell);
		}
	}

	private static Label SectionLabel(string text)
	{
		Label label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 15);
		label.AddThemeColorOverride("font_color", new Color("f0b27a"));
		return label;
	}

	// ── 刷新（按当刻存档重画；打开 / 拖动 / 闸门变化时调用） ─────────────

	private void RefreshLoad()
	{
		if (loadLabel == null)
		{
			return;
		}

		RunSaveData run = Run;
		if (run == null)
		{
			loadLabel.Text = string.Empty;
			return;
		}

		float load = RunBagSystem.TotalLoad(run);
		float limit = RunBagSystem.LoadLimit;
		bool overloaded = load > limit;
		loadLabel.Text = $"{LoadLabelPrefix}{load:0.0} / {limit:0.0}{(overloaded ? "（超载）" : string.Empty)}";
		loadLabel.AddThemeColorOverride("font_color", overloaded ? new Color("ff8a80") : new Color("cfe3ef"));
	}

	private void RefreshCharacterTabs()
	{
		for (int i = 0; i < characterTabs.Count; i++)
		{
			Button button = characterTabs[i];
			if (button == null || !GodotObject.IsInstanceValid(button))
			{
				continue;
			}

			button.Text = CharacterTabText(i);
			button.ButtonPressed = i == activeSlotIndex;
			button.Disabled = i >= SlotCount;
		}
	}

	private void RefreshBagColumn()
	{
		RunSaveData run = Run;
		List<RunBagEntrySave> entries = CollectBagEquipment(run);
		bagTotalEntries = entries.Count;
		bagPage = BagPageMath.ClampPage(bagPage, entries.Count);
		int firstIndex = BagPageMath.FirstIndexOn(bagPage, entries.Count);
		bagFilledCount = BagPageMath.CountOn(bagPage, entries.Count);

		for (int i = 0; i < bagSlotCells.Length; i++)
		{
			BagCell cell = bagSlotCells[i];
			if (cell == null || !GodotObject.IsInstanceValid(cell))
			{
				continue;
			}

			RunBagEntrySave entry = i < bagFilledCount ? entries[firstIndex + i] : null;
			cell.InstanceId = entry?.InstanceId ?? string.Empty;
			cell.SetFilled(entry != null);
			cell.SetContent(
				entry == null ? string.Empty : entry.Count > 1 ? $"{entry.DefinitionId} ×{entry.Count}" : entry.DefinitionId,
				entry == null ? string.Empty : EntryDetail(entry),
				entry == null ? EmptyCellColor : new Color("d8e4ec"));
		}

		if (pageLabel != null)
		{
			pageLabel.Text = BagPageMath.PageText(bagPage, entries.Count);
		}

		if (previousPageButton != null)
		{
			previousPageButton.Disabled = !CanGoPreviousPage;
		}

		if (nextPageButton != null)
		{
			nextPageButton.Disabled = !CanGoNextPage;
		}
	}

	private void RefreshSlotColumn()
	{
		foreach (BagCell cell in slotCells)
		{
			if (cell == null || !GodotObject.IsInstanceValid(cell))
			{
				continue;
			}

			cell.SlotIndex = activeSlotIndex;
			string definitionId = RunEquipmentSystem.BodySlotDefinition(Run, activeSlotIndex, cell.SlotKind, cell.Index);
			bool filled = !string.IsNullOrWhiteSpace(definitionId);
			cell.SetFilled(filled);
			if (filled)
			{
				DescribeBodyEquipment(definitionId, out string title, out string detail);
				cell.SetContent(title, detail, new Color("d8e4ec"));
			}
			else
			{
				// 空位显示部位名 + `空`（案 §二 / §九 第 6 条）
				cell.SetContent(SlotCaption(cell.SlotKind, cell.Index), EmptyCellText, EmptyCellColor);
			}
		}

		for (int hand = 0; hand < handCells.Length; hand++)
		{
			BagCell cell = handCells[hand];
			if (cell == null || !GodotObject.IsInstanceValid(cell))
			{
				continue;
			}

			cell.SlotIndex = activeSlotIndex;
			string definitionId = RunEquipmentSystem.HandDefinition(Run, activeSlotIndex, hand);
			bool filled = !string.IsNullOrWhiteSpace(definitionId);
			cell.SetFilled(filled);
			cell.SetContent(
				filled ? definitionId : RunEquipmentSystem.HandLabel(hand),
				filled ? HandDetail(definitionId) : EmptyCellText,
				filled ? new Color("d8e4ec") : EmptyCellColor);
		}
	}

	private void RefreshHint()
	{
		if (hintLabel != null)
		{
			hintLabel.Text = lastHint.Length > 0 ? lastHint : DefaultHintText;
		}
	}

	/// <summary>顶部横幅：闸门有原因时常驻说明；规则拒绝时显示「落点被拒：原因」；闸门一变旧拒绝即过期。</summary>
	private void RefreshBanner()
	{
		if (bannerPanel == null || bannerLabel == null)
		{
			return;
		}

		string block = Session?.BagArrangeBlockReason ?? string.Empty;
		if (!string.Equals(block, bannerGateReason, StringComparison.Ordinal))
		{
			bannerGateReason = block;
			lastRejected = false;
		}

		string text;
		Color color;
		if (block.Length > 0)
		{
			text = BagArrangeGate.DescribeRestriction(block);
			color = new Color("ffcc80");
		}
		else if (lastRejected)
		{
			text = DropRejectedPrefix + lastHint;
			color = new Color("ffb4a2");
		}
		else
		{
			text = string.Empty;
			color = new Color("ffb4a2");
		}

		bannerLabel.Text = text;
		bannerLabel.AddThemeColorOverride("font_color", color);
		bannerPanel.Visible = text.Length > 0;
	}

	/// <summary>背包里的**装备类目**条目（部位格 / 手位的拖动来源；随身格上的不算 —— 先取回背包）。</summary>
	private static List<RunBagEntrySave> CollectBagEquipment(RunSaveData run)
	{
		List<RunBagEntrySave> entries = new List<RunBagEntrySave>();
		if (run?.BagEntries == null)
		{
			return entries;
		}

		foreach (RunBagEntrySave entry in run.BagEntries)
		{
			if (entry != null && entry.CategoryEnum == BagCategory.Equipment && entry.IsInBag)
			{
				entries.Add(entry);
			}
		}

		return entries;
	}

	/// <summary>背包装备格副文案：单 / 双手 + 单件负荷（与背包界面同一口径）。</summary>
	private static string EntryDetail(RunBagEntrySave entry)
	{
		string hands = ItemNameResolver.HandsRequiredOf(entry.DefinitionKey) >= 2 ? "双手" : "单手";
		return $"{hands} · 负荷 {ItemNameResolver.LoadOf(BagCategory.Equipment, entry.DefinitionKey):0.0}";
	}

	/// <summary>手位格副文案：单 / 双手 + 单件负荷。</summary>
	private static string HandDetail(string definitionId)
	{
		if (!ItemNameResolver.TryGetEquipmentKey(definitionId, out int key))
		{
			return "未在装备表里";
		}

		string hands = ItemNameResolver.HandsRequiredOf(key) >= 2 ? "双手" : "单手";
		return $"{hands} · 负荷 {ItemNameResolver.LoadOf(BagCategory.Equipment, key):0.0}";
	}

	/// <summary>部位格文案：`装备名` + `负荷 x.x · 防御 N · 伤害 +N · 移动 +N`（按 `Armor.csv` 的列）。</summary>
	private static void DescribeBodyEquipment(string definitionId, out string title, out string detail)
	{
		title = definitionId ?? string.Empty;
		detail = string.Empty;
		if (string.IsNullOrWhiteSpace(definitionId))
		{
			return;
		}

		if (!ItemNameResolver.TryGetArmorDefinition(definitionId, out ArmorDefinition armor) || armor == null)
		{
			detail = "未在部位装备表里";
			return;
		}

		List<string> parts = new List<string> { $"负荷 {armor.Load:0.0}" };
		if (armor.DefenseValue != 0)
		{
			parts.Add($"防御 {armor.DefenseValue}");
		}

		if (armor.DamageBonus != 0)
		{
			parts.Add($"伤害 +{armor.DamageBonus}");
		}

		if (armor.MoveBonus != 0)
		{
			parts.Add($"移动 +{armor.MoveBonus}");
		}

		detail = string.Join(" · ", parts);
	}

	/// <summary>空格子的名字颜色（与背包界面同一支暗色占位）。</summary>
	private static readonly Color EmptyCellColor = new Color("5b6a76");

	// ── 拖动载荷与落点（案 §三；鼠标与烟测共用同一条通路） ─────────────

	/// <summary>取该格的拖动载荷：空格 / 越界 / 没有进行中的本局一律空串（= 不可拖）。拿起恒可用。</summary>
	public string PayloadOf(BagCell cell)
	{
		if (cell == null || Run == null)
		{
			return string.Empty;
		}

		return cell.Kind switch
		{
			BagCellKind.BagEntry => string.IsNullOrWhiteSpace(cell.InstanceId) ? string.Empty : BagPayload(cell.InstanceId),
			BagCellKind.Hand => string.IsNullOrWhiteSpace(RunEquipmentSystem.HandDefinition(Run, cell.SlotIndex, cell.Index))
				? string.Empty
				: HandPayload(cell.SlotIndex, cell.Index),
			BagCellKind.BodySlot => string.IsNullOrWhiteSpace(
					RunEquipmentSystem.BodySlotDefinition(Run, cell.SlotIndex, cell.SlotKind, cell.Index))
				? string.Empty
				: BodySlotPayload(cell.SlotKind, cell.Index),
			_ => string.Empty,
		};
	}

	/// <summary>
	/// 目标能不能接这个载荷（拖动高亮 + `_CanDropData`）：只判「来源 → 目标」是否成套，
	/// 具体合法性（部位 / 双手 / 饰品满 / 闸门）由 `RunSession` 的落点判定给原因，界面不重复规则。
	/// **口径 2026-10-02**：闸门拦下的落点也要能落（否则 Godot 会直接吞掉这次拖动，横幅没地方显示）。
	/// </summary>
	public bool CanAccept(BagCell target, string payload)
	{
		if (target == null || Run == null || string.IsNullOrWhiteSpace(payload))
		{
			return false;
		}

		string source = payload.Split('|')[0];
		return target.Kind switch
		{
			BagCellKind.BagEntry => source is "hand" or "bodyslot",
			BagCellKind.BodySlot => source is "bag" or "bodyslot",
			BagCellKind.Hand => source is "bag" or "hand",
			_ => false,
		};
	}

	/// <summary>
	/// 落点（鼠标拖动与烟测共用）：调 `RunSession` 对应的整理 API，成功 / 失败都写提示行，成功后重画。
	/// 失败原因一律来自规则层（`RunEquipmentSystem.*Error`），界面只做展示。
	/// </summary>
	public bool ApplyDrop(BagCell target, string payload)
	{
		RunSession session = Session;
		if (session?.Current == null || target == null || string.IsNullOrWhiteSpace(payload))
		{
			return false;
		}

		string[] parts = payload.Split('|');
		string source = parts[0];
		bool ok;
		string error;

		switch (target.Kind)
		{
			case BagCellKind.BagEntry:
				if (source == "hand")
				{
					ok = session.TryUnequipHandToBag(ParsePart(parts, 1), ParsePart(parts, 2), out error);
				}
				else if (source == "bodyslot")
				{
					ok = session.TryUnequipSlotToBag(activeSlotIndex, ParsePart(parts, 1), ParsePart(parts, 2), out error);
				}
				else
				{
					ok = false;
					error = "它已经在背包里了。";
				}
				break;
			case BagCellKind.BodySlot:
				if (source == "bag")
				{
					ok = session.TryEquipBagEntryToSlot(parts[1], target.SlotIndex, target.SlotKind, target.Index, out error);
				}
				else if (source == "bodyslot")
				{
					ok = session.TrySwapBodySlots(target.SlotIndex, ParsePart(parts, 1), ParsePart(parts, 2),
						target.SlotKind, target.Index, out error);
				}
				else
				{
					ok = false;
					error = RunEquipmentSystem.BodySlotMismatchError;
				}
				break;
			default:
				if (source == "bag")
				{
					ok = session.TryEquipBagEntryToHand(parts[1], target.SlotIndex, target.Index, out error);
				}
				else if (source == "hand")
				{
					ok = session.TryMoveHandToHand(target.SlotIndex, ParsePart(parts, 1), target.Index, out error);
				}
				else
				{
					ok = false;
					error = RunEquipmentSystem.BodySlotMismatchError;
				}
				break;
		}

		lastRejected = !ok;
		lastHint = ok ? MovedHintPrefix + DescribePayload(payload) : error;
		Refresh();
		return ok;
	}

	/// <summary>提示行里那件装备的名字（按载荷来源取当刻存档里的名字）。</summary>
	private static string DescribePayload(string payload)
	{
		string[] parts = (payload ?? string.Empty).Split('|');
		if (parts.Length == 0 || Run == null)
		{
			return "装备";
		}

		switch (parts[0])
		{
			case "bag":
				RunBagEntrySave entry = RunBagSystem.Find(Run, parts.Length > 1 ? parts[1] : string.Empty);
				return entry?.DefinitionId ?? "装备";
			case "bodyslot":
				return "已搬运的部位装备";
			default:
				return RunEquipmentSystem.HandText(Run, ParsePart(parts, 1), ParsePart(parts, 2));
		}
	}

	private static int ParsePart(string[] parts, int index) =>
		index >= 0 && index < parts.Length && int.TryParse(parts[index], out int value) ? value : -1;

	private static BagCell FindCell(Node node, string name)
	{
		if (node is BagCell cell && string.Equals(cell.Name.ToString(), name, StringComparison.Ordinal))
		{
			return cell;
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			BagCell found = FindCell(node.GetChild(i), name);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}
}
