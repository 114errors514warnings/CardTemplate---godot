using Godot;
using CardSimulator;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class CardBattleScene : Control
{
	private enum PileViewType { Draw, Discard, Exhaust }

	private sealed class UnitViewRefs
	{
		public UnitInstanceView Root { get; }
		public IUnitInstance Unit { get; }
		public bool IsPlayer { get; }
		public UnitViewRefs(UnitInstanceView root, IUnitInstance unit, bool isPlayer)
		{ Root = root; Unit = unit; IsPlayer = isPlayer; }
	}

	[Export] public PackedScene CardDisplayScene;
	[Export] public Godot.Collections.Array<int> InitialCharacterIds = new() { 1002 };
	[Export] public Godot.Collections.Array<int> InitialMonsterIds = new() { 3001, 3002, 3003 };
	[Export] public bool AutoStartBattle = false;
	[Export] public PackedScene SetupWindowScene;
	[Export] public PackedScene DebugPanelScene;
	[Export] public PackedScene UnitViewScene;
	[Export] public int MaxPlayerSlots = 3;
	[Export] public int MaxMonsterSlots = 3;
	[Export] public int MaxUnitsPerRow = 3;
	[Export] public int PilePopupColumns = 4;

	private BattleSytem battle;
	private Label turnLabel;
	private PanelContainer playersPanel;
	private PanelContainer monstersPanel;
	private GridContainer playersRow;
	private GridContainer monstersRow;
	private ScrollContainer monsterScroll;
	private Label currentHandOwnerLabel;
	private HBoxContainer handCardsContainer;
	private readonly Stack<Control> cardPool = new();
	private Control handCardsViewport;
	
	private VBoxContainer handPlayerTabs;
	private Button drawPileButton;
	private Button discardPileButton;
	private Label handPlayerLabel;
	private Label energyLabel;
	private Button exhaustPileButton;
	
	private Button endTurnButton;
	private Control pileOverlay;
	private HBoxContainer pileTabButtons;
	private Label pileTitleLabel;
	private GridContainer pileCardsGrid;
	private Label pileEmptyLabel;
	private PanelContainer stateTooltip;
	private RichTextLabel stateTooltipLabel;
	private Control dragLayer;
	private Line2D dragArrow;
	private Button setupWindowButton;
	private Button debugPanelButton;
	private CanvasLayer windowLayer;
	private Control floatLayer;
	private Control setupWindow;
	private Control debugPanelWindow;
	private bool pendingExternalRefresh;
	// 当前结算高亮的怪物 id（刷新重建单位面板后需重放放大）。
	private int highlightedMonsterUniqueId = -1;
	private bool isBannerActive;
	private Control handPanelRoot;
	private readonly List<int> initialMonsterOrder = new();
	private readonly Dictionary<int, UnitViewRefs> unitViews = new();
	private readonly Dictionary<ulong, (CharacterInstance owner, Card card)> cardViewMap = new();
	private int selectedHandPlayerIndex = 0;
	private int selectedUnitUniqueInGameId = -1;
	private int popupPlayerUniqueInGameId = -1;
	private PileViewType currentPileViewType = PileViewType.Draw;
	private CharacterInstance draggedOwner;
	private Card draggedCard;
	private Control draggedCardNode;
	private int draggedCardOriginalIndex;
	private bool dragExitedHandArea;
	private IUnitInstance hoveredDropTarget;
	private bool DragActive => draggedCard != null;
	private static readonly Color ArrowDefault = Colors.White;
	private static readonly Color ArrowTarget = new Color(1, 0.2f, 0.2f);

	public override void _Ready()
	{
		// 强制重读状态 CSV（不依赖 LoadingSystem.stateCache 启动时缓存）：
		// 开发期间改通用State.csv 后，重启 Godot 即可看到新 Name / EffectDescription。
		LoadingSystem.ReloadStates();

		CardDisplayScene ??= GD.Load<PackedScene>("res://Scenes/Card/CardDisplayPrefab.tscn");
		SetupWindowScene ??= GD.Load<PackedScene>("res://Scenes/UI/BattleSetupWindow.tscn");
		DebugPanelScene ??= GD.Load<PackedScene>("res://Scenes/UI/BattleDebugPanelWindow.tscn");
		UnitViewScene ??= GD.Load<PackedScene>("res://Scenes/UI/UnitInstanceView.tscn");
		battle = GetNodeOrNull<BattleSytem>("BattleSytem");
		turnLabel = GetNodeOrNull<Label>("MainMargin/MainVBox/TopBar/TurnLabel");
		playersPanel = GetNodeOrNull<PanelContainer>("MainMargin/MainVBox/ArenaRow/PlayersPanel");
		monstersPanel = GetNodeOrNull<PanelContainer>("MainMargin/MainVBox/ArenaRow/MonstersPanel");
		playersRow = GetNodeOrNull<GridContainer>("MainMargin/MainVBox/ArenaRow/PlayersPanel/Margin/VBox/PlayersRow");
		monstersRow = GetNodeOrNull<GridContainer>("MainMargin/MainVBox/ArenaRow/MonstersPanel/Margin/VBox/MonstersRow");
		// 仅怪物较多时用 ScrollContainer 兜底：只包怪物网格，角色区与其他 UI 保持原布局尺寸不变
		WrapUnitGridInScroll(monstersRow);
		monsterScroll = monstersRow != null ? monstersRow.GetParent() as ScrollContainer : null;
		currentHandOwnerLabel = GetNodeOrNull<Label>("MainMargin/MainVBox/BottomRow/HandPanel/Margin/VBox/HeaderRow/CurrentHandOwnerLabel");
		handCardsContainer = GetNodeOrNull<HBoxContainer>("MainMargin/MainVBox/BottomRow/HandPanel/Margin/VBox/ContentRow/HandCardsViewport/HandCards");
		handCardsViewport = GetNodeOrNull<Control>("MainMargin/MainVBox/BottomRow/HandPanel/Margin/VBox/ContentRow/HandCardsViewport");
		if (handCardsViewport != null) handCardsViewport.CustomMinimumSize = new Vector2(0, 270);
		
		handPlayerTabs = GetNodeOrNull<VBoxContainer>("MainMargin/MainVBox/BottomRow/HandPanel/Margin/VBox/ContentRow/HandPlayerTabs");
		drawPileButton = GetNodeOrNull<Button>("MainMargin/MainVBox/BottomRow/LeftButtons/DrawPileButton");
		discardPileButton = GetNodeOrNull<Button>("MainMargin/MainVBox/BottomRow/RightButtons/DiscardPileButton");
		exhaustPileButton = GetNodeOrNull<Button>("MainMargin/MainVBox/BottomRow/RightButtons/ExhaustPileButton");
		handPlayerLabel = GetNodeOrNull<Label>("MainMargin/MainVBox/BottomRow/LeftButtons/HandPlayerLabel");
		energyLabel = GetNodeOrNull<Label>("MainMargin/MainVBox/BottomRow/LeftButtons/EnergyLabel");
		
		endTurnButton = GetNodeOrNull<Button>("MainMargin/MainVBox/BottomRow/HandPanel/Margin/VBox/HeaderRow/EndTurnButton");
		handPanelRoot = GetNodeOrNull<Control>("MainMargin/MainVBox/BottomRow/HandPanel");
		pileOverlay = GetNodeOrNull<Control>("PileOverlay");
		pileTabButtons = GetNodeOrNull<HBoxContainer>("PileOverlay/Window/Content/Header/PileTabButtons");
		pileTitleLabel = GetNodeOrNull<Label>("PileOverlay/Window/Content/PileTitle");
		pileCardsGrid = GetNodeOrNull<GridContainer>("PileOverlay/Window/Content/PileScroll/PileCardsGrid");
		pileEmptyLabel = GetNodeOrNull<Label>("PileOverlay/Window/Content/PileEmptyLabel");
		stateTooltip = GetNodeOrNull<PanelContainer>("StateTooltip");
		stateTooltipLabel = GetNodeOrNull<RichTextLabel>("StateTooltip/TooltipText");
		dragLayer = GetNodeOrNull<Control>("DragLayer");
		dragArrow = GetNodeOrNull<Line2D>("DragLayer/DragArrow");
		setupWindowButton = GetNodeOrNull<Button>("MainMargin/MainVBox/TopBar/SetupWindowButton");
		debugPanelButton = GetNodeOrNull<Button>("MainMargin/MainVBox/TopBar/DebugPanelButton");
		windowLayer = GetNodeOrNull<CanvasLayer>("WindowLayer");
		EnsureAuxiliaryWindows(); EnsureSetupDataInitialized();
		if (!AutoStartBattle) { battle?.RefreshBattleInfoDisplay(); ShowSetupWindow(); }
		else { initialMonsterOrder.Clear(); initialMonsterOrder.AddRange(BuildInitialMonsterIds()); battle?.OnInit(BuildInitialCharacterIds(), BuildInitialMonsterIds()); }
		RefreshAllUi(); BindUiEvents(); ApplyArenaPanelStyle();
		CreateFloatLayer();
		BattleSytem.OnDamageApplied += ShowDamageNumberOnUnit;
		BattleSytem.OnPlayerTurnStart += ShowPlayerTurnBanner;
		BattleSytem.OnMonsterTurnStart += ShowMonsterTurnBanner;
		BattleSytem.OnMonsterIntentionHighlight += SetMonsterHighlight;
	}

	public override void _Process(double delta)
	{
		if (pendingExternalRefresh)
		{
			pendingExternalRefresh = false;
			SyncMonsterOrderFromBattle();
			RefreshAllUi();
			if (battle != null && battle.IsBattleStarted) HideSetupWindow();
		}
		UpdateTooltipPosition();
		UpdateDragState();
	}

	private void SyncMonsterOrderFromBattle()
	{
		if (battle == null || !battle.IsBattleStarted || battle.Monsters == null || battle.Monsters.Count == 0)
		{
			if (initialMonsterOrder.Count == 0) return;
			initialMonsterOrder.Clear();
			return;
		}
		initialMonsterOrder.Clear();
		initialMonsterOrder.AddRange(GetOrderedMonsters().Select(m => m.UniqueInGameId));
	}

	public void RequestExternalUiRefresh() { pendingExternalRefresh = true; }

	public void ShowDamageNumberOnUnit(IUnitInstance unit, int damage)
	{
		if (unit == null || damage <= 0 || floatLayer == null) return;

		Vector2 screenPos;
		if (unitViews.TryGetValue(unit.UniqueInGameId, out var view) && view?.Root != null)
		{
			screenPos = view.Root.GlobalPosition + view.Root.Size * new Vector2(0.5f, 0.07f);
		}
		else
		{
			screenPos = GetViewportRect().Size * new Vector2(0.5f, 0.35f);
		}

		Label floatingLabel = new Label();
		floatingLabel.Text = damage.ToString();
		floatingLabel.HorizontalAlignment = HorizontalAlignment.Center;
		floatingLabel.VerticalAlignment = VerticalAlignment.Center;
		floatingLabel.AddThemeFontSizeOverride("font_size", 36);
		floatingLabel.AddThemeColorOverride("font_color", Colors.White);
		floatingLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		floatingLabel.AddThemeConstantOverride("outline_size", 2);
		floatingLabel.Position = screenPos - floatLayer.GlobalPosition - floatingLabel.Size / 2;

		floatLayer.AddChild(floatingLabel);

		Tween tween = floatingLabel.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(floatingLabel, "position", floatingLabel.Position + new Vector2(0, -70), 0.8f);
		tween.TweenProperty(floatingLabel, "modulate", new Color(1, 1, 1, 0), 0.8f);
		tween.Finished += () =>
		{
			if (floatingLabel != null && IsInstanceValid(floatingLabel))
				floatingLabel.QueueFree();
		};
	}


	private void CreateFloatLayer()
	{
		floatLayer = new Control();
		floatLayer.MouseFilter = Control.MouseFilterEnum.Ignore;
		floatLayer.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(floatLayer);
	}

	// ── Input ──────────────────────────────────────────
	public override void _Input(InputEvent e)
	{
		if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			TryStartDragFromPosition(mb.Position);
		if (e is InputEventMouseMotion && DragActive)
			UpdateDragState();
		if (e is InputEventMouseButton mu && !mu.Pressed && mu.ButtonIndex == MouseButton.Left && DragActive)
		{
			if (dragExitedHandArea)
			{
				IUnitInstance target = FindHoveredTarget();
				if (target != null) hoveredDropTarget = target;
				CompleteCardDrag();
			}
			else CancelCardDrag();
		}
	}

	// ── 鼠标拾取卡牌 ──────────────────────────────────
	private void TryStartDragFromPosition(Vector2 position)
	{
		if (!CanDrag()) return;
		foreach (Node child in handCardsContainer.GetChildren())
		{
			if (child is Control ctrl && ctrl.GetGlobalRect().HasPoint(position))
			{
				if (cardViewMap.TryGetValue(ctrl.GetInstanceId(), out var entry))
				{ OnHandCardGuiInput(entry.owner, entry.card); return; }
			}
		}
	}
	private bool CanDrag() => !DragActive && handCardsContainer != null && battle != null && battle.IsPlayerTurn;

	private void OnHandCardGuiInput(CharacterInstance owner, Card card) { StartCardDrag(owner, card); }

	private void StartCardDrag(CharacterInstance owner, Card card)
	{
		if (DragActive) CancelCardDrag();
		draggedOwner = owner; draggedCard = card; dragExitedHandArea = false; hoveredDropTarget = null;
		// 将卡牌本体从手牌区移到 DragLayer
		foreach (Node child in handCardsContainer.GetChildren())
		{
			if (child is Control ctrl && cardViewMap.TryGetValue(ctrl.GetInstanceId(), out var entry) && entry.card == card)
			{
				draggedCardNode = ctrl;
				draggedCardOriginalIndex = ctrl.GetIndex();
				handCardsContainer.RemoveChild(ctrl);
				dragLayer.AddChild(ctrl);
				ctrl.Modulate = new Color(1, 1, 1, 0.92f);
				break;
			}
		}
		}

	// ── 拖动状态 ──────────────────────────────────────
	private void UpdateDragState()
	{
		if (!DragActive || draggedCardNode == null) return;
		Vector2 mousePos = GetGlobalMousePosition();
		Rect2 handArea = GetHandAreaGlobalRect();
		dragExitedHandArea = !handArea.HasPoint(mousePos);

		if (dragExitedHandArea)
		{
			bool needsTarget = draggedCard != null && draggedCard.NeedTarget;
			if (needsTarget)
			{
				float centerX = GetViewportRect().Size.X / 2;
				draggedCardNode.Position = new Vector2(centerX - draggedCardNode.Size.X / 2, 20);
				UpdateDragArrow(mousePos);
			}
			else
			{
				if (dragArrow != null) dragArrow.Visible = false;
				draggedCardNode.Position = mousePos - draggedCardNode.Size / 2;
			}
		}
		else
		{
			if (dragArrow != null) dragArrow.Visible = false;
			draggedCardNode.Position = mousePos - draggedCardNode.Size / 2;
		}
	}

	private void UpdateDragArrow(Vector2 mousePos)
	{
		if (dragArrow == null) return;
		Vector2 from = new Vector2(GetViewportRect().Size.X / 2, 20 + 80); // 卡牌底部
		Vector2 to = mousePos;
		IUnitInstance target = FindHoveredTarget();
		dragArrow.DefaultColor = target != null ? ArrowTarget : ArrowDefault;
		dragArrow.Points = new Vector2[] { from, to };
		dragArrow.Visible = true;
	}

	private IUnitInstance FindHoveredTarget()
	{
		Vector2 mousePos = GetGlobalMousePosition();
		foreach (var kvp in unitViews)
		{
			if (kvp.Value?.Root == null || kvp.Value.IsPlayer) continue;
			if (kvp.Value.Unit.HP <= 0) continue;
			Rect2 rect = kvp.Value.Root.GetGlobalRect();
			if (rect.HasPoint(mousePos)) return kvp.Value.Unit;
		}
		return null;
	}

	// ── 完成/取消拖动 ────────────────────────────────
	private void CompleteCardDrag()
	{
		if (draggedCard == null || draggedOwner == null || battle == null) { CancelDragCleanup(); return; }
		if (draggedCard.NeedTarget && hoveredDropTarget == null) { CancelDragCleanup(); return; }
		bool ok = battle.PlayHandCard(draggedOwner, draggedCard, hoveredDropTarget);
		draggedOwner = null; draggedCard = null;
		if (ok)
		{
			ReturnDragCardToPool();
			RefreshAllUi();
		}
		else
		{
			CancelDragCleanup();
			RefreshAllUi();
		}
		dragExitedHandArea = false; hoveredDropTarget = null;
		if (dragArrow != null) dragArrow.Visible = false;
	}

	private void CancelCardDrag() { CancelDragCleanup(); }

	private void CancelDragCleanup()
	{
		draggedOwner = null; draggedCard = null;
		// 将卡牌本体归还到手牌区原位
		if (draggedCardNode != null && dragLayer != null && handCardsContainer != null)
		{
			dragLayer.RemoveChild(draggedCardNode);
			draggedCardNode.Modulate = Colors.White;
			handCardsContainer.AddChild(draggedCardNode);
			int idx = Mathf.Clamp(draggedCardOriginalIndex, 0, handCardsContainer.GetChildCount() - 1);
			handCardsContainer.MoveChild(draggedCardNode, idx);
		}
		draggedCardNode = null;
		dragExitedHandArea = false; hoveredDropTarget = null;
		if (dragArrow != null) dragArrow.Visible = false;
		}

	// ── UI 绑定 ──────────────────────────────────────
	private void BindUiEvents()
	{
		if (setupWindowButton != null) setupWindowButton.Pressed += ToggleSetupWindow;
		if (debugPanelButton != null) debugPanelButton.Pressed += ToggleDebugPanelWindow;
		if (endTurnButton != null) endTurnButton.Pressed += OnEndTurnPressed;
		if (drawPileButton != null) drawPileButton.Pressed += () => OpenPilePopup(PileViewType.Draw);
		if (discardPileButton != null) discardPileButton.Pressed += () => OpenPilePopup(PileViewType.Discard);
		if (exhaustPileButton != null) exhaustPileButton.Pressed += () => OpenPilePopup(PileViewType.Exhaust);
		// 牌堆弹窗关闭
		var closeBtn = GetNodeOrNull<Button>("PileOverlay/Window/Content/Header/CloseButton");
		if (closeBtn != null) closeBtn.Pressed += ClosePilePopup;
	}

	private void EnsureAuxiliaryWindows()
	{
		if (SetupWindowScene != null && setupWindow == null && windowLayer != null)
		{
			setupWindow = SetupWindowScene.Instantiate<Control>();
			windowLayer.AddChild(setupWindow);
			BindWindowCloseButton(setupWindow);
		}
		if (DebugPanelScene != null && debugPanelWindow == null && windowLayer != null)
		{
			debugPanelWindow = DebugPanelScene.Instantiate<Control>();
			windowLayer.AddChild(debugPanelWindow);
			BindWindowCloseButton(debugPanelWindow);
		}
	}
	private void EnsureSetupDataInitialized()
	{
		if (battle != null && battle.SetupData == null)
		{
			battle.SetupData = new BattleSetupData();
			foreach (var cid in InitialCharacterIds.Where(id => id > 0)) battle.SetupData.AddCharacterId(cid);
			foreach (var mid in InitialMonsterIds.Where(id => id > 0)) battle.SetupData.AddMonsterId(mid);
		}
	}
	private void BindWindowCloseButton(Control window)
	{
		if (window == null) return;
		// 按钮放在 window 的父节点上（与 window 同级），避免 PanelContainer 内部布局系统撑开按钮
		Node parent = window.GetParent();
		if (parent == null) return;
		var closeBtn = new Button();
		closeBtn.Text = "×";
		closeBtn.Flat = true;
		closeBtn.CustomMinimumSize = new Vector2(32, 32);
		closeBtn.Size = new Vector2(32, 32);
		closeBtn.AddThemeFontSizeOverride("font_size", 18);
		closeBtn.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
		closeBtn.Pressed += () => window.Visible = false;
		parent.AddChild(closeBtn);
		// 同步可见性
		closeBtn.Visible = window.Visible;
		window.TreeExiting += () => closeBtn.QueueFree();
		window.VisibilityChanged += () => { closeBtn.Visible = window.Visible; RepositionCloseButton(window, closeBtn); };
		// 初始定位 + 窗口尺寸变化时重定位
		RepositionCloseButton(window, closeBtn);
		window.Resized += () => RepositionCloseButton(window, closeBtn);
	}
	private void RepositionCloseButton(Control window, Button closeBtn)
	{
		closeBtn.Position = window.Position + new Vector2(window.Size.X - 40, 12);
	}

	public void ToggleSetupWindow() { if (setupWindow != null) setupWindow.Visible = !setupWindow.Visible; }
	public void ToggleDebugPanelWindow() { if (debugPanelWindow != null) debugPanelWindow.Visible = !debugPanelWindow.Visible; }
	public void ShowSetupWindow() { if (setupWindow != null) setupWindow.Visible = true; }
	public void HideSetupWindow() { if (setupWindow != null) setupWindow.Visible = false; }
	private void OnEndTurnPressed() { if (battle != null && battle.IsPlayerTurn) battle.EndPlayerTurn(); }

	// ── UI 刷新 ──────────────────────────────────────
	private void RefreshAllUi()
	{
		ClampSelectedHandPlayerIndex(); RefreshTurnLabel(); RefreshUnitAreas(); RefreshHandTabs();
		RefreshHandCards(); RefreshPileButtons(); RefreshMonsterIntentions(); UpdateEndTurnButtonState();
	}
	private void RefreshMonsterIntentions()
	{
		foreach (var kvp in unitViews)
		{
			if (kvp.Value?.Root != null && kvp.Value.Unit is MonsterInstance)
				kvp.Value.Root.RefreshIntentionDisplay();
		}
	}
	private void RefreshTurnLabel()
	{
		if (turnLabel == null || battle == null) return;
		if (!battle.IsBattleStarted) { turnLabel.Text = "\u5F53\u524D\u9636\u6BB5\uFF1A\u672A\u5F00\u59CB"; return; }
		turnLabel.Text = battle.IsPlayerTurn ? "\u73A9\u5BB6\u56DE\u5408" : "\u654C\u4EBA\u56DE\u5408";
	}

	// ── 单位区域 ──────────────────────────────────────
	private void RefreshUnitAreas()
	{
		if (playersRow == null || monstersRow == null) return;
		unitViews.Clear();
		ClearChildren(playersRow); ClearChildren(monstersRow);
		var players = GetAllOrderedPlayers();
		int playerSlotCount = Math.Max(MaxPlayerSlots, players.Count);
		int playerColumns = Math.Max(1, Math.Min(MaxUnitsPerRow, playerSlotCount));
		ApplyUnitAreaColumns(playersRow, playerColumns);
		ApplyUnitAreaScale(playersRow, playerSlotCount, playerColumns);
		for (int i = 0; i < playerSlotCount; i++)
		{
			if (i < players.Count)
			{
				CharacterInstance player = players[i];
				if (player.HP > 0)
				{
					playersRow.AddChild(CreateUnitPanel(player, true, player.Name));
				}
				else
				{
					var deadSlot = CreateEmptyUnitSlot(player.Name);
					deadSlot.ShowDeadOverlay();
					playersRow.AddChild(deadSlot);
				}
			}
			else
			{
				playersRow.AddChild(CreateEmptyUnitSlot($"\u89D2\u8272\u69FD\u4F4D {i + 1}"));
			}
		}
		List<MonsterInstance> orderedMonsters = GetOrderedMonsters();
		int actualMonsterCount = orderedMonsters.Count;
		int monsterSlotCount = Math.Max(MaxMonsterSlots, actualMonsterCount);
		int monsterColumns = GetAdaptiveMonsterColumns(monsterSlotCount);
		ApplyUnitAreaColumns(monstersRow, monsterColumns);
		ApplyUnitAreaScale(monstersRow, monsterSlotCount, monsterColumns);
		for (int mi = 0; mi < monsterSlotCount; mi++)
		{
			if (mi < actualMonsterCount)
			{
				MonsterInstance monster = orderedMonsters[mi];
				if (monster.HP > 0)
				{
					monstersRow.AddChild(CreateUnitPanel(monster, false, monster.Name));
				}
				else
				{
					var deadSlot = CreateEmptyUnitSlot(monster.Name);
					deadSlot.ShowDeadOverlay();
					monstersRow.AddChild(deadSlot);
				}
			}
			else
			{
				monstersRow.AddChild(CreateEmptyUnitSlot($"怪物槽位 {mi + 1}"));
			}
		}

		// 多行布局时压缩单位内部元素，避免最小尺寸撑破视口
		CompactMonsterRows(monstersRow, monsterSlotCount, monsterColumns);
		// 刷新后怪物区滚动回顶部，避免滚动位置残留
		if (monsterScroll != null)
		{
			monsterScroll.ScrollVertical = 0;
		}

		// 刷新重建面板后，若当前有结算高亮中的怪物，重新对其放大，保证高亮持续。
		if (highlightedMonsterUniqueId != -1 && unitViews.TryGetValue(highlightedMonsterUniqueId, out var highlightedView) && highlightedView?.Root != null)
		{
			highlightedView.Root.SetHighlighted(true);
		}
	}

	private static void ApplyUnitAreaColumns(GridContainer grid, int columns)
	{
		if (grid == null) return;
		int clamped = Math.Max(1, columns);
		if (grid.Columns != clamped) grid.Columns = clamped;
	}

	// 固定画布内：不做整行视觉 Scale（避免双重缩小/模糊），
	// 尺寸控制交给 CompactMonsterRows 的最小尺寸收敛 + ScrollContainer 兜底。
	private void ApplyUnitAreaScale(GridContainer grid, int slotCount, int columns)
	{
		if (grid == null) return;
		grid.Scale = Vector2.One;
		grid.PivotOffset = grid.Size * 0.5f;
	}

	private static void WrapUnitGridInScroll(GridContainer grid)
	{
		if (grid == null || grid.GetParent() is ScrollContainer)
		{
			return;
		}

		Node parent = grid.GetParent();
		if (parent == null)
		{
			return;
		}

		int index = grid.GetIndex();
		parent.RemoveChild(grid);

		ScrollContainer scroll = new ScrollContainer
		{
			Name = grid.Name + "Scroll",
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
		};
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;

		parent.AddChild(scroll);
		int insertIndex = Mathf.Clamp(index, 0, Math.Max(0, parent.GetChildCount() - 1));
		parent.MoveChild(scroll, insertIndex);
		scroll.AddChild(grid);
		grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
	}

	private static int GetAdaptiveMonsterColumns(int slotCount)
	{
		if (slotCount <= 3) return 3;
		if (slotCount <= 6) return 3;
		// 7+：尽量两行（5 列 × 2 行最多容纳 10 只）
		return 5;
	}

	private void CompactMonsterRows(GridContainer grid, int slotCount, int columns)
	{
		if (grid == null || columns <= 0) return;
		int rows = (slotCount + columns - 1) / columns;
		if (rows <= 1) return;

		// 固定画布内：尽量一屏两行以内；尺寸受宽/高双约束，
		// 下限 0.5 保证可读，极少数超出的部分交给 ScrollContainer 滚动兜底。
		const float areaWidth = 660f;
		const float areaHeight = 400f;
		float widthFactor = columns * 210f > 0 ? areaWidth / (columns * 210f) : 1f;
		float heightFactor = rows * 360f > 0 ? areaHeight / (rows * 360f) : 1f;
		float factor = Mathf.Clamp(Mathf.Min(widthFactor, heightFactor), 0.5f, 1f);
		foreach (Node child in grid.GetChildren())
		{
			if (child is UnitInstanceView view)
			{
				ShrinkUnitPanelContents(view, factor);
			}
		}
	}

	private static void ShrinkUnitPanelContents(UnitInstanceView view, float factor)
	{
		if (view == null || factor <= 0f || factor >= 0.99f) return;

		view.CustomMinimumSize = new Vector2(210f * factor, 360f * factor);
		view.Size = view.CustomMinimumSize;

		Control portrait = view.GetNodeOrNull<Control>("Margin/Body/PortraitCenter/Portrait");
		if (portrait != null)
		{
			portrait.CustomMinimumSize = new Vector2(100f * factor, 170f * factor);
			portrait.Size = portrait.CustomMinimumSize;
		}

		Control intention = view.GetNodeOrNull<Control>("Margin/Body/IntentionLabel");
		if (intention != null)
		{
			intention.CustomMinimumSize = new Vector2(180f * factor, 120f * factor);
			intention.Size = intention.CustomMinimumSize;
		}

		Control stateCenter = view.GetNodeOrNull<Control>("Margin/Body/StateCenter");
		if (stateCenter != null)
		{
			stateCenter.CustomMinimumSize = new Vector2(0f, 40f * factor);
		}

		MarginContainer margin = view.GetNodeOrNull<MarginContainer>("Margin");
		if (margin != null)
		{
			int m = Mathf.Max(2, (int)(10f * factor));
			margin.AddThemeConstantOverride("margin_left", m);
			margin.AddThemeConstantOverride("margin_top", m);
			margin.AddThemeConstantOverride("margin_right", m);
			margin.AddThemeConstantOverride("margin_bottom", m);
		}

		VBoxContainer body = view.GetNodeOrNull<VBoxContainer>("Margin/Body");
		if (body != null)
		{
			body.AddThemeConstantOverride("separation", Mathf.Max(2, (int)(10f * factor)));
		}
	}
	private UnitInstanceView CreateUnitPanel(IUnitInstance unit, bool isPlayer, string title)
	{
		var root = UnitViewScene.Instantiate<UnitInstanceView>();
		root.GuiInput += ev => OnUnitPanelGuiInput(unit, ev);
		root.Bind(unit, isPlayer, ShowStateTooltip, HideStateTooltip);
		unitViews[unit.UniqueInGameId] = new UnitViewRefs(root, unit, isPlayer);
		return root;
	}
	private UnitInstanceView CreateEmptyUnitSlot(string title)
	{
		var root = UnitViewScene.Instantiate<UnitInstanceView>();
		root.BindPlaceholder(title);
		return root;
	}
	private void OnUnitPanelGuiInput(IUnitInstance unit, InputEvent ev)
	{
		if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			selectedUnitUniqueInGameId = unit.UniqueInGameId;
	}

	// ── 手牌 ──────────────────────────────────────────
	private void RefreshHandTabs()
	{
		if (handPlayerTabs == null) return;
		ClearChildren(handPlayerTabs);
		var players = GetOrderedPlayers();
		if (players.Count <= 1) { handPlayerTabs.Visible = false; return; }
		handPlayerTabs.Visible = true;
		for (int i = 0; i < players.Count; i++)
		{
			int idx = i; var btn = new Button();
			btn.Text = players[i].Name; btn.Flat = true;
			btn.Pressed += () => { selectedHandPlayerIndex = idx; RefreshHandCards(); RefreshPileButtons(); };
			handPlayerTabs.AddChild(btn);
		}
	}
	private Control GetPooledCard()
	{
		if (cardPool.Count > 0) { var c = cardPool.Pop(); c.Visible = true; return c; }
		return CardDisplayScene.Instantiate<Control>();
	}
	private void ReturnToPool(Control cv)
	{
		if (cv == null) return;
		cv.Visible = false;
		cardPool.Clear(); // 简单策略：隐藏并丢弃池中旧卡，等重建
		cv.GetParent()?.RemoveChild(cv);
		cv.QueueFree();
	}
	private void ReturnDragCardToPool()
	{
		if (draggedCardNode != null)
		{
			draggedCardNode.Visible = false;
			draggedCardNode.GetParent()?.RemoveChild(draggedCardNode);
			draggedCardNode.QueueFree();
		}
		draggedCardNode = null;
	}

	private void RefreshHandCards()
	{
		if (handCardsContainer == null) return;
		ClearChildren(handCardsContainer);
			
		ClearChildren(handCardsContainer);
		cardViewMap.Clear();
		var players = GetOrderedPlayers();
		if (players.Count == 0) return;
		int idx = Mathf.Clamp(selectedHandPlayerIndex, 0, players.Count - 1);
		var player = players[idx];
		if (currentHandOwnerLabel != null) currentHandOwnerLabel.Text = "";
		if (handPlayerLabel != null) handPlayerLabel.Text = $"当前\n{player.Name}";
		if (energyLabel != null) energyLabel.Text = $"{player.costs}/{player.Max_costs}";
		foreach (var card in player.handcards)
		{
			if (card == null) continue;
			var cv = GetPooledCard();
			if (cv is CardDisplayPrefab p) p.SyncFromCard(card);
			// CustomMinimumSize 只是下限，必须直接设 Size 才能改实际尺寸
			cv.Size = new Vector2(195, 270);
			cv.CustomMinimumSize = new Vector2(195, 270);
			cv.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
			cv.SizeFlagsVertical = SizeFlags.ShrinkBegin;
			// 缩小内部节点防止撑大父控件
			var content = cv.GetNodeOrNull<ColorRect>("CardFrame/Content");
			if (content != null) { content.Size = new Vector2(195, 270); content.CustomMinimumSize = new Vector2(195, 270); }
			var desc = cv.GetNodeOrNull<RichTextLabel>("CardFrame/Content/Margin/Body/DescriptionLabel");
			if (desc != null) { desc.CustomMinimumSize = new Vector2(0, 180); }
			// 缩小卡牌名称和类型字号
			var nameLbl = cv.GetNodeOrNull<Label>("CardFrame/Content/Margin/Body/NameLabel");
			if (nameLbl != null) nameLbl.AddThemeFontSizeOverride("font_size", 17);
			var typeLbl = cv.GetNodeOrNull<Label>("CardFrame/Content/Margin/Body/MetaRow/TypeLabel");
			if (typeLbl != null) typeLbl.AddThemeFontSizeOverride("font_size", 12);
			var costLbl = cv.GetNodeOrNull<Label>("CardFrame/Content/Margin/Body/MetaRow/CostLabel");
			if (costLbl != null) costLbl.AddThemeFontSizeOverride("font_size", 12);
			cv.AddThemeStyleboxOverride("panel", MakeCardBorderStyle());
			cv.GuiInput += (ev) =>
			{
				if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && CanDrag())
					OnHandCardGuiInput(player, card);
			};
			cardViewMap[cv.GetInstanceId()] = (player, card);
			handCardsContainer.AddChild(cv);
		}
		// 卡牌超出时缩进间距
		AdjustCardSpacing();
	}
	private void AdjustCardSpacing()
	{
		if (handCardsContainer == null || handCardsViewport == null) return;
		float cardWidth = 195;
		float spacing = 10;
		int cardCount = handCardsContainer.GetChildCount();
		float viewportWidth = handCardsViewport.Size.X;
		if (cardCount > 1 && cardCount * (cardWidth + spacing) > viewportWidth)
			spacing = Math.Max(4, (viewportWidth - cardCount * cardWidth) / (cardCount - 1));
		handCardsContainer.AddThemeConstantOverride("separation", (int)spacing);
	}

	// ── 牌堆弹窗 ──────────────────────────────────────
	private void RefreshPileButtons()
	{
		if (drawPileButton == null) return;
		var players = GetOrderedPlayers(); if (players.Count == 0) return;
		var p = players[Mathf.Clamp(selectedHandPlayerIndex, 0, players.Count - 1)];
		drawPileButton.Text = $"\u62BD\u724C\u5806 ({p.drawpile.Count})";
		if (discardPileButton != null) discardPileButton.Text = $"\u5F03\u724C\u5806 ({p.discardpile.Count})";
		if (exhaustPileButton != null) exhaustPileButton.Text = $"\u6D88\u8017\u724C\u5806 ({p.ExhaustPile.Count})";
	}
	private void OpenPilePopup(PileViewType type)
	{
		currentPileViewType = type;
		if (pileOverlay != null) pileOverlay.Visible = true;
		RefreshPilePopup();
	}
	private void ClosePilePopup() { if (pileOverlay != null) pileOverlay.Visible = false; }
	private void RefreshPilePopup()
	{
		if (pileCardsGrid == null || pileEmptyLabel == null || pileTitleLabel == null) return;
		ClearChildren(pileCardsGrid);
		var players = GetOrderedPlayers(); if (players.Count == 0) return;
		int idx = Mathf.Clamp(selectedHandPlayerIndex, 0, players.Count - 1);
		var player = players[idx];
		List<Card> cards = currentPileViewType switch
		{
			PileViewType.Draw => player.drawpile.ToList(),
			PileViewType.Discard => player.discardpile.ToList(),
			PileViewType.Exhaust => player.ExhaustPile.ToList(),
			_ => new List<Card>(),
		};
		string title = currentPileViewType switch
		{
			PileViewType.Draw => "\u62BD\u724C\u5806",
			PileViewType.Discard => "\u5F03\u724C\u5806",
			PileViewType.Exhaust => "\u6D88\u8017\u724C\u5806",
			_ => "\u724C\u5806",
		};
		pileTitleLabel.Text = $"{title} - {player.Name}";
		pileEmptyLabel.Visible = cards.Count == 0;
		if (cards.Count > 0 && pileCardsGrid.Columns != PilePopupColumns) pileCardsGrid.Columns = PilePopupColumns;
		foreach (var card in cards)
		{
			if (card == null) continue;
			var cv = GetPooledCard();
			if (cv is CardDisplayPrefab p) p.SyncFromCard(card);
			pileCardsGrid.AddChild(cv);
		}
	}

	// ── 状态提示 ──────────────────────────────────────
	private void UpdateTooltipPosition()
	{
		if (stateTooltip == null || !stateTooltip.Visible) return;
		Vector2 mp = GetGlobalMousePosition();
		stateTooltip.Position = mp + new Vector2(20, 20);
	}
	private void ShowStateTooltip(StateType stateType, int stacks)
	{
		if (stateTooltipLabel == null) return;
		string stateName = GetStateDisplayName(stateType);
		string effect = LoadingSystem.StateDictionary.TryGetValue(stateType, out var def) && !string.IsNullOrWhiteSpace(def.EffectDescription)
			? def.EffectDescription
			: $"{stateName}\u6548\u679C\u4EE5\u5F53\u524D\u89C4\u5219\u5B9E\u73B0\u4E3A\u51C6";
		stateTooltipLabel.Text = $"{stateName}\n{effect}";
		if (stateTooltip != null) stateTooltip.Visible = true;
	}
	private void HideStateTooltip() { if (stateTooltip != null) stateTooltip.Visible = false; }
	private void UpdateEndTurnButtonState() { if (endTurnButton != null && battle != null) endTurnButton.Disabled = !battle.IsPlayerTurn || !battle.IsBattleStarted; }

	// ── 工具方法 ──────────────────────────────────────
	private void ClampSelectedHandPlayerIndex()
	{
		var players = GetOrderedPlayers();
		if (players.Count > 0) selectedHandPlayerIndex = Mathf.Clamp(selectedHandPlayerIndex, 0, players.Count - 1);
	}
	private List<CharacterInstance> GetOrderedPlayers()
	{
		if (battle == null || battle.Players == null) return new();
		return battle.Players.Values.Where(p => p.HP > 0).OrderBy(p => p.UniqueInGameId).ToList();
	}
	private List<CharacterInstance> GetAllOrderedPlayers()
	{
		if (battle == null || battle.Players == null) return new();
		return battle.Players.Values.OrderBy(p => p.UniqueInGameId).ToList();
	}
	private List<MonsterInstance> GetOrderedMonsters()
	{
		if (battle == null || battle.Monsters == null) return new();
		return battle.Monsters.Values.OrderBy(m => m.UniqueInGameId).ToList();
	}
	private List<int> BuildInitialCharacterIds()
	{
		var ids = InitialCharacterIds.Where(id => id > 0).ToList();
		if (ids.Count == 0 && battle?.SetupData != null) ids = battle.SetupData.GetCharacterIdList();
		if (ids.Count == 0) ids.Add(1002);
		return ids;
	}
	private List<int> BuildInitialMonsterIds()
	{
		var ids = InitialMonsterIds.Where(id => id > 0).ToList();
		if (ids.Count == 0 && battle?.SetupData != null) ids = battle.SetupData.GetMonsterIdList();
		if (ids.Count == 0) ids.Add(3001);
		return ids;
	}
	private Rect2 GetHandAreaGlobalRect()
	{
		if (handCardsViewport != null) return new Rect2(handCardsViewport.GlobalPosition, handCardsViewport.Size);
		return new Rect2(0, 0, 1200, 270);
	}
	private void ApplyArenaPanelStyle()
	{
		playersPanel?.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		monstersPanel?.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
	}
		private static StyleBoxFlat MakeCardBorderStyle()
	{
		var sb = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = new Color(0.22f, 0.22f, 0.22f) };
		sb.SetBorderWidthAll(2); sb.SetCornerRadiusAll(4);
		sb.ContentMarginLeft = 0; sb.ContentMarginTop = 0; sb.ContentMarginRight = 0; sb.ContentMarginBottom = 0;
		return sb;
	}

	private static void ClearChildren(Node node) { if (node == null) return; foreach (Node child in node.GetChildren()) child.QueueFree(); }
	private static string GetStateDisplayName(StateType stateType)
	{
		if (LoadingSystem.StateDictionary.TryGetValue(stateType, out var def) && !string.IsNullOrWhiteSpace(def.Name))
		{
			return def.Name;
		}
		return stateType.ToString();
	}

	// ── 回合切换横幅 ──────────────────────────────────────
	private void ShowPlayerTurnBanner() => _ = ShowTurnBannerAsync("\u5DF2\u65B9\u56DE\u5408");
	private void ShowMonsterTurnBanner() => _ = ShowTurnBannerAsync("\u654C\u65B9\u56DE\u5408");

	private async System.Threading.Tasks.Task ShowTurnBannerAsync(string text, double holdDuration = 1.0)
	{
		if (floatLayer == null) return;
		Control banner = BuildTurnBannerNode(text);
		floatLayer.AddChild(banner);
		isBannerActive = true;
		if (handPanelRoot != null) handPanelRoot.MouseFilter = Control.MouseFilterEnum.Ignore;

		await ToSignal(GetTree().CreateTimer(holdDuration), SceneTreeTimer.SignalName.Timeout);

		// 淡出
		Tween fadeOut = banner.CreateTween();
		fadeOut.TweenProperty(banner, "modulate:a", 0.0f, 0.2);
		await ToSignal(fadeOut, Tween.SignalName.Finished);

		isBannerActive = false;
		if (handPanelRoot != null) handPanelRoot.MouseFilter = Control.MouseFilterEnum.Stop;
		banner.QueueFree();
	}

	private Control BuildTurnBannerNode(string text)
	{
		var root = new Control { Name = "TurnBanner" };
		root.MouseFilter = Control.MouseFilterEnum.Stop;
		root.SetAnchorsPreset(LayoutPreset.FullRect);
		root.Modulate = new Color(1, 1, 1, 0);

		var bg = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
		bg.Name = "Bg";
		bg.MouseFilter = Control.MouseFilterEnum.Ignore;
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		root.AddChild(bg);

		var label = new Label { Text = text };
		label.Name = "Label";
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeFontSizeOverride("font_size", 96);
		label.AddThemeColorOverride("font_color", Colors.White);
		label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
		label.SetAnchorsPreset(LayoutPreset.FullRect);
		root.AddChild(label);

		return root;
	}

	// ── 怪物结算高亮（结算时图片放大） ──────────────────
	public void SetMonsterHighlight(int uniqueInGameId, bool on)
	{
		highlightedMonsterUniqueId = on ? uniqueInGameId : (highlightedMonsterUniqueId == uniqueInGameId ? -1 : highlightedMonsterUniqueId);
		if (unitViews.TryGetValue(uniqueInGameId, out var view) && view?.Root != null)
		{
			view.Root.SetHighlighted(on);
		}
	}
}
