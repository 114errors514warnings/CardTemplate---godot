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
	[Export] public int PilePopupColumns = 4;

	private BattleSytem battle;
	private Label turnLabel;
	private PanelContainer playersPanel;
	private PanelContainer monstersPanel;
	private HBoxContainer playersRow;
	private HBoxContainer monstersRow;
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
	private Control windowLayer;
	private Control setupWindow;
	private Control debugPanelWindow;
	private bool pendingExternalRefresh;
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
		CardDisplayScene ??= GD.Load<PackedScene>("res://Scenes/Card/CardDisplayPrefab.tscn");
		SetupWindowScene ??= GD.Load<PackedScene>("res://Scenes/UI/BattleSetupWindow.tscn");
		DebugPanelScene ??= GD.Load<PackedScene>("res://Scenes/UI/BattleDebugPanelWindow.tscn");
		UnitViewScene ??= GD.Load<PackedScene>("res://Scenes/UI/UnitInstanceView.tscn");
		battle = GetNodeOrNull<BattleSytem>("BattleSytem");
		turnLabel = GetNodeOrNull<Label>("MainMargin/MainVBox/TopBar/TurnLabel");
		playersPanel = GetNodeOrNull<PanelContainer>("MainMargin/MainVBox/ArenaRow/PlayersPanel");
		monstersPanel = GetNodeOrNull<PanelContainer>("MainMargin/MainVBox/ArenaRow/MonstersPanel");
		playersRow = GetNodeOrNull<HBoxContainer>("MainMargin/MainVBox/ArenaRow/PlayersPanel/Margin/VBox/PlayersRow");
		monstersRow = GetNodeOrNull<HBoxContainer>("MainMargin/MainVBox/ArenaRow/MonstersPanel/Margin/VBox/MonstersRow");
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
		windowLayer = GetNodeOrNull<Control>("WindowLayer");
		EnsureAuxiliaryWindows(); EnsureSetupDataInitialized();
		if (!AutoStartBattle) { battle?.RefreshBattleInfoDisplay(); ShowSetupWindow(); }
		else { initialMonsterOrder.Clear(); initialMonsterOrder.AddRange(BuildInitialMonsterIds()); battle?.OnInit(BuildInitialCharacterIds(), BuildInitialMonsterIds()); }
		RefreshAllUi(); BindUiEvents(); ApplyArenaPanelStyle();
	}

	public override void _Process(double delta)
	{
		if (pendingExternalRefresh)
		{
			pendingExternalRefresh = false;
			if (initialMonsterOrder.Count == 0) initialMonsterOrder.AddRange(GetOrderedMonsters().Select(m => m.UniqueInGameId));
			RefreshAllUi();
			if (battle != null && battle.IsBattleStarted) HideSetupWindow();
		}
		UpdateTooltipPosition();
		UpdateDragState();
	}

	public void RequestExternalUiRefresh() { pendingExternalRefresh = true; }

	public void ShowDamageNumberOnUnit(IUnitInstance unit, int damage)
	{
		if (unit == null || damage <= 0) return;
		if (unitViews.TryGetValue(unit.UniqueInGameId, out var view))
			view.Root.ShowFloatingDamage(damage);
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
		var players = GetOrderedPlayers();
		for (int i = 0; i < Math.Max(MaxPlayerSlots, players.Count); i++)
		{
			if (i < players.Count) playersRow.AddChild(CreateUnitPanel(players[i], true, players[i].Name));
			else playersRow.AddChild(CreateEmptyUnitSlot($"\u89D2\u8272\u69FD\u4F4D {i + 1}"));
		}
		for (int mi = 0; mi < initialMonsterOrder.Count; mi++)
		{
			int mid = initialMonsterOrder[mi];
			MonsterInstance lm = (battle != null && battle.Monsters != null && battle.Monsters.TryGetValue(mid, out var tmpLm)) ? tmpLm : null;
			bool alive = lm != null && lm.HP > 0;
			if (alive) monstersRow.AddChild(CreateUnitPanel(lm, false, lm.Name));
			else monstersRow.AddChild(CreateEmptyUnitSlot(lm != null ? lm.Name + "\n\u5DF2\u6B7B\u4EA1" : "\u5DF2\u6B7B\u4EA1"));
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
		string duration = BuildStateDurationText(stateType, stacks);
		string effect = stateType switch
		{
			StateType.Vulnerable => $"{duration}\u53D7\u5230\u7684\u4F24\u5BB3\u589E\u52A050%",
			StateType.Weak => $"{duration}\u9020\u6210\u7684\u653B\u51FB\u4F24\u5BB3\u964D\u4F4E25%",
			StateType.CounterAttack => $"{duration}\u5728\u56DE\u5408\u5916\u53D7\u5230\u653B\u51FB\u65F6\uFF0C\u5BF9\u6765\u6E90\u8FDB\u884C\u4E00\u6B21\u53CD\u51FB",
			StateType.WhirlwindSlash => $"{duration}\u56DE\u5408\u5916\u7684\u653B\u51FB\u4F1A\u4F5C\u7528\u4E8E\u6240\u6709\u654C\u4EBA",
			StateType.AddAttack => $"\u5F53\u524D\u989D\u5916\u653B\u51FB\u4F24\u5BB3 +{stacks}",
			StateType.ExtraEnergy => $"\u4E0B\u56DE\u5408\u989D\u5916\u83B7\u5F97 {stacks} \u70B9\u80FD\u91CF",
			StateType.CourageArmor => $"{duration}\u6BCF\u6253\u51FA\u4E00\u5F20\u653B\u51FB\u724C\u540E\u9632\u5FA1\u4E00\u6B21",
			_ => $"{stateName}\u6548\u679C\u4EE5\u5F53\u524D\u89C4\u5219\u5B9E\u73B0\u4E3A\u51C6",
		};
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
			return stateType switch
			{
				StateType.Vulnerable => "\u6613\u4F24",
				StateType.Weak => "\u865A\u5F31",
				StateType.CounterAttack => "\u53CD\u51FB",
				StateType.WhirlwindSlash => "\u65CB\u98CE\u65A9",
				StateType.AddAttack => "\u52A0\u653B",
				StateType.ExtraEnergy => "\u989D\u5916\u80FD\u91CF",
				StateType.Void => "\u865A\u65E0",
				StateType.CourageArmor => "\u52C7\u6C14\u94E0\u7532",
				_ => def.Name,
			};
		}
		return stateType.ToString();
	}
	private static string BuildStateDurationText(StateType stateType, int stacks)
	{
		if (!LoadingSystem.StateDictionary.TryGetValue(stateType, out var def) || def == null || def.IsPermanent || def.TurnStartDecayAmount <= 0)
			return string.Empty;
		int remaining = Math.Max(1, (int)Math.Ceiling((double)Math.Max(1, stacks) / def.TurnStartDecayAmount));
		return $"\u5728{remaining}\u56DE\u5408\u5185";
	}
}
