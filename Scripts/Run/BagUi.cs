// BagUi.cs
// 运行局**背包界面**（《背包系统交互案》§一 / §二 / §三，2026-10-02 批 E）：
//   入口在常驻顶栏的**时间点行**里（`时间点` 文案的右边、「结束当天」之前，见 RunFlowScene.BuildGlobalTopBar）；
//   界面挂 `RunUiLayers.Modal`，单实例开关，`关闭` / `Esc` 退出，关闭不改任何状态。
//
// 结构（交互案 §二）：左 = 背包格列表（页签过滤 + 负荷条），右 = **可拖放目标** ——
//   装备栏（当前角色的左手 / 右手位）与道具栏（队伍共享的随身 3 格）。两处槽位数量与战斗内一致，不引入第二套模型。
//
// 拖动（交互案 §三 + 2026-10-02 用户口径改判）：**拿起恒可用**（只要本局进行中就能拖起来），
//   被闸门拦下的是**落点** —— 内容进行中放进道具栏 / 装备栏会被拒绝，并由顶部**横幅**给出原因。
// 落点全部走 `RunSession` 上的同一批 API（`TryMoveBagEntryToCarrySlot` / `TryEquipBagEntryToHand` / …），
//   因此鼠标拖动与烟测的 `SimulateDrop` 用的是同一条通路，界面不自带规则。
//
// 格区（用户口径 2026-10-02 第 2 条）：**固定 5 × 5 = 25 格**的均匀网格（空位也画格子），
//   分页算术在纯逻辑 `BagPageMath` 里；网格下面一行显示「第 x / y 页」与 `上一页` / `下一页`。
using Godot;
using System;
using System.Collections.Generic;

public partial class BagUi : Node, IDragHost
{
	public const string TitleText = "背包";
	public const string CloseText = "关闭";
	public const string AllTabText = "全部";
	public const string CarryColumnTitle = "道具栏（随身 3 格）";
	public const string EquipmentColumnTitle = "装备栏（左手 / 右手）";
	public const string EmptyCellText = "空";
	public const string LoadLabelPrefix = "负荷 ";
	public const string DefaultHintText = "拖动整理：道具 / 材料 / 食物 → 道具栏；装备 → 左手 / 右手位；拖回背包即卸下。";
	public const string MovedHintPrefix = "已移动：";
	public const string OverloadedHintText = "已超载：奖励照常入账，但新的拖入会被拒绝，先整理或消耗。";

	/// <summary>落点被规则拒绝时的横幅前缀（部位不符 / 双手冲突 / 负荷不足 / 许可通道…）。</summary>
	public const string DropRejectedPrefix = "落点被拒：";

	/// <summary>每页格数（均匀网格 5 列 × 5 行；算术在 `BagPageMath`）。</summary>
	public const int PageCapacity = BagPageMath.PageCapacity;

	public const string PreviousPageText = "上一页";
	public const string NextPageText = "下一页";

	/// <summary>
	/// 网格格的统一尺寸（用户口径 2026-10-02：格区是**均匀网格**、**物品格用正方形**）。
	/// 宽高必须相等；5 行 × 96 + 4 × 8 行距 = 512，加标题 / 页签 / 翻页 / 提示行后在 1600 × 900 基准分辨率下不滚动。
	/// </summary>
	public const int BagCellWidth = 96;
	public const int BagCellHeight = BagCellWidth;

	/// <summary>页签（`null` = 全部；其余 = 单个类别，背包系统交互案 §二）。</summary>
	private static readonly (string Label, BagCategory? Category)[] TabDefinitions =
	{
		(AllTabText, null),
		("材料", BagCategory.Material),
		("道具", BagCategory.Item),
		("装备", BagCategory.Equipment),
		("食物", BagCategory.Food),
	};

	private CanvasLayer modalLayer;
	private Control root;
	private Button closeButton;
	private readonly List<Button> tabButtons = new List<Button>();
	private readonly List<Button> characterTabs = new List<Button>();
	private Label loadLabel;
	private Label hintLabel;
	private GridContainer bagColumn;
	private readonly BagCell[] bagSlotCells = new BagCell[PageCapacity];
	private readonly BagCell[] carryCells = new BagCell[RunBagSystem.CarryItemSlotCount];
	private readonly BagCell[] handCells = new BagCell[RunEquipmentSystem.HandCount];
	private Button previousPageButton;
	private Button nextPageButton;
	private Label pageLabel;
	private PanelContainer bannerPanel;
	private Label bannerLabel;

	private BagCategory? activeTab;
	private int activeSlotIndex;

	/// <summary>当前页（0 基；越界一律由 `BagPageMath.ClampPage` 夹回）。</summary>
	private int bagPage;

	/// <summary>当前页**有内容**的格数（`BagCellCount` 的读数；空白格不算）。</summary>
	private int bagFilledCount;

	/// <summary>当前页签下的条目总数（翻页夹取与文案用）。</summary>
	private int bagTotalEntries;

	/// <summary>最近一次拖动结果的提示（成功 / 失败原因）；打开界面时清空。</summary>
	private string lastHint = string.Empty;

	/// <summary>最近一次拖动是否被拒（横幅用它区分「规则拒绝」与「演示口径说明」）。</summary>
	private bool lastRejected;

