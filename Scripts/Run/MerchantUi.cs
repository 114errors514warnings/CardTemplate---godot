// MerchantUi.cs
// 商人界面（商人交互案 §4.1–§4.9 的结构 / §6.1–§6.4 的购买流程）。
//   上部一行 5 个**卡包**格（点开看包内 5 张，逐张买）；材料 / 食物各 3 格（左列，上 3 / 下 3）；
//   装备 / 道具各 3 格（右列）+ 钥匙 1 格；右下两个大按钮：`卡牌相关操作`（删除 / 变化 / 转移 / 升级）
//   与 `锻造炉`（**复用村庄 `SmithyUi`**，注入 `SmithyContext.MerchantForge`）。
// 规则不住这里：货架 / 卡包抽样在 `MerchantStock` / `MerchantCardPacks`，卡牌操作校验与计价在 `DeckOps`，
//   全部结算（扣金币 / 入包 / 入组 / 次数）在 `RunSession.Merchant.cs`；本文件只画界面、读快照、点一下调结算。
// 时间点：商人**不消耗时间点**（§一 / §五）——买货 / 买卡 / 卡牌操作都只花金币；只有锻造炉那一次打造另计（§4.9）。
using Godot;
using static Godot.Control;
using System;
using System.Collections.Generic;
using CardSimulator;

public partial class MerchantUi : Node
{
	public const string CloseText = "关闭";
	public const string DeckOpsText = "卡牌相关操作";
	public const string ForgeText = "锻造炉";
	public const string ConfirmText = "确认";
	public const string DefaultHintText = "点货架格买入（点一下即结算）；点上部卡包看包内 5 张卡逐张买；右下可做卡牌操作与锻造。";
	public const string DeckOpsHintText = "先选操作 → 选槽位 → 选卡（转移还要选目标槽位），再点「确认」。";
	public const string PackHintText = "点一张可买的卡即结算（第 4 / 5 包先选归属槽位）；买走的不再上架。";

	private CanvasLayer layer;
	private Control root;
	private Label titleLabel;
	private Label hintLabel;
	private HBoxContainer packsRow;
	private HBoxContainer materialRow;
	private HBoxContainer foodRow;
	private HBoxContainer equipmentRow;
	private HBoxContainer itemRow;
	private HBoxContainer keyRow;
	private Button deckOpsButton;
	private Button forgeButton;
	private readonly List<Button> packButtons = new List<Button>();
	private readonly Dictionary<MerchantCategory, List<Button>> stockButtons = new Dictionary<MerchantCategory, List<Button>>();

	private Control packOverlay;
	private Label packTitleLabel;
	private Label packHintLabel;
	private HBoxContainer packCardRow;
	private HBoxContainer packSlotRow;
	private readonly List<Button> packCardButtons = new List<Button>();
	private readonly List<Button> packSlotButtons = new List<Button>();
	private int openPackIndex = -1;
	private int pendingCardIndex = -1;

	private Control opsOverlay;
	private Label opsHintLabel;
	private Label opsCostLabel;
	private Button opsConfirmButton;
	private readonly List<Button> opsButtons = new List<Button>();
	private readonly List<Button> opsSlotButtons = new List<Button>();
	private VBoxContainer opsDeckList;
	private VBoxContainer opsSlotList;
	private readonly List<Button> opsDeckButtons = new List<Button>();
	private MerchantDeckOp selectedOp = MerchantDeckOp.Remove;
	private int selectedSlot;
	private int selectedDeckIndex = -1;
	private int selectedTargetSlot = -1;

	private SmithyUi smithy;
	private Action onClose;

	private static readonly MerchantCategory[] LeftCategories = { MerchantCategory.Material, MerchantCategory.Food };
	private static readonly MerchantCategory[] RightCategories = { MerchantCategory.Equipment, MerchantCategory.Item, MerchantCategory.Key };

	/// <summary>界面是否打开（单实例判据）。</summary>
	public bool IsOpen => root != null && GodotObject.IsInstanceValid(root) && root.Visible;

	/// <summary>卡包详细子界面是否开着。</summary>
	public bool IsPackDetailOpen => packOverlay != null && GodotObject.IsInstanceValid(packOverlay) && packOverlay.Visible;

	/// <summary>卡牌操作子界面是否开着。</summary>
	public bool IsDeckOpsOpen => opsOverlay != null && GodotObject.IsInstanceValid(opsOverlay) && opsOverlay.Visible;

	/// <summary>锻造炉（复用的 `SmithyUi`）是否开着。</summary>
	public bool IsForgeOpen => smithy != null && GodotObject.IsInstanceValid(smithy) && smithy.IsOpen;

