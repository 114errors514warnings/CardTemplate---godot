// SettlementUi.cs
// 战斗结算界面控制器（《战斗结算界面交互案》2026-09-27 定稿）：
//   结算面板（条状 Tab + 卡牌份 + 折损行 + 关闭）、卡牌三选一（CardDisplayPrefab）、
//   待领取浮窗、放弃确认弹窗。
// 由 RunFlowScene 常驻持有（面板 / 三选一 / 浮窗 / 弹窗统一收口，避免随战斗内容重建丢失）；
// 层号只认 RunUiLayers，禁止用 ZIndex 跨层抢占。
using Godot;
using System;
using System.Collections.Generic;

public partial class SettlementUi : Node
{
	// ── 文案（§二 / §5.1 / §5.5 / §6.3 / §7.2） ──
	public const string TitleText = "搜刮！";
	public const string CardPickTitleText = "选择一张牌";
	public const string SkipText = "跳过";
	public const string CloseText = "关闭";
	public const string AbandonTitleText = "放弃未领取的战利品？";
	public const string AbandonSuppressText = "本局游戏内不再显示";
	public const string AbandonConfirmText = "确认放弃并进入";
	public const string CancelText = "取消";
	public const string BadgeTitleText = "战利品";
	public const string CardPrefabPath = "res://Scenes/Card/CardDisplayPrefab.tscn";
	public const string CardPickButtonNamePrefix = "CardPick_";
	public const string CardTabButtonNamePrefix = "SettlementCardTab_";

	/// <summary>最近一条放弃日志内容（与 `GD.Print` 一致；烟测按关键字断言用）。</summary>
	public static string LastAbandonLog { get; private set; }

	/// <summary>清空放弃日志记录（烟测断言「本次没有打印」前调用）。</summary>
	public static void ResetLastAbandonLog() => LastAbandonLog = null;

	/// <summary>追回被窃金币（结算当刻已入账，这里只做展示；读档重进为 0，与旧实现一致）。</summary>
	public int RefundedStolenGold { get; set; }

	/// <summary>面板关闭：可能是「待领取态」（宿主刷新地图/浮窗），也可能是「结算完成」（宿主推进回地图）。</summary>
	public event Action PanelClosed;

	/// <summary>确认放弃未领取项（宿主负责重放被拦截的那次节点进入）。</summary>
	public event Action AbandonCommitted;

	/// <summary>面板 / 浮窗可见性变化（宿主据此把世界地图切回只读；面板关闭另见 <see cref="PanelClosed"/>）。</summary>
	public event Action StateChanged;

	private CanvasLayer panelLayer, badgeLayer, confirmLayer;
	private Control panelRoot, cardPickRoot, confirmRoot;
	private Button badgeRoot;
	private CheckBox suppressCheck;

	private static RunSession Session => RunSession.Instance;
	private static RunSaveData Run => RunSession.Instance?.Current;

	public bool IsPanelOpen => panelRoot != null && GodotObject.IsInstanceValid(panelRoot) && panelRoot.Visible;
	public bool IsCardPickOpen => cardPickRoot != null && GodotObject.IsInstanceValid(cardPickRoot) && cardPickRoot.Visible;
	public bool IsConfirmOpen => confirmRoot != null && GodotObject.IsInstanceValid(confirmRoot) && confirmRoot.Visible;
	public bool IsBadgeVisible => badgeRoot != null && GodotObject.IsInstanceValid(badgeRoot) && badgeRoot.Visible;

	/// <summary>未领取项数 = 未领取物品 Tab + 未领取卡牌份（§6.3）。</summary>
	public int UnclaimedCount => Run == null ? 0 : SettlementRewardPresenter.CountUnclaimed(Run, LoadingSystem.DropTableEntries);

	/// <summary>是否仍有未领取项（放弃闸门 / 浮窗显示判据）。</summary>
	public bool HasUnclaimed => Session?.IsInSettlement == true && UnclaimedCount > 0;

	public void Bind(CanvasLayer panel, CanvasLayer badge, CanvasLayer confirm)
	{
		panelLayer = panel;
		badgeLayer = badge;
		confirmLayer = confirm;
	}