	/// <summary>横幅上一次渲染时的闸门原因：闸门状态一变，旧的「落点被拒」就过期（可能正是它造成的）。</summary>
	private string bannerGateReason = string.Empty;

	private static RunSession Session => RunSession.Instance;
	private static RunSaveData Run => RunSession.Instance?.Current;

	/// <summary>界面上正在显示的角色槽位数（= `CharacterSlots.Count`，越界一律夹到这个范围内）。</summary>
	private int SlotCount => Run == null ? 0 : Run.CharacterSlots.Count;

	/// <summary>界面是否打开（单实例判据）。</summary>
	public bool IsOpen => root != null && GodotObject.IsInstanceValid(root) && root.Visible;

	/// <summary>
	/// 落点是否被闸门拦下（内容进行中 / 没有进行中的本局）。
	/// **注意口径**：这只禁「放进槽位」，不禁「拿起」（2026-10-02 用户口径）。
	/// </summary>
	public bool IsArrangeBlocked => Session?.Current == null || !string.IsNullOrEmpty(Session.BagArrangeBlockReason);

	/// <summary>`IsArrangeBlocked` 的旧名（只读→落点受限的改名过渡，语义同上）。</summary>
	public bool IsReadOnly => IsArrangeBlocked;


	/// <summary>负荷行文案（烟测断言「数值随拖动变化」用）。</summary>
	public string LoadText => loadLabel?.Text ?? string.Empty;

	/// <summary>提示行文案（成功 / 失败原因 / 落点受限原因）。</summary>
	public string HintText => hintLabel?.Text ?? string.Empty;

	/// <summary>顶部横幅文案（空串 = 横幅隐藏）。</summary>
	public string BannerText => bannerPanel != null && bannerPanel.Visible ? bannerLabel?.Text ?? string.Empty : string.Empty;

	/// <summary>当前页**有内容**的格数（烟测断言过滤口径用）。</summary>
	public int BagCellCount => bagFilledCount;

	/// <summary>网格里实际画出的格子数（固定 = `PageCapacity`：空位也画格）。</summary>
	public int BagSlotCellCount
	{
		get
		{
			int count = 0;
			foreach (BagCell cell in bagSlotCells)
			{
				if (cell != null && GodotObject.IsInstanceValid(cell))
				{
					count++;
				}
			}

			return count;
		}
	}

	/// <summary>页码文案（`第 x / y 页`）。</summary>
	public string PageText => pageLabel?.Text ?? string.Empty;

	/// <summary>网格第 `index` 格的主文案（空格子 = 空串；烟测断言「空格不写「空」」用）。</summary>
	public string BagSlotText(int index)
	{
		BagCell cell = SlotCellAt(index);
		return cell?.TitleText ?? string.Empty;
	}

	/// <summary>网格第 `index` 格的实际布出尺寸（烟测断言「物品格是正方形」用；界面没开 = 零向量）。</summary>
	public Vector2 BagSlotSize(int index) => SlotCellAt(index)?.Size ?? Vector2.Zero;

	private BagCell SlotCellAt(int index) =>
		index >= 0 && index < bagSlotCells.Length && GodotObject.IsInstanceValid(bagSlotCells[index])
			? bagSlotCells[index]
			: null;

	/// <summary>当前页（1 基，界面口径）。</summary>
	public int PageNumber => bagPage + 1;

	/// <summary>总页数（0 件 = 1 页）。</summary>
	public int PageCount => BagPageMath.PageCountOf(bagTotalEntries);

	/// <summary>是否还能翻页（烟测断言按钮可用性）。</summary>
	public bool CanGoPreviousPage => BagPageMath.HasPrevious(bagPage, bagTotalEntries);
	public bool CanGoNextPage => BagPageMath.HasNext(bagPage, bagTotalEntries);

	// ── AI 接口访问面（2026-10-02）：给 `PlayerApiRun` 一个与鼠标操作等价的窄口 ──────────
	// 口径：这里的方法都只是「把界面上的那一次点击写出来」，不新增任何规则。

	/// <summary>当前页签（null = 全部）。</summary>
	public BagCategory? ActiveTab => activeTab;

	/// <summary>当前页签的显示名（`全部` / `材料` / …），供 API 回读。</summary>
	public string ActiveTabLabel => TabLabelOf(activeTab);

	/// <summary>当前正在看的角色槽位（装备栏归属）。</summary>
	public int ActiveSlotIndex => activeSlotIndex;

	/// <summary>页签显示名（`null` → `全部`）。</summary>
	public static string TabLabelOf(BagCategory? category)
	{
		foreach ((string label, BagCategory? value) in TabDefinitions)
		{
			if (value == category)
			{
				return label;
			}
		}

		return category?.ToString() ?? AllTabText;
	}