	public string HintText => hintLabel?.Text ?? string.Empty;
	public string PackHintTextValue => packHintLabel?.Text ?? string.Empty;
	public string DeckOpsHintTextValue => opsHintLabel?.Text ?? string.Empty;

	/// <summary>当前打开的卡包序号（1–5；-1 = 没开）。</summary>
	public int OpenPackIndex => openPackIndex;

	public int PackCount => packButtons.Count;

	/// <summary>某类目已渲染的格数（烟测断言格数用）。</summary>
	public int StockSlotCount(MerchantCategory category) =>
		stockButtons.TryGetValue(category, out List<Button> slots) ? slots.Count : 0;

	/// <summary>某包还剩几张可买（界面文案用的同一处判据）。</summary>
	public int PackRemaining(int packIndex) =>
		MerchantCardPacks.RemainingCount(
			MerchantCardPacks.FindPack(RunSession.Instance?.Current?.MerchantState?.CardPacks, packIndex));

	/// <summary>卡组列当前渲染的行数（烟测用）。</summary>
	public int DeckRowCount => opsDeckButtons.Count;

	public override void _Ready()
	{
		layer = new CanvasLayer { Layer = RunUiLayers.Modal };
		AddChild(layer);
		BuildUi();
		BuildPackOverlay();
		BuildDeckOpsOverlay();
		smithy = new SmithyUi();
		AddChild(smithy);
	}

	/// <summary>打开界面：首次进入本商人时生成快照（`RunSession.EnsureMerchantSnapshot`），然后整屏刷新。</summary>
	public void Open(Action closed)
	{
		onClose = closed;
		RunSession session = RunSession.Instance;
		session?.EnsureMerchantSnapshot();
		openPackIndex = -1;
		pendingCardIndex = -1;
		selectedOp = MerchantDeckOp.Remove;
		selectedSlot = 0;
		selectedDeckIndex = -1;
		selectedTargetSlot = -1;
		ClosePackDetail();
		CloseDeckOps();
		RefreshAll();
		root.Visible = true;
		SetHint(DefaultHintText);
	}

	/// <summary>关闭界面（`Esc` / `关闭` 同一个出口）：子界面先各自收掉。</summary>
	public void Close()
	{
		if (IsPackDetailOpen || IsDeckOpsOpen || IsForgeOpen)
		{
			if (IsForgeOpen)
			{
				smithy.Close();
				return;
			}

			ClosePackDetail();
			CloseDeckOps();
			return;
		}

		if (root != null && GodotObject.IsInstanceValid(root))
		{
			root.Visible = false;
		}

		Action callback = onClose;
		onClose = null;
		callback?.Invoke();
	}

	public override void _UnhandledInput(InputEvent inputEvent)
	{
		if (!IsOpen || inputEvent is not InputEventKey key || !key.Pressed || key.Keycode != Key.Escape)
		{
			return;
		}

		Close();
		GetViewport()?.SetInputAsHandled();
	}

	private void SetHint(string text)
	{
		if (hintLabel != null)
		{
			hintLabel.Text = text ?? string.Empty;
		}
	}

	private static RunSession Session => RunSession.Instance;
	private static RunSaveData Run => RunSession.Instance?.Current;

	private static readonly MerchantCategory[] AllCategories =
	{
		MerchantCategory.Material,
		MerchantCategory.Food,
		MerchantCategory.Equipment,
		MerchantCategory.Item,
		MerchantCategory.Key,
	};

	private void BuildUi()
	{
		root = new Control { Visible = false };
		root.SetAnchorsPreset(LayoutPreset.FullRect);
		root.MouseFilter = MouseFilterEnum.Stop;
		layer.AddChild(root);

		ColorRect dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
		dim.SetAnchorsPreset(LayoutPreset.FullRect);
		root.AddChild(dim);

		PanelContainer panel = new PanelContainer();
		panel.SetAnchorsPreset(LayoutPreset.Center);
		panel.CustomMinimumSize = new Vector2(1040f, 660f);
		root.AddChild(panel);

		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		VBoxContainer column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 8);
		margin.AddChild(column);

		HBoxContainer headerRow = new HBoxContainer();
		column.AddChild(headerRow);

		titleLabel = new Label { Text = string.Empty, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		titleLabel.AddThemeFontSizeOverride("font_size", 18);
		headerRow.AddChild(titleLabel);

		Button closeButton = new Button { Text = CloseText };
		closeButton.Pressed += Close;
		headerRow.AddChild(closeButton);

		column.AddChild(new HSeparator());

		// 上部一行 5 个卡包（商人案 §4.2）。
		packsRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		packsRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(packsRow);

		column.AddChild(new HSeparator());

		// 中段：左列（材料 / 食物）、右列（装备 / 道具 / 钥匙）—— §4.4–§4.7 的位置口径。
		HBoxContainer body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 16);
		column.AddChild(body);