	/// <summary>
	/// 按存档重建面板与浮窗（胜利弹面板 / 读档重进 / 领取后刷新）。
	/// 面板是否自动打开只看 `SettlementPanelClosed`：已关闭过的待领取态只显示浮窗（§6.5）。
	/// </summary>
	public void RefreshFromSave()
	{
		HideAbandonConfirm();
		HideCardPick();
		HidePanel();
		HideBadge();

		if (Run == null || Session?.IsInSettlement != true)
		{
			StateChanged?.Invoke();
			return;
		}

		BuildPanel();
		int unclaimed = UnclaimedCount;
		// 还有未领取项且玩家已关闭过面板 → 只显示浮窗；否则直接复现面板（含「全部已领取」态）。
		bool panelVisible = !Run.SettlementPanelClosed || unclaimed == 0;
		panelRoot.Visible = panelVisible;
		if (unclaimed > 0 && !panelVisible)
		{
			BuildBadge();
		}

		StateChanged?.Invoke();
	}

	// ── 结算面板（§二 / §三 / §四 / §五） ─────────────────────

	/// <summary>重建结算面板：标题条 + 条状 Tab 列表 + 折损行 / 追回金币行 + 底部「关闭」。</summary>
	private void BuildPanel()
	{
		HidePanel();
		panelRoot = new Control { Name = "SettlementPanelRoot", MouseFilter = Control.MouseFilterEnum.Stop };
		panelRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		panelLayer.AddChild(panelRoot);

		ColorRect dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore };
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		panelRoot.AddChild(dim);

		CenterContainer center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		panelRoot.AddChild(center);

		PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(780, 520) };
		center.AddChild(panel);

		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_bottom", 16);
		panel.AddChild(margin);

		VBoxContainer vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		margin.AddChild(vbox);

		BuildPanelTitleRow(vbox);

		// 条目多于 5 条时列表内部滚动：标题条与底部动作区固定不动（§三）。
		ScrollContainer scroll = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		vbox.AddChild(scroll);

		VBoxContainer tabs = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		tabs.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(tabs);
		BuildTabs(tabs);

		BuildPanelFooter(vbox);
	}

	/// <summary>标题条（羊皮纸标题「搜刮！」）+ 右上角关闭（X）：两个关闭入口等价（§6.1）。</summary>
	private void BuildPanelTitleRow(VBoxContainer vbox)
	{
		HBoxContainer titleRow = new HBoxContainer();
		titleRow.AddThemeConstantOverride("separation", 8);
		vbox.AddChild(titleRow);

		PanelContainer titleBar = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		Label title = new Label { Text = TitleText, HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 32);
		titleBar.AddChild(title);
		titleRow.AddChild(titleBar);

		Button closeX = new Button { Text = "X", CustomMinimumSize = new Vector2(52, 0) };
		closeX.Pressed += ClosePanel;
		titleRow.AddChild(closeX);
	}

	/// <summary>
	/// 面板列表：物品 Tab（掉落表行）+ 卡牌份 Tab。
	/// **2026-10-02 用户口径**：领取过的条目**直接从列表里消失**（不再转灰显示「已领取」）——
	/// 因此渲染走 `BuildVisibleItemTabs` / `BuildVisibleCardPools`，只列出还没领的。
	/// 回改方式：换成 `BuildItemTabs` / `Run.SettlementCardPools` 并把「已领取」文案与 `Disabled` 分支加回来。
	/// </summary>
	private void BuildTabs(VBoxContainer tabs)
	{
		foreach (SettlementItemTab tab in SettlementRewardPresenter.BuildVisibleItemTabs(Run, LoadingSystem.DropTableEntries))
		{
			SettlementItemTab captured = tab;
			Button row = new Button
			{
				Text = captured.Text,
				Alignment = HorizontalAlignment.Left,
				CustomMinimumSize = new Vector2(0, 64),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				FocusMode = Control.FocusModeEnum.None,
			};
			row.AddThemeFontSizeOverride("font_size", 24);
			row.Pressed += () => ClaimItem(captured);
			tabs.AddChild(row);
		}

		foreach (SettlementCardPoolSave pool in SettlementRewardPresenter.BuildVisibleCardPools(Run))
		{
			SettlementCardPoolSave captured = pool;
			string slotName = SettlementRewardPresenter.GetSlotDisplayName(captured, Session.GetSlotDisplayName);
			Button row = new Button
			{
				Name = CardTabButtonNamePrefix + captured.SlotIndex,
				Text = SettlementRewardPresenter.GetCardTabText(slotName),
				Alignment = HorizontalAlignment.Left,
				CustomMinimumSize = new Vector2(0, 64),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				FocusMode = Control.FocusModeEnum.None,
			};
			row.AddThemeFontSizeOverride("font_size", 24);
			row.Pressed += () => OpenCardPick(captured);
			tabs.AddChild(row);
		}
	}

	/// <summary>面板底部：折损行（有折损时）+ 追回被窃金币行 + 「关闭」按钮（§5.6 / §6.1）。</summary>
	private void BuildPanelFooter(VBoxContainer vbox)
	{
		if (Run.SettlementCardPools.Count > 0)
		{
			int rewardCount = MonsterValuePoints.GetCardRewardCountFromTier(Run.SettlementLossTier);
			string lossLine = SettlementRewardPresenter.FormatLossLine(Run.SettlementValueRatio, rewardCount);
			if (!string.IsNullOrEmpty(lossLine))
			{
				Label loss = new Label { Text = lossLine, HorizontalAlignment = HorizontalAlignment.Center };
				loss.AddThemeFontSizeOverride("font_size", 18);
				loss.AddThemeColorOverride("font_color", Colors.Orange);
				vbox.AddChild(loss);
			}
		}

		if (RefundedStolenGold > 0)
		{
			Label refund = new Label
			{
				Text = $"追回被窃金币 +{RefundedStolenGold}（被击杀的怪物原额返还）",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			refund.AddThemeFontSizeOverride("font_size", 18);
			refund.AddThemeColorOverride("font_color", Colors.LightGreen);
			vbox.AddChild(refund);
		}

		CenterContainer actionRow = new CenterContainer();
		Button close = new Button { Name = "SettlementCloseButton", Text = CloseText, CustomMinimumSize = new Vector2(300, 52) };
		close.AddThemeFontSizeOverride("font_size", 22);
		close.Pressed += ClosePanel;
		actionRow.AddChild(close);
		vbox.AddChild(actionRow);
	}

	/// <summary>物品 Tab 点击即领取（§四）：入账 + 去重键落档 → 重建面板，该 Tab 直接从列表消失（2026-10-02 口径）。</summary>
	private void ClaimItem(SettlementItemTab tab)
	{
		if (Session == null || tab == null)
		{
			return;
		}

		if (!Session.TryClaimSettlementReward(tab.ClaimKey, tab.Entry))
		{
			return;
		}

		GD.Print($"[结算] 领取物品：{tab.Text}。");
		RefreshFromSave();
	}

	private static string ResolveCardName(int cardId)
	{
		if (LoadingSystem.CardDictionary.TryGetValue(cardId, out Card card) && card != null && !string.IsNullOrWhiteSpace(card.CardName))
		{
			return card.CardName;
		}

		return $"卡牌ID {cardId}";
	}

	// ── 卡牌三选一（§5.1 – §5.5） ─────────────────────────────

	/// <summary>打开某份的三选一：同层遮罩盖住结算面板其余部分（含面板的「关闭」按钮）。</summary>
	private void OpenCardPick(SettlementCardPoolSave pool)
	{
		HideCardPick();
		if (pool == null || Session == null || SettlementRewardPresenter.IsCardPoolClaimed(Run, pool.SlotIndex))
		{
			return;
		}

		string slotName = SettlementRewardPresenter.GetSlotDisplayName(pool, Session.GetSlotDisplayName);
		int candidateCount = pool.CandidateCardIds?.Count ?? 0;
		if (candidateCount == 0)
		{
			GD.PrintErr($"[结算] {slotName} 的卡池为空（份 {pool.SlotIndex}）：该份显示空态，不跨角色补足。");
		}

		cardPickRoot = new Control { Name = "SettlementCardPickRoot", MouseFilter = Control.MouseFilterEnum.Stop };
		cardPickRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		panelLayer.AddChild(cardPickRoot);

		ColorRect dim = new ColorRect { Color = new Color(0, 0, 0, 0.78f) };
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		cardPickRoot.AddChild(dim);

		CenterContainer center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		cardPickRoot.AddChild(center);

		VBoxContainer vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 16);
		center.AddChild(vbox);

		Label title = new Label { Text = CardPickTitleText, HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 30);
		vbox.AddChild(title);

		if (candidateCount == 0)
		{
			Label empty = new Label { Text = SettlementRewardPresenter.EmptyPoolText, HorizontalAlignment = HorizontalAlignment.Center };
			empty.AddThemeFontSizeOverride("font_size", 22);
			empty.AddThemeColorOverride("font_color", Colors.Orange);
			vbox.AddChild(empty);
		}
		else
		{
			HBoxContainer cardRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			cardRow.AddThemeConstantOverride("separation", 24);
			vbox.AddChild(cardRow);
			foreach (int cardId in pool.CandidateCardIds)
			{
				cardRow.AddChild(BuildCandidateColumn(pool, cardId, slotName));
			}
		}

		CenterContainer skipRow = new CenterContainer();
		Button skip = new Button { Name = "SettlementCardSkipButton", Text = SkipText, CustomMinimumSize = new Vector2(320, 64) };
		skip.AddThemeFontSizeOverride("font_size", 24);
		skip.Pressed += SkipCardPick;
		skipRow.AddChild(skip);
		vbox.AddChild(skipRow);
	}

	/// <summary>跳过（§5.5，`Esc` 等价）：不领取、该份保持未领取，可再次点开重新选择。</summary>
	private void SkipCardPick()
	{
		HideCardPick();
	}

	/// <summary>一张候选：卡牌 prefab（`SyncFromCard`）+ 卡下方归属角色显示名 + 覆盖全卡的点击区。</summary>
	private Control BuildCandidateColumn(SettlementCardPoolSave pool, int cardId, string slotName)
	{
		float scale = ResolveCardScale();
		VBoxContainer column = new VBoxContainer { CustomMinimumSize = new Vector2(300f * scale, (420f * scale) + 40f) };
		column.AddThemeConstantOverride("separation", 8);

		Control holder = new Control { CustomMinimumSize = new Vector2(300f * scale, 420f * scale) };
		column.AddChild(holder);

		PackedScene packed = GD.Load<PackedScene>(CardPrefabPath);
		if (packed != null && LoadingSystem.CardDictionary.TryGetValue(cardId, out Card template) && template != null)
		{
			CardDisplayPrefab view = packed.Instantiate<CardDisplayPrefab>();
			view.Scale = new Vector2(scale, scale);
			holder.AddChild(view);
			view.SyncFromCard(template);
		}
		else
		{
			GD.PrintErr($"[结算] 卡牌 {cardId} 无法用 CardDisplayPrefab 显示（卡表缺失或 prefab 未找到）。");
		}

		// 点击命中区盖在卡面上：Flat 按钮同时提供悬停高亮（§5.4），点击立即选中并结算、不要求二次确认。
		Button hit = new Button { Name = CardPickButtonNamePrefix + cardId, Flat = true, FocusMode = Control.FocusModeEnum.None };
		hit.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		SettlementCardPoolSave capturedPool = pool;
		hit.Pressed += () => ChooseCard(capturedPool, cardId);
		holder.AddChild(hit);

		Label owner = new Label { Name = "CardOwner_" + cardId, Text = slotName, HorizontalAlignment = HorizontalAlignment.Center };
		owner.AddThemeFontSizeOverride("font_size", 20);
		column.AddChild(owner);
		return column;
	}

	/// <summary>屏幕高度不足时整组等比缩小（仍是同一个 prefab，§5.2）。</summary>
	private float ResolveCardScale()
	{
		Viewport viewport = GetViewport();
		if (viewport == null)
		{
			return 1f;
		}

		float available = viewport.GetVisibleRect().Size.Y - 260f;
		if (available >= 420f || available <= 0)
		{
			return 1f;
		}

		return Mathf.Clamp(available / 420f, 0.5f, 1f);
	}

	/// <summary>选中并立即领取（§5.4）：只有该份落档，重建面板后该份 Tab 从列表消失（2026-10-02 口径）。</summary>
	private void ChooseCard(SettlementCardPoolSave pool, int cardId)
	{
		if (Session == null || pool == null)
		{
			return;
		}

		if (!Session.TryClaimSettlementCard(pool.SlotIndex, cardId, out int ownerSlot))
		{
			return;
		}

		GD.Print($"[结算] 领取卡牌 {cardId}（{ResolveCardName(cardId)}）→ 槽位 {ownerSlot} {Session.GetSlotDisplayName(ownerSlot)}。");
		HideCardPick();
		RefreshFromSave();
	}

	// ── 关闭面板与待领取态（§6.1 – §6.4） ─────────────────────

	/// <summary>关闭面板（`Esc` / X / 底部「关闭」等价，§6.1）：不领取任何东西、不附加任何提示。</summary>
	private void ClosePanel()
	{
		if (Run == null || !IsPanelOpen)
		{
			return;
		}

		HideCardPick();
		HidePanel();
		if (HasUnclaimed)
		{
			// 待领取态（§6.2）：GameMode 仍是 InSettlement，浮窗常驻、地图转可选。
			Session.SetSettlementPanelClosed(true);
			BuildBadge();
		}
		else
		{
			// 全部领取：关闭即视为结算完成（直接转 OnMap、无浮窗），由宿主推进回地图（§7.3）。
			HideBadge();
		}

		PanelClosed?.Invoke();
	}

	/// <summary>结算浮窗（§6.3）：标题 + 未领取项数 + 来源名；点击重新打开面板。</summary>
	private void BuildBadge()
	{
		HideBadge();
		if (Run == null)
		{
			return;
		}

		string source = string.IsNullOrWhiteSpace(Run.SettlementEncounterName) ? "战斗结算" : Run.SettlementEncounterName;
		Button badge = new Button
		{
			Name = "SettlementBadge",
			Text = $"{BadgeTitleText}\n{UnclaimedCount} 项未领取\n{source}",
			CustomMinimumSize = new Vector2(220, 96),
			FocusMode = Control.FocusModeEnum.None,
			TooltipText = "点击重新打开结算界面",
		};
		badge.AddThemeFontSizeOverride("font_size", 18);
		// 位置（新案 §二，2026-09-28 修订）：屏幕右侧**上部**空带 —— 右缘内缩 24、上沿 = 15% 屏高、高 112；
		// 必须落在常驻按钮栏（≤ 0.08）与剧情行（0.09–0.145）之下、战斗 HUD 右侧列「当前格道具」（0.30–0.44）
		// 与「随身道具」（0.48–0.62）之上，避免遮挡既有 UI。
		badge.AnchorLeft = 1f;
		badge.AnchorRight = 1f;
		badge.AnchorTop = 0.15f;
		badge.AnchorBottom = 0.15f;
		badge.OffsetLeft = -244f;
		badge.OffsetRight = -24f;
		badge.OffsetTop = 0f;
		badge.OffsetBottom = 112f;
		badge.Pressed += OpenPanelFromBadge;
		badgeLayer.AddChild(badge);
		badgeRoot = badge;
	}

	/// <summary>点浮窗 → 重新打开面板（§6.4）：已领取的条目不再列出、各份候选不重抽（直接读存档）。</summary>
	private void OpenPanelFromBadge()
	{
		if (Session == null)
		{
			return;
		}

		Session.SetSettlementPanelClosed(false);
		RefreshFromSave();
	}

	// ── 放弃确认弹窗（§7.2 / §7.3 / §7.4） ───────────────────

	/// <summary>弹出放弃确认弹窗：**只有标题 + 勾选项 + 两个按钮**，不列未领取清单、不写后果说明。</summary>
	public void ShowAbandonConfirm()
	{
		if (Run == null || IsConfirmOpen)
		{
			return;
		}

		HideAbandonConfirm();
		confirmRoot = new Control { Name = "SettlementAbandonConfirmRoot", MouseFilter = Control.MouseFilterEnum.Stop };
		confirmRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		confirmLayer.AddChild(confirmRoot);

		ColorRect dim = new ColorRect { Color = new Color(0, 0, 0, 0.65f) };
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		confirmRoot.AddChild(dim);

		CenterContainer center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		confirmRoot.AddChild(center);

		PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(440, 220) };
		center.AddChild(panel);

		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		panel.AddChild(margin);

		VBoxContainer vbox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		vbox.AddThemeConstantOverride("separation", 16);
		margin.AddChild(vbox);

		Label title = new Label { Text = AbandonTitleText, HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 24);
		vbox.AddChild(title);

		suppressCheck = new CheckBox { Name = "SettlementAbandonSuppressCheck", Text = AbandonSuppressText, ButtonPressed = Run.SuppressAbandonSettlementConfirm };
		vbox.AddChild(suppressCheck);

		HBoxContainer buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		buttons.AddThemeConstantOverride("separation", 12);
		vbox.AddChild(buttons);

		Button confirm = new Button { Name = "SettlementAbandonConfirmButton", Text = AbandonConfirmText, CustomMinimumSize = new Vector2(200, 48) };
		confirm.Pressed += ConfirmAbandonFromDialog;
		buttons.AddChild(confirm);

		Button cancel = new Button { Name = "SettlementAbandonCancelButton", Text = CancelText, CustomMinimumSize = new Vector2(140, 48) };
		cancel.Pressed += CancelAbandonConfirm;
		buttons.AddChild(cancel);
	}

	/// <summary>「确认放弃并进入」：勾选落档 → 作废未领取项并打印一条日志（§7.2 / §7.4）。</summary>
	private void ConfirmAbandonFromDialog()
	{
		bool suppress = suppressCheck != null && GodotObject.IsInstanceValid(suppressCheck) && suppressCheck.ButtonPressed;
		if (suppress)
		{
			Session?.SetSuppressAbandonSettlementConfirm(true);
		}

		CommitAbandon(SettlementRewardPresenter.AbandonModeDialog);
	}

	/// <summary>「取消」/ `Esc`（§7.2）：只关弹窗，不进入节点、不放弃、不额外提示、不打印日志。</summary>
	public void CancelAbandonConfirm()
	{
		HideAbandonConfirm();
	}

	/// <summary>
	/// 实际作废未领取项：打印一条 `SETTLEMENT_ABANDONED`（未领取项为 0 时不打印）→ 清结算态，
	/// 然后由宿主重放被拦截的节点进入（§7.3 / §7.4）。`abandonMode` 区分玩家主动确认与本局静默放弃。
	/// </summary>
	public void CommitAbandon(string abandonMode)
	{
		HideAbandonConfirm();
		HideCardPick();
		HidePanel();
		HideBadge();
		StateChanged?.Invoke();

		if (Run == null)
		{
			AbandonCommitted?.Invoke();
			return;
		}

		List<string> details = SettlementRewardPresenter.BuildUnclaimedDetails(Run, LoadingSystem.DropTableEntries, Session.GetSlotDisplayName);
		if (details.Count > 0)
		{
			string log = SettlementRewardPresenter.FormatAbandonLog(
				Run.SettlementEncounterName,
				SettlementRewardPresenter.GetSourceNodeTypeText((MapNodeType)Run.SettlementSourceNodeType),
				details,
				abandonMode);
			LastAbandonLog = log;
			GD.Print(log);
		}

		// 放弃与「全部领取后关闭」都是本次结算的结束：节点同样算已访问。
		// 否则放弃后原节点仍是未访问态、可被再次点击，重新打一场 / 重播事件并再发一次奖励。
		Session.MarkCurrentNodeVisitedAndAdvanceEncounter();
		Session.CompleteSettlementToMap();
		AbandonCommitted?.Invoke();
	}

	// ── Esc 逐层关闭与清理（§九） ─────────────────────────────

	/// <summary>`Esc` 逐层关闭：三选一 → 跳过（不领取）；面板 → 关闭（待领取态）；弹窗 → 取消。返回 true = 已消费。</summary>
	public bool HandleEscape()
	{
		if (IsConfirmOpen)
		{
			CancelAbandonConfirm();
			return true;
		}

		if (IsCardPickOpen)
		{
			SkipCardPick();
			return true;
		}

		if (IsPanelOpen)
		{
			ClosePanel();
			return true;
		}

		return false;
	}

	private void HidePanel()
	{
		FreeNow(panelRoot);
		panelRoot = null;
	}

	private void HideCardPick()
	{
		FreeNow(cardPickRoot);
		cardPickRoot = null;
	}

	private void HideBadge()
	{
		FreeNow(badgeRoot);
		badgeRoot = null;
	}

	private void HideAbandonConfirm()
	{
		FreeNow(confirmRoot);
		confirmRoot = null;
		suppressCheck = null;
	}

	/// <summary>隐藏后排队释放：先置不可见，避免同一帧内新旧两棵树同时可见 / 同时吃输入。</summary>
	private static void FreeNow(Node node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node))
		{
			return;
		}

		if (node is CanvasItem item)
		{
			item.Visible = false;
		}

		node.QueueFree();
	}
}