	/// <summary>解析页签参数：接受显示名（`全部` / `材料` / `道具` / `装备` / `食物`）或枚举名（`all` / `material` / …）。</summary>
	public static bool TryParseTab(string text, out BagCategory? category)
	{
		category = null;
		if (string.IsNullOrWhiteSpace(text) || string.Equals(text, AllTabText, StringComparison.Ordinal)
			|| string.Equals(text, "all", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		foreach ((string label, BagCategory? value) in TabDefinitions)
		{
			if (value != null && (string.Equals(text, label, StringComparison.Ordinal)
				|| string.Equals(text, value.Value.ToString(), StringComparison.OrdinalIgnoreCase)))
			{
				category = value;
				return true;
			}
		}

		return false;
	}

	/// <summary>切换页签（与点页签按钮同一条通路：换页签回到第 1 页）。界面未打开返回 false。</summary>
	public bool SelectTab(string text)
	{
		if (!IsOpen || !TryParseTab(text, out BagCategory? category))
		{
			return false;
		}

		activeTab = category;
		bagPage = 0;
		Refresh();
		return true;
	}

	/// <summary>切换装备栏归属的角色页签（与点角色 Tab 同一条通路）。</summary>
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
	/// AI 接口：按「格名」取一次拖动（`fromCell` 的画面内容 → `toCell`）——
	/// 与鼠标拖动共用 `ApplyDrop`，所以 API 能覆盖到的规则就是玩家真能动到的规则。
	/// </summary>
	public bool DragCell(string fromCell, string toCell)
	{
		string payload = PayloadOfCell(fromCell);
		return payload.Length > 0 && SimulateDrop(payload, toCell);
	}

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
		Refresh();
	}

	/// <summary>关闭界面（关闭不改任何状态：背包 / 手位 / 随身格的当前值原样保留）。</summary>
	public void Close()
	{
		if (root == null || !GodotObject.IsInstanceValid(root))
		{
			return;
		}

		root.QueueFree();
		root = null;
		Array.Clear(bagSlotCells, 0, bagSlotCells.Length);
		Array.Clear(carryCells, 0, carryCells.Length);
		Array.Clear(handCells, 0, handCells.Length);
		// 页签与角色 Tab 也是**本次面板**的子控件：面板一销毁就必须从列表摘掉，
		// 否则「关 → 再开」几轮后会遍历到已释放的 Button（`ObjectDisposedException`，2026-10-02 装备烟测实测）。
		tabButtons.Clear();
		characterTabs.Clear();
		previousPageButton = null;
		nextPageButton = null;
		pageLabel = null;
		bannerPanel = null;
		bannerLabel = null;
	}

	/// <summary>按当刻存档重画（打开 / 拖动成功 / 落点受限态变化时调用）。</summary>
	public void Refresh()
	{
		if (!IsOpen)
		{
			return;
		}

		BagCategory? tab = activeTab;
		if (activeSlotIndex >= SlotCount)
		{
			activeSlotIndex = 0;
		}

		foreach (Button button in tabButtons)
		{
			button.ButtonPressed = string.Equals(button.Name.ToString(), TabName(tab), StringComparison.Ordinal);
		}

		RefreshLoad();
		RefreshBagColumn(tab);
		RefreshCharacterTabs();
		RefreshSlots();
		RefreshHint();
		RefreshBanner();
	}

	// ── 文案与名字（烟测按名字找控件） ─────────────────────────

	public static string TabName(BagCategory? category) =>
		category == null ? "BagTab_" + AllTabText : $"BagTab_{category.Value}";

	public static string CharacterTabName(int slotIndex) => $"BagCharTab_{slotIndex}";

	public static string BagCellName(int index) => $"BagCell_{index}";

	public static string CarryCellName(int slot) => $"BagCarry_{slot}";

	public static string HandCellName(int slotIndex, int hand) => $"BagHand_{slotIndex}_{hand}";

	/// <summary>翻页控件名（烟测按名找控件用）。</summary>
	public const string PreviousPageName = "BagPrevPage";
	public const string NextPageName = "BagNextPage";
	public const string PageLabelName = "BagPageLabel";

	// ── 翻页（用户口径 2026-10-02 第 2 条；算术在 `BagPageMath`） ─────────────

	/// <summary>下一页（越界自动夹取；界面未打开时无动作）。</summary>
	public void NextPage() => GoToPage(bagPage + 1);

	/// <summary>上一页（越界自动夹取）。</summary>
	public void PreviousPage() => GoToPage(bagPage - 1);

	/// <summary>翻到指定页（烟测与按钮共用）：夹取后重画。</summary>
	public void GoToPage(int page)
	{
		if (!IsOpen)
		{
			return;
		}

		bagPage = BagPageMath.ClampPage(page, bagTotalEntries);
		Refresh();
	}

	// ── 面板构建（交互案 §二 的两栏结构） ─────────────────────

	private void BuildPanel()
	{
		Close();
		root = new Control { Name = "BagPanelRoot", MouseFilter = Control.MouseFilterEnum.Stop };
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		modalLayer.AddChild(root);

		ColorRect dim = new ColorRect { Color = new Color(0, 0, 0, .55f), MouseFilter = Control.MouseFilterEnum.Ignore };
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(dim);

		CenterContainer center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(center);

		// 面板要容下 5 列 × 96 的正方形网格 + 右侧槽位列（330），并在 1600 × 900 基准分辨率下四周留白；
		// 高度交给内容决定（0 = 不设下限），格区改成正方形后纵向需求变了，写死高度会留大片空白。
		PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(920, 0) };
		// 底板用与常驻顶栏同一套深色料（`RunFlowScene.BuildTopBarBackdrop`）：模态要能盖住世界地图 / 战场，
		// 不能沿用默认主题的半透明面板 —— 否则「均匀网格」会与背后的地图格纠缠在一起。
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
		BuildTabRow(vbox);

		HBoxContainer columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 22);
		vbox.AddChild(columns);
		BuildBagColumn(columns);
		BuildArrangeColumn(columns);

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