		VBoxContainer left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		left.SizeFlagsStretchRatio = 0.5f;
		left.AddThemeConstantOverride("separation", 6);
		body.AddChild(left);
		materialRow = AddStockSection(left, "材料（每格一件，点一下即买入）");
		foodRow = AddStockSection(left, "食物（买入后按实例入背包，各自记有效期）");

		VBoxContainer right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		right.SizeFlagsStretchRatio = 0.5f;
		right.AddThemeConstantOverride("separation", 6);
		body.AddChild(right);
		equipmentRow = AddStockSection(right, "装备（买入进背包，不自动装上）");
		itemRow = AddStockSection(right, "道具（买入进背包，不自动进随身格）");
		keyRow = AddStockSection(right, "钥匙（每层商人 1 把）");

		column.AddChild(new HSeparator());

		HBoxContainer bottomRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		bottomRow.AddThemeConstantOverride("separation", 10);
		column.AddChild(bottomRow);

		deckOpsButton = new Button { Text = DeckOpsText, CustomMinimumSize = new Vector2(180f, 46f) };
		deckOpsButton.Pressed += OpenDeckOps;
		bottomRow.AddChild(deckOpsButton);

		forgeButton = new Button { Text = ForgeText, CustomMinimumSize = new Vector2(180f, 46f) };
		forgeButton.Pressed += OpenForge;
		bottomRow.AddChild(forgeButton);