		closeButton = new Button { Name = "BagClose", Text = CloseText, CustomMinimumSize = new Vector2(96, 34) };
		closeButton.Pressed += Close;
		row.AddChild(closeButton);
	}

	/// <summary>
	/// 顶部**横幅**（用户口径 2026-10-02 第 1 条：拖动被拦下时要给横幅提示）。
	/// 位置固定在标题行下方、页签行上方 —— 打开界面就能看到，不随提示行一起被长文本挤走。
	/// 内容为空的唯一显示条件是「内容进行中」（`RefreshBanner`），此时说明「能拿起 / 不能放进槽位」。
	/// </summary>
	private void BuildBannerRow(Control parent)
	{
		bannerPanel = new PanelContainer { Visible = false, Name = "BagBanner" };
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

	private void BuildTabRow(Control parent)
	{
		HBoxContainer row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);

		foreach ((string label, BagCategory? category) in TabDefinitions)
		{
			BagCategory? captured = category;
			Button button = new Button
			{
				Name = TabName(captured),
				Text = label,
				ToggleMode = true,
				CustomMinimumSize = new Vector2(84, 32),
			};
			button.Pressed += () =>
			{
				activeTab = captured;
				bagPage = 0; // 换页签回到第 1 页（过滤后条目集合完全变了，留在旧页会看到空页）
				Refresh();
			};
			row.AddChild(button);
			tabButtons.Add(button);
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

		Label caption = new Label { Text = "背包（页签只过滤显示，负荷是整包口径）" };
		caption.AddThemeFontSizeOverride("font_size", 15);
		column.AddChild(caption);

		// 均匀网格（用户口径 2026-10-02 第 2 条）：固定 5 × 5 个等宽等高格，**空位也画格子**，
		// 因此不再需要滚动条 —— 翻页取代滚动（`上一页` / `第 x / y 页` / `下一页` 在网格下方一行）。
		bagColumn = new GridContainer { Columns = BagPageMath.Columns, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
		bagColumn.AddThemeConstantOverride("h_separation", 8);
		bagColumn.AddThemeConstantOverride("v_separation", 8);
		column.AddChild(bagColumn);

		for (int i = 0; i < PageCapacity; i++)
		{
			BagCell cell = new BagCell(this, BagCellKind.BagEntry, i, -1, BagCellName(i));
			bagSlotCells[i] = cell;
			bagColumn.AddChild(cell);
		}

		BuildPagerRow(column);
	}

	/// <summary>网格下方的翻页行：`上一页` / `第 x / y 页` / `下一页`（页数来自纯逻辑 `BagPageMath`）。</summary>
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
			Text = BagPageMath.PageText(0, 0),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		pageLabel.AddThemeFontSizeOverride("font_size", 15);
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

	private void BuildArrangeColumn(Control parent)
	{
		VBoxContainer column = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
		column.AddThemeConstantOverride("separation", 6);
		parent.AddChild(column);

		HBoxContainer tabs = new HBoxContainer();
		tabs.AddThemeConstantOverride("separation", 6);
		column.AddChild(tabs);
		for (int i = 0; i < 3; i++)
		{
			int slotIndex = i;
			Button button = new Button { Name = CharacterTabName(i), ToggleMode = true, CustomMinimumSize = new Vector2(104, 30) };
			button.Pressed += () =>
			{
				activeSlotIndex = slotIndex;
				Refresh();
			};
			tabs.AddChild(button);
			characterTabs.Add(button);
		}

		column.AddChild(SectionLabel(CarryColumnTitle));
		HBoxContainer carryRow = new HBoxContainer();
		carryRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(carryRow);
		for (int slot = 0; slot < carryCells.Length; slot++)
		{
			BagCell cell = new BagCell(this, BagCellKind.Carry, slot, -1, CarryCellName(slot));
			carryCells[slot] = cell;
			carryRow.AddChild(cell);
		}

		column.AddChild(SectionLabel(EquipmentColumnTitle));
		HBoxContainer handRow = new HBoxContainer();
		handRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(handRow);
		for (int hand = 0; hand < handCells.Length; hand++)
		{
			BagCell cell = new BagCell(this, BagCellKind.Hand, hand, activeSlotIndex, HandCellName(activeSlotIndex, hand));
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

	// ── 刷新（按当刻存档重画；打开 / 拖动成功 / 只读态变化时调用） ──

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

	private void RefreshBagColumn(BagCategory? tab)
	{
		RunSaveData run = Run;
		List<RunBagEntrySave> entries = CollectEntries(run, tab);
		bagTotalEntries = entries.Count;
		bagPage = BagPageMath.ClampPage(bagPage, entries.Count);
		int firstIndex = BagPageMath.FirstIndexOn(bagPage, entries.Count);
		bagFilledCount = BagPageMath.CountOn(bagPage, entries.Count);

		// 均匀网格：格数是固定的，只换内容 —— 空位画一个暗色 `空` 格（既是视觉占位，也是回背包的合法落点）。
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
			// 空格子**不写文案**（用户口径 2026-10-02：没有物品时中间不显示「空」）——
			// 只留一个暗色空框表达「这是第几格」，位置感由格底承担。
			cell.SetContent(
				entry == null ? string.Empty : DescribeEntry(entry),
				entry == null ? string.Empty : DescribeEntryDetail(entry),
				entry == null ? EmptyCellColor : RarityColor(entry.Rarity));
		}

		RefreshPager();
	}

	/// <summary>当前页签下的条目（`全部` = 材料 / 道具 / 装备 / 食物四类按固定顺序拼接）。</summary>
	private static List<RunBagEntrySave> CollectEntries(RunSaveData run, BagCategory? tab)
	{
		List<RunBagEntrySave> entries = new List<RunBagEntrySave>();
		if (run == null)
		{
			return entries;
		}

		if (tab == null)
		{
			foreach (BagCategory category in new[] { BagCategory.Material, BagCategory.Item, BagCategory.Equipment, BagCategory.Food })
			{
				entries.AddRange(RunBagSystem.EntriesOf(run, category));
			}
		}
		else
		{
			entries.AddRange(RunBagSystem.EntriesOf(run, tab.Value));
		}

		return entries;
	}

	/// <summary>翻页行：页数文案与两个按钮的可用性（只有一页时两边都禁用）。</summary>
	private void RefreshPager()
	{
		if (pageLabel != null)
		{
			pageLabel.Text = BagPageMath.PageText(bagPage, bagTotalEntries);
		}

		if (previousPageButton != null)
		{
			previousPageButton.Disabled = !BagPageMath.HasPrevious(bagPage, bagTotalEntries);
		}

		if (nextPageButton != null)
		{
			nextPageButton.Disabled = !BagPageMath.HasNext(bagPage, bagTotalEntries);
		}
	}

	private void RefreshCharacterTabs()
	{
		for (int i = 0; i < characterTabs.Count; i++)
		{
			Button button = characterTabs[i];
			bool exists = i < SlotCount;
			button.Visible = exists;
			if (!exists)
			{
				continue;
			}

			button.Text = Session?.GetSlotDisplayName(i) ?? $"角色 {i}";
			button.ButtonPressed = i == activeSlotIndex;
		}
	}

	private void RefreshSlots()
	{
		RunSaveData run = Run;

		for (int slot = 0; slot < carryCells.Length; slot++)
		{
			RunBagEntrySave entry = run == null ? null : RunBagSystem.CarrySlotEntry(run, slot);
			BagCell cell = carryCells[slot];
			cell.InstanceId = entry?.InstanceId ?? string.Empty;
			cell.SetFilled(entry != null);
			cell.SetContent(
				entry == null ? EmptyCellText : DescribeEntry(entry),
				entry == null ? "（队伍共享）" : DescribeEntryDetail(entry),
				entry == null ? null : RarityColor(entry.Rarity));
		}

		for (int hand = 0; hand < handCells.Length; hand++)
		{
			BagCell cell = handCells[hand];
			cell.SlotIndex = activeSlotIndex;
			cell.Name = HandCellName(activeSlotIndex, hand);
			string handName = hand == RunEquipmentSystem.LeftHand ? "左手" : "右手";
			string definitionId = run == null ? string.Empty : RunEquipmentSystem.HandDefinition(run, activeSlotIndex, hand);
			string detail = string.IsNullOrWhiteSpace(definitionId)
				? handName
				: $"{handName} · 负荷 {RunEquipmentSystem.HandLoad(definitionId):0.0}";
			cell.SetFilled(!string.IsNullOrWhiteSpace(definitionId));
			cell.SetContent(
				RunEquipmentSystem.HandText(run, activeSlotIndex, hand),
				detail,
				string.IsNullOrWhiteSpace(definitionId) ? null : new Color("cfe3ef"));
		}
	}

	private void RefreshHint()
	{
		if (hintLabel == null)
		{
			return;
		}

		RunSaveData run = Run;
		string block = Session?.BagArrangeBlockReason ?? string.Empty;
		if (block.Length > 0)
		{
			// 口径 2026-10-02：拦的是落点，不是拿起 —— 提示行与顶部横幅都要说清这一点。
			hintLabel.Text = BagArrangeGate.RestrictedHintPrefix + block;
			hintLabel.AddThemeColorOverride("font_color", new Color("ffcc80"));
			return;
		}

		if (run != null && RunBagSystem.IsOverloaded(run))
		{
			hintLabel.Text = OverloadedHintText;
			hintLabel.AddThemeColorOverride("font_color", new Color("ff8a80"));
			return;
		}

		// 颜色按**结果**分：无操作 = 灰（默认说明）、刚被拒 = 红、刚成功 = 绿。
		hintLabel.Text = string.IsNullOrEmpty(lastHint) ? DefaultHintText : lastHint;
		hintLabel.AddThemeColorOverride("font_color",
			string.IsNullOrEmpty(lastHint) ? new Color("8fa1ad") : lastRejected ? new Color("ff8a80") : new Color("a8e6a1"));
	}

	/// <summary>
	/// 顶部横幅（用户口径 2026-10-02 第 1 条）：两种内容二选一，都可能为空（空 = 隐藏）。
	/// ① 内容进行中（闸门有原因）→ 说明「可以拿起、不能放进道具栏 / 装备栏」，常驻显示；
	/// ② 刚刚有一次落点被规则拒绝（部位不符 / 双手冲突 / 负荷不足 / 许可通道…）→ 显示原因。
	/// 拖动成功或闸门解除后横幅自动消失（`Open` 时两个来源都清空）。
	/// </summary>
	private void RefreshBanner()
	{
		if (bannerPanel == null || bannerLabel == null)
		{
			return;
		}

		string block = Session?.BagArrangeBlockReason ?? string.Empty;
		if (!string.Equals(block, bannerGateReason, StringComparison.Ordinal))
		{
			// 闸门状态变了：上一次的「落点被拒」已经过期（内容开始 / 结束都会让旧原因不再成立），先清掉它。
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

	/// <summary>格内主文案：`名字 ×数量`。</summary>
	private static string DescribeEntry(RunBagEntrySave entry) =>
		entry == null ? EmptyCellText : entry.Count > 1 ? $"{entry.DefinitionId} ×{entry.Count}" : entry.DefinitionId;

	/// <summary>格内副文案：类别 · 单件负荷（食物再带剩余有效期）。</summary>
	private static string DescribeEntryDetail(RunBagEntrySave entry)
	{
		if (entry == null)
		{
			return string.Empty;
		}

		string text = $"{CategoryText(entry.CategoryEnum)} · 负荷 {ItemNameResolver.LoadOf(entry.CategoryEnum, entry.DefinitionKey):0.0}";
		if (entry.CategoryEnum == BagCategory.Food)
		{
			text += $" · 剩 {Math.Max(0, entry.ExpireDaysRemaining)} 天";
		}

		return text;
	}

	private static string CategoryText(BagCategory category) => category switch
	{
		BagCategory.Material => "材料",
		BagCategory.Item => "道具",
		BagCategory.Equipment => "装备",
		BagCategory.Food => "食物",
		_ => "物品",
	};

	/// <summary>空格子的名字颜色（暗色占位：看得出是格子，但不与真实物品抢注意力）。</summary>
	private static readonly Color EmptyCellColor = new Color("5b6a76");

	private static Color RarityColor(int rarity) => rarity switch
	{
		2 => new Color("ffd166"),
		1 => new Color("9fd8ff"),
		_ => new Color("d8e4ec"),
	};

	// ── 拖动载荷与落点（交互案 §三；鼠标与烟测共用同一条通路） ──

	/// <summary>载荷：`bag|{实例键}`（从背包格拖出）。</summary>
	public static string BagPayload(string instanceId) => $"bag|{instanceId}";

	/// <summary>载荷：`carry|{格号}`（从道具栏拖出）。</summary>
	public static string CarryPayload(int slot) => $"carry|{slot}";

	/// <summary>载荷：`hand|{角色槽}|{手位}`（从装备栏拖出）。</summary>
	public static string HandPayload(int slotIndex, int hand) => $"hand|{slotIndex}|{hand}";

	/// <summary>
	/// 取该格的拖动载荷：空格 / 越界 / 没有进行中的本局一律返回空串（= 不可拖）。
	/// **口径 2026-10-02**：内容进行中（闸门有原因）时**照样可以拿起** —— 被拦的是落点，
	/// 因此这里不再看 `IsArrangeBlocked`；拒绝原因由落点判定 → 提示行与横幅给出。
	/// </summary>
	public string PayloadOf(BagCell cell)
	{
		if (cell == null || Run == null)
		{
			return string.Empty;
		}

		return cell.Kind switch
		{
			BagCellKind.BagEntry => string.IsNullOrWhiteSpace(cell.InstanceId) ? string.Empty : BagPayload(cell.InstanceId),
			BagCellKind.Carry => string.IsNullOrWhiteSpace(cell.InstanceId) ? string.Empty : CarryPayload(cell.Index),
			_ => string.IsNullOrWhiteSpace(RunEquipmentSystem.HandDefinition(Run, cell.SlotIndex, cell.Index))
				? string.Empty
				: HandPayload(cell.SlotIndex, cell.Index),
		};
	}

	/// <summary>
	/// 目标能不能接这个载荷（拖动过程高亮 + `_CanDropData` 用）：只判「来源 → 目标」是否成套。
	/// 具体合法性（部位 / 双手 / 负荷 / 许可通道 / 闸门）一律由 `RunSession` 的落点判定给出原因，界面不重复规则。
	/// **口径 2026-10-02**：闸门拦下的落点**也要能落**（`_CanDropData` 返回 true），否则 Godot 会直接吞掉这次
	/// 拖动、`_DropData` 根本不会被调用 —— 那样就没有任何地方能显示横幅提示。落点再由 `ApplyDrop` 拒绝 + 给原因。
	/// </summary>
	public bool CanAccept(BagCell target, string payload)
	{
		if (target == null || Run == null || string.IsNullOrWhiteSpace(payload))
		{
			return false;
		}

		string source = payload.Split('|')[0];
		// 背包只接「卸下 / 取回」；道具栏与手位接背包物品与另一侧槽位（合法性由 RunSession 给原因）。
		return target.Kind switch
		{
			BagCellKind.BagEntry => source is "carry" or "hand",
			BagCellKind.Carry => source is "bag" or "hand",
			_ => source is "bag" or "hand",
		};
	}

	/// <summary>
	/// 落点（鼠标拖动与烟测共用）：调 `RunSession` 对应的整理 API，成功 / 失败都写提示行，成功后重画。
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
				if (source == "carry")
				{
					ok = session.TryMoveCarrySlotToBag(ParsePart(parts, 1), out error);
				}
				else if (source == "hand")
				{
					ok = session.TryUnequipHandToBag(ParsePart(parts, 1), ParsePart(parts, 2), out error);
				}
				else
				{
					ok = false;
					error = "它已经在背包里了。";
				}
				break;
			case BagCellKind.Carry:
				if (source == "bag")
				{
					ok = session.TryMoveBagEntryToCarrySlot(parts[1], target.Index, out error);
				}
				else if (source == "hand")
				{
					ok = session.TryMoveHandToCarrySlot(ParsePart(parts, 1), ParsePart(parts, 2), target.Index, out error);
				}
				else
				{
					ok = false;
					error = "同一格，无需移动。";
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
					error = RunEquipmentSystem.SlotMismatchError;
				}
				break;
		}

		lastRejected = !ok;
		lastHint = ok ? MovedHintPrefix + DescribePayload(payload) : error;
		Refresh();
		return ok;
	}

	/// <summary>
	/// 烟测入口：按控件名（`BagCell_i` / `BagCarry_i` / `BagHand_{槽}_{手位}`）找落点并喂一次拖动 ——
	/// 与鼠标拖动走的是同一个 `ApplyDrop`，因此这里能覆盖到的规则就是玩家真能动到的规则。
	/// </summary>
	public bool SimulateDrop(string payload, string targetName)
	{
		if (root == null || !GodotObject.IsInstanceValid(root))
		{
			return false;
		}

		BagCell target = FindCell(root, targetName);
		return target != null && ApplyDrop(target, payload);
	}

	/// <summary>
	/// 烟测入口：按控件名取该格的拖动载荷（空串 = 拿不起来）。
	/// 口径 2026-10-02：内容进行中「能否拿起」也要可断言，所以把 `PayloadOf` 对外的窄口开在这里。
	/// </summary>
	public string PayloadOfCell(string cellName)
	{
		if (root == null || !GodotObject.IsInstanceValid(root))
		{
			return string.Empty;
		}

		BagCell cell = FindCell(root, cellName);
		return cell == null ? string.Empty : PayloadOf(cell);
	}

	private static BagCell FindCell(Node node, string name)
	{
		if (node is BagCell cell && string.Equals(cell.Name.ToString(), name, StringComparison.Ordinal))
		{
			return cell;
		}

		foreach (Node child in node.GetChildren())
		{
			BagCell found = FindCell(child, name);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	private static int ParsePart(string[] parts, int index) =>
		index < parts.Length && int.TryParse(parts[index], out int value) ? value : -1;

	/// <summary>载荷对应的物品名（成功提示用）。</summary>
	private string DescribePayload(string payload)
	{
		string[] parts = payload.Split('|');
		RunSaveData run = Run;
		if (run == null)
		{
			return payload;
		}

		switch (parts[0])
		{
			case "bag":
				RunBagEntrySave entry = RunBagSystem.Find(run, parts[1]);
				return entry?.DefinitionId ?? parts[1];
			case "carry":
				RunBagEntrySave carried = RunBagSystem.CarrySlotEntry(run, ParsePart(parts, 1));
				return carried?.DefinitionId ?? "道具";
			default:
				return RunEquipmentSystem.HandText(run, ParsePart(parts, 1), ParsePart(parts, 2));
		}
	}
}

/// <summary>格子类别：背包格 / 道具栏格 / 装备栏（手位）格 / 部位格（头 · 身 · 脚 · 饰品）。</summary>
public enum BagCellKind
{
	BagEntry = 0,
	Carry = 1,
	Hand = 2,
	BodySlot = 3,
}

/// <summary>
/// 拖动宿主的窄口：背包界面（`BagUi`）与装备界面（`EquipmentUi`）共用同一个格控件 `BagCell`，
/// 因此「怎么取载荷 / 接不接这个落点 / 落点怎么执行」由宿主实现 —— 格控件只把鼠标事件翻译成一次回调。
/// 规则本体不在界面里（两边都通向 `RunSession` 的整理 API），宿主只做类别分流与文案。
/// </summary>
public interface IDragHost
{
	/// <summary>取该格的拖动载荷（空串 = 拿不起来）。</summary>
	string PayloadOf(BagCell cell);

	/// <summary>目标能不能接这个载荷（拖动高亮 + `_CanDropData`；只判「成套」，合法性由规则层给原因）。</summary>
	bool CanAccept(BagCell cell, string payload);

	/// <summary>执行一次落点（鼠标与烟测共用）。</summary>
	bool ApplyDrop(BagCell cell, string payload);
}

/// <summary>
/// 背包界面的一格：既是**拖动源**（`_GetDragData` 返回载荷串）又是**落点**（`_CanDropData` / `_DropData`）。
/// 判定与规则全在 `BagUi` / `RunSession` 侧，本控件只负责把鼠标事件翻译成一次 `ApplyDrop`。
/// </summary>
public partial class BagCell : PanelContainer
{
	private readonly IDragHost owner;
	private readonly Label nameLabel;
	private readonly Label detailLabel;

	public readonly BagCellKind Kind;

	/// <summary>随身格序号 / 手位序号 / 部位格内的格序。</summary>
	public readonly int Index;

	/// <summary>部位格的部位（`EquipmentSlotKind`；其它类别为 -1）。</summary>
	public readonly int SlotKind;

	/// <summary>手位 / 部位格所属角色槽（背包格与随身格为 -1）。</summary>
	public int SlotIndex;

	/// <summary>当前格内条目的实例键（手位 / 部位格按装备名串搬运，这里保持空串）。</summary>
	public string InstanceId = string.Empty;

	public BagCell(IDragHost owner, BagCellKind kind, int index, int slotIndex, string name, int slotKind = -1, Vector2? size = null)
	{
		this.owner = owner;
		Kind = kind;
		Index = index;
		SlotKind = slotKind;
		SlotIndex = slotIndex;
		Name = name;
		CustomMinimumSize = size ?? (kind == BagCellKind.BagEntry
			? new Vector2(BagUi.BagCellWidth, BagUi.BagCellHeight)   // 均匀网格 + 正方形（用户口径 2026-10-02）
			: new Vector2(104, 62));                                   // 右侧道具栏 / 手位：保持原来的窄格
		MouseFilter = MouseFilterEnum.Stop;

		VBoxContainer column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		column.AddThemeConstantOverride("separation", 2);
		AddChild(column);

		nameLabel = new Label
		{
			Text = string.Empty,   // 空格子不写文案（用户口径 2026-10-02），初值也留空
			HorizontalAlignment = HorizontalAlignment.Center,
			ClipText = true,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		nameLabel.AddThemeFontSizeOverride("font_size", 15);
		column.AddChild(nameLabel);

		detailLabel = new Label
		{
			Text = string.Empty,
			HorizontalAlignment = HorizontalAlignment.Center,
			// 正方形格只有 96 px 宽：副行（`类别 · 负荷` / 食物剩余天数）必须能折行，不能裁成省略号。
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		detailLabel.AddThemeFontSizeOverride("font_size", 11);
		detailLabel.AddThemeColorOverride("font_color", new Color("8fa1ad"));
		column.AddChild(detailLabel);
	}

	/// <summary>格内主文案（烟测断言「空格没有字 / 有格有字」用）。</summary>
	public string TitleText => nameLabel?.Text ?? string.Empty;

	/// <summary>写格内文案（名字 + 副行 + 名字颜色）。</summary>
	public void SetContent(string name, string detail, Color? nameColor)
	{
		nameLabel.Text = name ?? string.Empty;
		detailLabel.Text = detail ?? string.Empty;
		if (nameColor.HasValue)
		{
			nameLabel.AddThemeColorOverride("font_color", nameColor.Value);
		}
	}

	/// <summary>
	/// 格底样式：`有内容` = 略亮底 + 亮边，`空` = 暗底 + 暗边（用户指令 2026-10-02 第 2 条：
	/// 格区要看得出来是「均匀网格排列的格子」，只有文案而没有格底是看不出来的）。
	/// 两种样式做成静态共享实例（改一次内容不必每格重造资源）。
	/// </summary>
	public void SetFilled(bool filled)
	{
		if (filledSlot == filled && HasThemeStyleboxOverride("panel"))
		{
			return;
		}

		filledSlot = filled;
		AddThemeStyleboxOverride("panel", filled ? FilledStyle : EmptyStyle);
	}

	private bool filledSlot;

	private static readonly StyleBoxFlat FilledStyle = BuildSlotStyle(new Color("1d2731"), new Color("3b4d5c"));
	private static readonly StyleBoxFlat EmptyStyle = BuildSlotStyle(new Color("141b22"), new Color("232e38"));

	private static StyleBoxFlat BuildSlotStyle(Color background, Color border)
	{
		StyleBoxFlat style = new StyleBoxFlat
		{
			BgColor = background,
			BorderColor = border,
			ContentMarginLeft = 6,
			ContentMarginRight = 6,
			ContentMarginTop = 4,
			ContentMarginBottom = 4,
		};
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		return style;
	}

	public override Variant _GetDragData(Vector2 atPosition)
	{
		string payload = owner?.PayloadOf(this) ?? string.Empty;
		if (string.IsNullOrEmpty(payload))
		{
			return default;
		}

		Label preview = new Label { Text = nameLabel.Text };
		preview.AddThemeColorOverride("font_color", new Color("f5d98c"));
		SetDragPreview(preview);
		return payload;
	}

	public override bool _CanDropData(Vector2 atPosition, Variant data) =>
		owner?.CanAccept(this, data.AsString()) == true;

	public override void _DropData(Vector2 atPosition, Variant data) => owner?.ApplyDrop(this, data.AsString());
}