		hintLabel = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		hintLabel.AddThemeFontSizeOverride("font_size", 14);
		column.AddChild(hintLabel);
	}

	/// <summary>加一节货架（标题 + 一行格），返回那一行（刷新时按类目填格）。</summary>
	private HBoxContainer AddStockSection(VBoxContainer parent, string title)
	{
		Label label = new Label { Text = title };
		label.AddThemeFontSizeOverride("font_size", 14);
		parent.AddChild(label);

		HBoxContainer row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);
		return row;
	}

	private HBoxContainer RowOf(MerchantCategory category) => category switch
	{
		MerchantCategory.Material => materialRow,
		MerchantCategory.Food => foodRow,
		MerchantCategory.Equipment => equipmentRow,
		MerchantCategory.Item => itemRow,
		_ => keyRow,
	};

	private void RefreshAll()
	{
		RefreshHeader();
		RefreshPacks();
		foreach (MerchantCategory category in AllCategories)
		{
			RefreshStock(category);
		}
	}

	private void RefreshHeader()
	{
		if (titleLabel != null)
		{
			titleLabel.Text = $"商人　·　金币 {Run?.Gold ?? 0}";
		}
	}

	/// <summary>刷上部 5 个卡包格：包名 + `余 N / 5`（售罄显示 `已售罄`，仍可点开只读）。</summary>
	private void RefreshPacks()
	{
		if (packsRow == null)
		{
			return;
		}

		List<RunMerchantCardPackSave> packs = Run?.MerchantState?.CardPacks;
		int count = packs?.Count ?? 0;
		while (packButtons.Count < count)
		{
			int index = packButtons.Count;
			Button button = new Button
			{
				CustomMinimumSize = new Vector2(180f, 64f),
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			button.Pressed += () => OpenPackDetail(index);
			packsRow.AddChild(button);
			packButtons.Add(button);
		}

		for (int i = 0; i < packButtons.Count; i++)
		{
			RunMerchantCardPackSave pack = i < count ? packs[i] : null;
			if (pack == null)
			{
				packButtons[i].Text = "—";
				packButtons[i].Modulate = new Color(1f, 1f, 1f, 0.4f);
				continue;
			}

			int remaining = MerchantCardPacks.RemainingCount(pack);
			int total = pack.Cards?.Count ?? 0;
			packButtons[i].Text = remaining <= 0
				? $"{PackDisplayName(pack)}\n已售罄"
				: $"{PackDisplayName(pack)}\n余 {remaining} / {total}";
			packButtons[i].Modulate = Colors.White;
		}
	}

	/// <summary>包名（§4.2）：包 1–3 = 槽位角色专属包；包 4 = 混合包；包 5 = 通用牌包。</summary>
	private static string PackDisplayName(RunMerchantCardPackSave pack)
	{
		if (pack == null)
		{
			return "卡包";
		}

		return (MerchantPackKind)pack.Kind switch
		{
			MerchantPackKind.Character => $"槽位 {pack.OwnerSlot + 1} 专属包",
			MerchantPackKind.Mixed => "混合包",
			_ => "通用牌包",
		};
	}

	/// <summary>刷新某一类目的 3（钥匙 1）格：名称 + 价；售出后打「已售出」并灰显，买不起时价格标红。</summary>
	private void RefreshStock(MerchantCategory category)
	{
		HBoxContainer row = RowOf(category);
		if (row == null)
		{
			return;
		}

		int slots = MerchantCatalog.StockSlots(category);
		if (!stockButtons.TryGetValue(category, out List<Button> buttons))
		{
			buttons = new List<Button>();
			stockButtons[category] = buttons;
		}

		while (buttons.Count < slots)
		{
			int index = buttons.Count;
			Button button = new Button
			{
				CustomMinimumSize = new Vector2(150f, 72f),
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			button.Pressed += () => OnStockPressed(category, index);
			row.AddChild(button);
			buttons.Add(button);
		}

		for (int i = 0; i < slots; i++)
		{
			RunMerchantStockEntrySave entry = MerchantStock.FindEntry(Run?.MerchantState?.Stock, category, i);
			Button button = buttons[i];
			if (entry == null)
			{
				button.Text = "—";
				button.Modulate = new Color(1f, 1f, 1f, 0.4f);
				continue;
			}

			button.Text = entry.Sold
				? $"{entry.DefinitionId}\n{entry.Price} 金币\n已售出"
				: $"{entry.DefinitionId}\n{entry.Price} 金币";
			bool poor = !entry.Sold && (Run?.Gold ?? 0) < entry.Price;
			button.Modulate = entry.Sold
				? new Color(1f, 1f, 1f, 0.45f)
				: poor ? new Color(1f, 0.6f, 0.6f, 1f) : Colors.White;
		}
	}

	private void OnStockPressed(MerchantCategory category, int slotIndex) => BuyStock(category, slotIndex);

	/// <summary>**AI 接口用**：买某类目第 `slotIndex` 格（= 点那一格，点一下即结算）。</summary>
	public bool BuyStock(MerchantCategory category, int slotIndex)
	{
		if (Session == null)
		{
			SetHint("没有进行中的本局。");
			return false;
		}

		string name = MerchantStock.FindEntry(Run?.MerchantState?.Stock, category, slotIndex)?.DefinitionId ?? "该格";
		bool ok = Session.TryBuyMerchantStock(category, slotIndex, out string error);
		SetHint(ok ? $"买入完成：{name}。" : error);
		RefreshHeader();
		RefreshStock(category);
		return ok;
	}

	// ── 卡包详细（§4.3）──────────────────────────────────────────────

	private void BuildPackOverlay()
	{
		packOverlay = new Control { Visible = false };
		packOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		packOverlay.MouseFilter = MouseFilterEnum.Stop;
		layer.AddChild(packOverlay);

		ColorRect dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
		dim.SetAnchorsPreset(LayoutPreset.FullRect);
		packOverlay.AddChild(dim);

		PanelContainer panel = new PanelContainer();
		panel.SetAnchorsPreset(LayoutPreset.Center);
		panel.CustomMinimumSize = new Vector2(900f, 420f);
		packOverlay.AddChild(panel);

		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		VBoxContainer column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 8);
		margin.AddChild(column);

		HBoxContainer header = new HBoxContainer();
		column.AddChild(header);
		packTitleLabel = new Label { Text = string.Empty, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		packTitleLabel.AddThemeFontSizeOverride("font_size", 16);
		header.AddChild(packTitleLabel);
		Button closeButton = new Button { Text = CloseText };
		closeButton.Pressed += ClosePackDetail;
		header.AddChild(closeButton);

		column.AddChild(new HSeparator());

		packCardRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		packCardRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(packCardRow);

		packSlotRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Visible = false };
		packSlotRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(packSlotRow);

		packHintLabel = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		packHintLabel.AddThemeFontSizeOverride("font_size", 14);
		column.AddChild(packHintLabel);
	}

	/// <summary>打开某包的卡包详细（§4.3）；`Esc` / `关闭` 回商人界面。</summary>
	public void OpenPackDetail(int packIndex)
	{
		RunMerchantCardPackSave pack = MerchantCardPacks.FindPack(Run?.MerchantState?.CardPacks, packIndex);
		if (pack == null)
		{
			SetHint("该卡包不存在。");
			return;
		}

		openPackIndex = packIndex;
		pendingCardIndex = -1;
		if (packOverlay != null && GodotObject.IsInstanceValid(packOverlay))
		{
			packOverlay.Visible = true;
		}

		RefreshPackDetail();
		if (packHintLabel != null)
		{
			packHintLabel.Text = PackHintText;
		}
	}

	/// <summary>关掉卡包详细（回商人界面，金币与其它货架状态不变）。</summary>
	public void ClosePackDetail()
	{
		openPackIndex = -1;
		pendingCardIndex = -1;
		if (packOverlay != null && GodotObject.IsInstanceValid(packOverlay))
		{
			packOverlay.Visible = false;
		}
	}

	private void RefreshPackDetail()
	{
		if (!IsPackDetailOpen)
		{
			return;
		}

		RunMerchantCardPackSave pack = MerchantCardPacks.FindPack(Run?.MerchantState?.CardPacks, openPackIndex);
		if (pack == null)
		{
			ClosePackDetail();
			return;
		}

		packTitleLabel.Text = $"{PackDisplayName(pack)}　·　金币 {Run?.Gold ?? 0}";
		int cards = pack.Cards?.Count ?? 0;
		while (packCardButtons.Count < cards)
		{
			int index = packCardButtons.Count;
			Button button = new Button
			{
				CustomMinimumSize = new Vector2(150f, 130f),
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			button.Pressed += () => OnPackCardPressed(index);
			packCardRow.AddChild(button);
			packCardButtons.Add(button);
		}

		for (int i = 0; i < packCardButtons.Count; i++)
		{
			RunMerchantCardEntrySave card = i < cards ? pack.Cards[i] : null;
			Button button = packCardButtons[i];
			if (card == null)
			{
				button.Text = "—";
				button.Modulate = new Color(1f, 1f, 1f, 0.4f);
				continue;
			}

			string tier = TierLetter((CardTier)card.Tier);
			button.Text = card.Sold
				? $"{CardName(card.CardId)}\n{tier} · {card.Price} 金币\n已售出"
				: $"{CardName(card.CardId)}\n{tier} · {card.Price} 金币";
			bool poor = !card.Sold && (Run?.Gold ?? 0) < card.Price;
			button.Modulate = card.Sold
				? new Color(1f, 1f, 1f, 0.45f)
				: poor ? new Color(1f, 0.6f, 0.6f, 1f) : Colors.White;
		}

		RefreshPackSlotRow();
	}

	/// <summary>归属选择行（§4.3：只有包 4 / 5 需要；包 1–3 点了就直接结算）。</summary>
	private void RefreshPackSlotRow()
	{
		bool needed = pendingCardIndex >= 0;
		packSlotRow.Visible = needed;
		if (!needed)
		{
			return;
		}

		int slotCount = Run?.CharacterSlots?.Count ?? 0;
		while (packSlotButtons.Count < slotCount)
		{
			int slot = packSlotButtons.Count;
			Button button = new Button { CustomMinimumSize = new Vector2(150f, 40f) };
			button.Pressed += () => OnPackSlotPressed(slot);
			packSlotRow.AddChild(button);
			packSlotButtons.Add(button);
		}

		for (int i = 0; i < packSlotButtons.Count; i++)
		{
			packSlotButtons[i].Visible = i < slotCount;
			if (i < slotCount)
			{
				int deckCount = Session?.GetSlotDeck(i)?.Count ?? 0;
				string slotName = Session?.GetSlotDisplayName(i) ?? ("槽位 " + (i + 1));
				packSlotButtons[i].Text = $"{slotName}（{deckCount}）";
			}
		}
	}

	private void OnPackCardPressed(int cardIndex)
	{
		RunMerchantCardPackSave pack = MerchantCardPacks.FindPack(Run?.MerchantState?.CardPacks, openPackIndex);
		if (pack?.Cards == null || cardIndex < 0 || cardIndex >= pack.Cards.Count)
		{
			return;
		}

		RunMerchantCardEntrySave card = pack.Cards[cardIndex];
		if (card.Sold)
		{
			packHintLabel.Text = MerchantStock.SoldOutText;
			return;
		}

		if ((Run?.Gold ?? 0) < card.Price)
		{
			packHintLabel.Text = MerchantStock.GoldShortText(card.Price, Run?.Gold ?? 0);
			return;
		}

		if (pack.OwnerSlot >= 0)
		{
			BuyPackCard(pack.PackIndex, cardIndex, pack.OwnerSlot);
			return;
		}

		// 包 4 / 5：先弹归属选择（§4.3；直接关掉 = 取消，不扣金币）。
		pendingCardIndex = cardIndex;
		RefreshPackSlotRow();
		packHintLabel.Text = "选择把这张卡加入哪个角色的卡组。";
	}

	private void OnPackSlotPressed(int slot)
	{
		if (pendingCardIndex < 0)
		{
			return;
		}

		int cardIndex = pendingCardIndex;
		pendingCardIndex = -1;
		BuyPackCard(openPackIndex, cardIndex, slot);
	}

	/// <summary>**AI 接口用**：买某包第 `cardIndex` 张卡（= 点那一张；`targetSlot &lt; 0` = 用包绑定槽位）。</summary>
	public bool BuyPackCard(int packIndex, int cardIndex, int targetSlot)
	{
		if (Session == null)
		{
			SetHint("没有进行中的本局。");
			return false;
		}

		RunMerchantCardPackSave pack = MerchantCardPacks.FindPack(Run?.MerchantState?.CardPacks, packIndex);
		int owner = targetSlot >= 0 ? targetSlot : pack?.OwnerSlot ?? -1;
		int cardId = pack?.Cards != null && cardIndex >= 0 && cardIndex < pack.Cards.Count
			? pack.Cards[cardIndex].CardId
			: 0;
		bool ok = Session.TryBuyMerchantCard(packIndex, cardIndex, owner, out string error);
		SetHint(ok ? $"买入完成：{CardName(cardId)}。" : error);
		RefreshHeader();
		RefreshPacks();
		RefreshPackDetail();
		return ok;
	}

	// ── 卡牌操作（§4.8 / §5.2）────────────────────────────────────────

	private void BuildDeckOpsOverlay()
	{
		opsOverlay = new Control { Visible = false };
		opsOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		opsOverlay.MouseFilter = MouseFilterEnum.Stop;
		layer.AddChild(opsOverlay);

		ColorRect dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.5f) };
		dim.SetAnchorsPreset(LayoutPreset.FullRect);
		opsOverlay.AddChild(dim);

		PanelContainer panel = new PanelContainer();
		panel.SetAnchorsPreset(LayoutPreset.Center);
		panel.CustomMinimumSize = new Vector2(1000f, 560f);
		opsOverlay.AddChild(panel);

		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		VBoxContainer column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 8);
		margin.AddChild(column);

		HBoxContainer header = new HBoxContainer();
		column.AddChild(header);
		Label title = new Label { Text = "卡牌操作", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		title.AddThemeFontSizeOverride("font_size", 16);
		header.AddChild(title);
		Button closeButton = new Button { Text = CloseText };
		closeButton.Pressed += CloseDeckOps;
		header.AddChild(closeButton);

		column.AddChild(new HSeparator());

		HBoxContainer body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 14);
		column.AddChild(body);

		// 操作列（26%）
		VBoxContainer opsColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		opsColumn.SizeFlagsStretchRatio = 0.26f;
		opsColumn.AddThemeConstantOverride("separation", 6);
		body.AddChild(opsColumn);
		foreach (MerchantDeckOp op in new[] { MerchantDeckOp.Remove, MerchantDeckOp.Change, MerchantDeckOp.Transfer, MerchantDeckOp.Upgrade })
		{
			MerchantDeckOp captured = op;
			Button button = new Button { CustomMinimumSize = new Vector2(200f, 44f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			button.Pressed += () => OnOpsPressed(captured);
			opsColumn.AddChild(button);
			opsButtons.Add(button);
		}

		// 角色列（20%）
		opsSlotList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		opsSlotList.SizeFlagsStretchRatio = 0.2f;
		opsSlotList.AddThemeConstantOverride("separation", 6);
		body.AddChild(opsSlotList);

		// 卡组列（54%）
		VBoxContainer deckColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		deckColumn.SizeFlagsStretchRatio = 0.54f;
		deckColumn.AddThemeConstantOverride("separation", 4);
		body.AddChild(deckColumn);
		ScrollContainer deckScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		deckColumn.AddChild(deckScroll);
		opsDeckList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		opsDeckList.AddThemeConstantOverride("separation", 4);
		deckScroll.AddChild(opsDeckList);

		column.AddChild(new HSeparator());

		HBoxContainer bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		bottom.AddThemeConstantOverride("separation", 10);
		column.AddChild(bottom);
		opsCostLabel = new Label { Text = string.Empty, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		opsCostLabel.AddThemeFontSizeOverride("font_size", 14);
		bottom.AddChild(opsCostLabel);
		opsConfirmButton = new Button { Text = ConfirmText, CustomMinimumSize = new Vector2(120f, 40f) };
		opsConfirmButton.Pressed += () => ConfirmDeckOp();
		bottom.AddChild(opsConfirmButton);

		opsHintLabel = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		opsHintLabel.AddThemeFontSizeOverride("font_size", 14);
		column.AddChild(opsHintLabel);
	}

	/// <summary>打开卡组操作界面（默认选中槽位 0 + 删除卡牌，§6.3 第 1 条）。</summary>
	public void OpenDeckOps()
	{
		selectedOp = MerchantDeckOp.Remove;
		selectedSlot = 0;
		selectedDeckIndex = -1;
		selectedTargetSlot = -1;
		if (opsOverlay != null && GodotObject.IsInstanceValid(opsOverlay))
		{
			opsOverlay.Visible = true;
		}

		RefreshDeckOps();
		SetOpsHint(DeckOpsHintText);
	}

	/// <summary>关掉卡组操作界面（回商人界面；次数不重置，§6.3 第 6 条）。</summary>
	public void CloseDeckOps()
	{
		if (opsOverlay != null && GodotObject.IsInstanceValid(opsOverlay))
		{
			opsOverlay.Visible = false;
		}
	}

	private void SetOpsHint(string text)
	{
		if (opsHintLabel != null)
		{
			opsHintLabel.Text = text ?? string.Empty;
		}
	}

	/// <summary>整屏刷新卡组操作界面：操作列（价 + 可用性）/ 角色列（张数）/ 卡组列（一行一卡）。</summary>
	private void RefreshDeckOps()
	{
		if (!IsDeckOpsOpen)
		{
			return;
		}

		// 操作列：名称 + 该操作当前价（不可用时灰显；价与可用性都来自 `ValidateMerchantDeckOp`，界面不另算一份）。
		for (int i = 0; i < opsButtons.Count; i++)
		{
			MerchantDeckOp op = (MerchantDeckOp)i;
			int cost = -1;
			string reason = string.Empty;
			bool usable = Session != null
				&& Session.ValidateMerchantDeckOp(op, selectedSlot, selectedDeckIndex, selectedTargetSlot, out cost, out reason);
			opsButtons[i].Text = $"{OpName(op)}　{(usable ? cost + " 金币" : "—")}";
			opsButtons[i].Modulate = op == selectedOp
				? new Color(0.7f, 0.9f, 1f, 1f)
				: usable ? Colors.White : new Color(1f, 1f, 1f, 0.5f);
		}

		// 角色列：显示名 + 卡组张数；转移时点它选目标槽位，其余情况点它切源槽位。
		int slotCount = Run?.CharacterSlots?.Count ?? 0;
		while (opsSlotButtons.Count < slotCount)
		{
			int slot = opsSlotButtons.Count;
			Button button = new Button { CustomMinimumSize = new Vector2(160f, 40f) };
			button.Pressed += () => OnOpsSlotPressed(slot);
			opsSlotList.AddChild(button);
			opsSlotButtons.Add(button);
		}

		for (int i = 0; i < opsSlotButtons.Count; i++)
		{
			opsSlotButtons[i].Visible = i < slotCount;
			if (i >= slotCount)
			{
				continue;
			}

			int deckCount = Session?.GetSlotDeck(i)?.Count ?? 0;
			string name = Session?.GetSlotDisplayName(i) ?? ("槽位 " + (i + 1));
			bool isSource = i == selectedSlot;
			bool isTarget = selectedOp == MerchantDeckOp.Transfer && i == selectedTargetSlot;
			opsSlotButtons[i].Text = $"{name}（{deckCount}）{(isTarget ? " ←目标" : string.Empty)}";
			opsSlotButtons[i].Modulate = isTarget
				? new Color(0.7f, 1f, 0.7f, 1f)
				: isSource ? new Color(0.7f, 0.9f, 1f, 1f) : Colors.White;
		}

		// 卡组列：一行一张卡（卡名 · 费用 · 等级 · 永久升级）。
		List<RunDeckEntry> deck = Session?.GetSlotDeck(selectedSlot) ?? new List<RunDeckEntry>();
		while (opsDeckButtons.Count < deck.Count)
		{
			int index = opsDeckButtons.Count;
			Button button = new Button { CustomMinimumSize = new Vector2(0f, 34f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			button.Pressed += () => OnOpsDeckCardPressed(index);
			opsDeckList.AddChild(button);
			opsDeckButtons.Add(button);
		}

		for (int i = 0; i < opsDeckButtons.Count; i++)
		{
			opsDeckButtons[i].Visible = i < deck.Count;
			if (i >= deck.Count)
			{
				continue;
			}

			RunDeckEntry entry = deck[i];
			int cardId = entry?.CardId ?? 0;
			int level = entry?.PermanentUpgradeLevel ?? 0;
			opsDeckButtons[i].Text = $"{CardName(cardId)}　{CardCostOf(cardId)} 费 · {TierLetter(CardTierOf(cardId))}"
				+ (level > 0 ? $" · +{level}" : string.Empty);
			opsDeckButtons[i].Modulate = i == selectedDeckIndex ? new Color(0.7f, 0.9f, 1f, 1f) : Colors.White;
		}

		// 费用行：选中卡 + 该操作的价 + 次数提示。
		string selected = selectedDeckIndex >= 0 && selectedDeckIndex < deck.Count
			? CardName(deck[selectedDeckIndex]?.CardId ?? 0)
			: "（未选卡）";
		int price = -1;
		string blockReason = string.Empty;
		bool ok = Session != null
			&& Session.ValidateMerchantDeckOp(selectedOp, selectedSlot, selectedDeckIndex, selectedTargetSlot, out price, out blockReason);
		opsCostLabel.Text = ok
			? $"选中卡：{selected} · 费用 {price} 金币"
			: $"选中卡：{selected} · {blockReason}";
		opsConfirmButton.Disabled = !ok;
	}

	private void OnOpsPressed(MerchantDeckOp op)
	{
		selectedOp = op;
		selectedTargetSlot = -1;
		RefreshDeckOps();
	}

	private void OnOpsSlotPressed(int slot)
	{
		// 转移：源槽位不变，点别的槽位 = 选目标槽位（§4.8 角色列口径）。
		if (selectedOp == MerchantDeckOp.Transfer && slot != selectedSlot)
		{
			selectedTargetSlot = slot;
		}
		else
		{
			selectedSlot = slot;
			selectedDeckIndex = -1;
			selectedTargetSlot = -1;
		}

		RefreshDeckOps();
	}

	private void OnOpsDeckCardPressed(int deckIndex)
	{
		selectedDeckIndex = deckIndex;
		RefreshDeckOps();
	}

	/// <summary>点「确认」：走 `RunSession.TryMerchantDeckOp` 结算一次卡牌操作（失败不改任何状态）。</summary>
	private bool ConfirmDeckOp()
	{
		if (Session == null)
		{
			SetOpsHint("没有进行中的本局。");
			return false;
		}

		if (selectedDeckIndex < 0)
		{
			SetOpsHint("先在卡组列选一张卡。");
			return false;
		}

		bool ok = Session.TryMerchantDeckOp(selectedOp, selectedSlot, selectedDeckIndex, selectedTargetSlot, out string error);
		SetOpsHint(ok ? $"已完成：{OpName(selectedOp)}。" : error);
		RefreshHeader();
		RefreshPacks();
		RefreshDeckOps();
		return ok;
	}

	/// <summary>**AI 接口用**：直接做一次卡牌操作（= 选操作 / 槽位 / 卡 / 目标槽位，再点「确认」）。</summary>
	public bool DeckOp(MerchantDeckOp op, int slot, int deckIndex, int targetSlot)
	{
		selectedOp = op;
		selectedSlot = slot;
		selectedDeckIndex = deckIndex;
		selectedTargetSlot = targetSlot;
		RefreshDeckOps();
		return ConfirmDeckOp();
	}

	/// <summary>打开锻造炉（复用村庄 `SmithyUi`，注入 `SmithyContext.MerchantForge`：次数 1 / 金币 ×1.5）。</summary>
	public void OpenForge()
	{
		if (smithy == null || !GodotObject.IsInstanceValid(smithy))
		{
			return;
		}

		smithy.Open(SmithyContext.MerchantForge, null);
	}

	private static string OpName(MerchantDeckOp op) => op switch
	{
		MerchantDeckOp.Remove => "删除卡牌",
		MerchantDeckOp.Change => "变化卡牌",
		MerchantDeckOp.Transfer => "转移卡牌",
		_ => "升级卡牌",
	};

	/// <summary>卡名（全卡表已在 `LoadingSystem.EnsureAllCardsLoaded` 里读齐；查不到退回 `卡 #Id`）。</summary>
	private static string CardName(int cardId)
	{
		Dictionary<int, Card> cards = LoadingSystem.CardDictionary;
		return cards != null && cards.TryGetValue(cardId, out Card card) && card != null && !string.IsNullOrWhiteSpace(card.CardName)
			? card.CardName
			: $"卡 #{cardId}";
	}

	private static CardTier CardTierOf(int cardId)
	{
		Dictionary<int, Card> cards = LoadingSystem.CardDictionary;
		return cards != null && cards.TryGetValue(cardId, out Card card) && card != null ? card.Tier : CardTier.None;
	}

	private static int CardCostOf(int cardId)
	{
		Dictionary<int, Card> cards = LoadingSystem.CardDictionary;
		return cards != null && cards.TryGetValue(cardId, out Card card) && card != null ? card.EnergyCost : 0;
	}

	/// <summary>等级字母（D / C / B / A / S；没有等级数据时打 `?`）。</summary>
	private static string TierLetter(CardTier tier) => tier == CardTier.None ? "?" : tier.ToString();
}
