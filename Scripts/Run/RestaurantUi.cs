// RestaurantUi.cs
// 村庄餐厅的三页签模态界面（餐厅交互案 §二 / §三 / §四 / §五）：
//   购买（4 格食物货架）/ 烹饪（「材料 → 食物」配方）/ 出售（背包食物按实例，现做 ×1.5）。
// 规则不住这里：买 / 卖价格、现做加成、防套利、次数与**每次操作的时间点代价**在 `RestaurantTrade`（纯逻辑、已单测）；
//   金币 / 背包 / 实例的增删在 `RunSession.Place.cs`（`TryBuyFood` / `TryCookAtRestaurant` / `TrySellFood`）。
// **2026-10-05 第四轮口径**：点菜（购买页「买入」）与现做（烹饪）都算一次「操作」——各收一次
//   全局数据表 `VillageOperationTimePointCost`（默认 0.1）的时间点；点菜另按菜价收金币，烹饪只吃材料。
//   出售是纯交易，不收时间点。
// 本批的两处实现口径（已在文件头登记，另写进施工文档）：
//   · 价目：`RestaurantTrade.FoodBuyPrice` 与商人 `MerchantPrice.csv` 的 Food 行同值；商人场景批接表后再收回表里。
//   · 「放行哪些配方」：**不改 `FoodRecipe.csv` 的 `Enabled`**（营地按 Enabled 列表，改它会把 7 条材料配方漏进营地），
//     餐厅侧改为「输入全是材料 = 餐厅配方」的判据（餐厅案 §七 的两个门之一）。
// 界面挂 `RunUiLayers.Modal`，单实例；`Esc` / `关闭` 退出，退出时清掉全部「现做」标（餐厅案 §五）。
using Godot;
using static Godot.Control;
using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;

public partial class RestaurantUi : Node
{
	public const string CloseText = "关闭";
	public const string BuyTabText = "购买";
	public const string CookTabText = "烹饪";
	public const string SellTabText = "出售";
	public const string BuyText = "买入";
	public const string CookText = "烹饪";
	public const string SellText = "卖出";
	public const string FreshMarkText = "现做";
	public const string TimeShortText = "时间点不足";
	public const string DefaultHintText = "购买（点菜）：每次消耗少量时间点 + 菜价，点「买入」立刻入背包；烹饪：选配方再点「烹饪」；出售：只收食物，现做的卖价更高。";

	private CanvasLayer layer;
	private Control root;
	private Label titleLabel;
	private Label hintLabel;
	private Button buyTabButton;
	private Button cookTabButton;
	private Button sellTabButton;
	private VBoxContainer buyList;
	private VBoxContainer cookList;
	private VBoxContainer sellList;
	private Control buyPage;
	private Control cookPage;
	private Control sellPage;

	private readonly List<ShelfSlot> shelf = new List<ShelfSlot>();
	private readonly RestaurantTrade.FreshMarks freshMarks = new RestaurantTrade.FreshMarks();
	private FoodRecipeDefinition selectedRecipe;
	private int cooksLeft = RestaurantTrade.CooksPerVisit;
	private Action onGoldChanged;
	private Action onClose;
	private TabKind activeTab = TabKind.Buy;

	public enum TabKind
	{
		Buy = 0,
		Cook = 1,
		Sell = 2,
	}

	private sealed class ShelfSlot
	{
		public int FoodKey;
		public int Price;
		public bool Sold;
	}

	/// <summary>界面是否打开（单实例判据）。</summary>
	public bool IsOpen => root != null && GodotObject.IsInstanceValid(root) && root.Visible;

	public int CooksLeft => cooksLeft;
	public string HintText => hintLabel?.Text ?? string.Empty;
	public int ShelfCount => shelf.Count;
	public int ShelfSoldCount => shelf.Count(x => x.Sold);
	public int BuyRowCount => buyList?.GetChildCount() ?? 0;
	public int CookRowCount => cookList?.GetChildCount() ?? 0;
	public int SellRowCount => sellList?.GetChildCount() ?? 0;
	public TabKind ActiveTab => activeTab;
	public int FreshMarkCount => freshMarks.Count;

	/// <summary>食物货架（索引 = 界面行序；只读快照按行序出）。</summary>
	public IEnumerable<(int FoodKey, int Price, bool Sold)> ShelfEntries
	{
		get
		{
			foreach (ShelfSlot slot in shelf)
			{
				yield return (slot.FoodKey, slot.Price, slot.Sold);
			}
		}
	}

	/// <summary>当前选中的配方 Id（-1 = 未选）。</summary>
	public int SelectedRecipeId => selectedRecipe?.RecipeId ?? -1;

	/// <summary>**AI 接口用**：买货架上第 `index` 件（= 点那一行的「买入」）。</summary>
	public bool TriggerBuyAt(int index)
	{
		if (index < 0 || index >= shelf.Count)
		{
			SetHint("货架上没有这一格（按界面行序数）。");
			return false;
		}

		ShelfSlot slot = shelf[index];
		if (slot.Sold)
		{
			SetHint(RestaurantTrade.SoldOutText);
			return false;
		}

		BuySlot(slot);
		return true;
	}

	/// <summary>**AI 接口用**：按配方 Id 选中并烹饪（= 点列表里那一行再点「烹饪」）。</summary>
	public bool TriggerCookByRecipeId(int recipeId)
	{
		FoodRecipeDefinition recipe = CookableRecipes().FirstOrDefault(x => x.RecipeId == recipeId);
		if (recipe == null)
		{
			SetHint($"材料配方表里没有配方 {recipeId}（可用配方按列表行序数）。");
			return false;
		}

		selectedRecipe = recipe;
		CookSelected();
		return true;
	}

	/// <summary>**AI 接口用**：卖出指定实例（= 点那一行的「卖出」）。</summary>
	public bool TriggerSellByInstanceId(string instanceId)
	{
		RunBagEntrySave entry = Run?.BagEntries?.FirstOrDefault(
			x => x != null && string.Equals(x.InstanceId, instanceId, StringComparison.Ordinal));
		if (entry == null)
		{
			SetHint("背包里没有这件食物（实例键见 run.bag.state）。");
			return false;
		}

		SellEntry(entry);
		return true;
	}

	/// <summary>可烹饪的配方 Id（升序；与烹饪页列表行序一致）。</summary>
	public IReadOnlyList<int> CookableRecipeIds
	{
		get
		{
			List<int> ids = new List<int>();
			foreach (FoodRecipeDefinition recipe in CookableRecipes())
			{
				ids.Add(recipe.RecipeId);
			}

			return ids;
		}
	}

	private static RunSession Session => RunSession.Instance;
	private static RunSaveData Run => RunSession.Instance?.Current;

	public override void _Ready()
	{
		layer = new CanvasLayer { Layer = RunUiLayers.Modal };
		AddChild(layer);
		BuildUi();
	}

	/// <summary>打开界面：次数与货架按本次进入重置；上一次的「现做」标在关闭时已清空。</summary>
	public void Open(Action goldChanged, Action closed)
	{
		onGoldChanged = goldChanged;
		onClose = closed;
		cooksLeft = RestaurantTrade.CooksPerVisit;
		selectedRecipe = null;
		BuildShelf();
		ShowTab(TabKind.Buy);
		root.Visible = true;
		RefreshTitle();
		SetHint(DefaultHintText);
	}

	/// <summary>关闭界面：清掉全部「现做」标（餐厅案 §五：离开餐厅即失效）。</summary>
	public void Close()
	{
		freshMarks.Clear();
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
		if (IsOpen && inputEvent is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
		{
			Close();
			GetViewport()?.SetInputAsHandled();
		}
	}

	private void RefreshTitle() =>
		titleLabel.Text = $"餐厅　·　金币 {Run?.Gold ?? 0}　·　本次还可烹饪 {cooksLeft} 次"
			+ $"　·　时间点 {RunTimePoints.Format(Run?.MapState?.RemainingToday ?? 0f)}";

	private void SetHint(string text)
	{
		if (hintLabel != null)
		{
			hintLabel.Text = text ?? string.Empty;
		}

		onGoldChanged?.Invoke();
	}

	// ── 货架（餐厅案 §三：4 格、一格一件、已售出不补位）──

	private void BuildShelf()
	{
		shelf.Clear();
		List<int> keys = new List<int>(LoadingSystem.LoadFoodsByKey().Keys);
		keys.Sort();
		if (keys.Count == 0)
		{
			return;
		}

		Random random = VillagePlaceData.RandomFor(Run, 0x7A57);
		List<int> picked = new List<int>();
		int guard = 0;
		while (picked.Count < RestaurantTrade.BuyShelfSlots && guard++ < 200)
		{
			int rarity = RollShelfRarity(random);
			int foodKey = PickFoodInRarity(random, keys, rarity, picked);
			if (foodKey > 0)
			{
				picked.Add(foodKey);
			}
		}

		foreach (int foodKey in picked)
		{
			shelf.Add(new ShelfSlot
			{
				FoodKey = foodKey,
				Price = RestaurantTrade.FoodBuyPrice(ItemNameResolver.RarityOf(BagCategory.Food, foodKey)),
				Sold = false,
			});
		}
	}

	/// <summary>抽档：权重沿用商人货架的 `MerchantStock.RarityWeights`（B17 默认 70 / 25 / 5）。</summary>
	private static int RollShelfRarity(Random random)
	{
		int total = MerchantStock.TotalWeight;
		if (total <= 0)
		{
			return (int)ItemRarity.Common;
		}

		int roll = random.Next(total);
		foreach ((ItemRarity rarity, int weight) in MerchantStock.RarityWeights)
		{
			roll -= Math.Max(0, weight);
			if (roll < 0)
			{
				return (int)rarity;
			}
		}

		return (int)ItemRarity.Common;
	}

	private static int PickFoodInRarity(Random random, List<int> keys, int rarity, List<int> exclude)
	{
		List<int> matches = keys.Where(x => !exclude.Contains(x) && ItemNameResolver.RarityOf(BagCategory.Food, x) == rarity).ToList();
		if (matches.Count == 0)
		{
			matches = keys.Where(x => !exclude.Contains(x)).ToList();
		}

		return matches.Count == 0 ? -1 : matches[random.Next(matches.Count)];
	}

	// ── 界面构建 ──

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
		panel.CustomMinimumSize = new Vector2(860f, 560f);
		root.AddChild(panel);

		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		VBoxContainer column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 10);
		margin.AddChild(column);

		HBoxContainer headerRow = new HBoxContainer();
		column.AddChild(headerRow);

		titleLabel = new Label { Text = string.Empty, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		titleLabel.AddThemeFontSizeOverride("font_size", 18);
		headerRow.AddChild(titleLabel);

		Button closeButton = new Button { Text = CloseText };
		closeButton.Pressed += Close;
		headerRow.AddChild(closeButton);

		HBoxContainer tabRow = new HBoxContainer();
		tabRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(tabRow);

		buyTabButton = new Button { Text = BuyTabText };
		buyTabButton.Pressed += () => ShowTab(TabKind.Buy);
		tabRow.AddChild(buyTabButton);

		cookTabButton = new Button { Text = CookTabText };
		cookTabButton.Pressed += () => ShowTab(TabKind.Cook);
		tabRow.AddChild(cookTabButton);

		sellTabButton = new Button { Text = SellTabText };
		sellTabButton.Pressed += () => ShowTab(TabKind.Sell);
		tabRow.AddChild(sellTabButton);

		column.AddChild(new HSeparator());

		// 三个页签各自独立滚动（餐厅案 §二），共用同一条底部提示行。
		buyPage = BuildPage(out buyList);
		cookPage = BuildPage(out cookList);
		sellPage = BuildPage(out sellList);
		column.AddChild(buyPage);
		column.AddChild(cookPage);
		column.AddChild(sellPage);

		column.AddChild(new HSeparator());

		hintLabel = new Label { Text = DefaultHintText, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		hintLabel.AddThemeFontSizeOverride("font_size", 14);
		column.AddChild(hintLabel);
	}

	private static Control BuildPage(out VBoxContainer list)
	{
		ScrollContainer scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false };
		list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(list);
		return scroll;
	}

	private void ShowTab(TabKind tab)
	{
		activeTab = tab;
		buyPage.Visible = tab == TabKind.Buy;
		cookPage.Visible = tab == TabKind.Cook;
		sellPage.Visible = tab == TabKind.Sell;

		switch (tab)
		{
			case TabKind.Buy:
				RefreshBuyTab();
				break;
			case TabKind.Cook:
				RefreshCookTab();
				break;
			default:
				RefreshSellTab();
				break;
		}

		RefreshTitle();
	}

	private static void ClearList(VBoxContainer list)
	{
		if (list == null)
		{
			return;
		}

		foreach (Node child in list.GetChildren())
		{
			list.RemoveChild(child);
			child.QueueFree();
		}
	}

	private static Label MakeRow(string text, Color? color = null)
	{
		Label label = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		label.AddThemeFontSizeOverride("font_size", 14);
		if (color.HasValue)
		{
			label.AddThemeColorOverride("font_color", color.Value);
		}

		return label;
	}

	// ── 购买页（餐厅案 §三）──

	private void RefreshBuyTab()
	{
		ClearList(buyList);
		if (shelf.Count == 0)
		{
			buyList.AddChild(MakeRow("（食物表为空）"));
			return;
		}

		float remaining = Run?.MapState?.RemainingToday ?? 0f;
		bool timeShort = !RestaurantTrade.CanOrder(remaining);
		if (timeShort)
		{
			// 点菜也是一次操作（餐厅案 §三 / §四）：时间点不足 → 按钮禁用 + 一行去处指引。
			buyList.AddChild(MakeRow(RestaurantTrade.OrderTimePointShortText(remaining), new Color(1f, 0.45f, 0.45f)));
		}

		foreach (ShelfSlot slot in shelf)
		{
			HBoxContainer row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);

			string text = $"{ItemNameResolver.Food(slot.FoodKey)}　{RarityText(ItemNameResolver.RarityOf(BagCategory.Food, slot.FoodKey))}"
				+ $"　饱食 {ItemNameResolver.FoodSatietyOf(slot.FoodKey)}　{slot.Price} 金币"
				+ $"　时间点 {RunTimePoints.Format(RestaurantTrade.OrderTimePointCost)}";
			row.AddChild(MakeRow(slot.Sold ? $"{text}（已售出）" : text, slot.Sold ? Colors.Gray : (Color?)null));

			ShelfSlot captured = slot;
			Button buy = new Button { Text = timeShort ? TimeShortText : BuyText, Disabled = slot.Sold || timeShort };
			buy.Pressed += () => BuySlot(captured);
			row.AddChild(buy);
			buyList.AddChild(row);
		}
	}

	private void BuySlot(ShelfSlot slot)
	{
		if (slot == null || slot.Sold)
		{
			SetHint(RestaurantTrade.SoldOutText);
			return;
		}

		RunSession session = Session;
		string error = string.Empty;
		if (session == null || !session.TryBuyFood(slot.FoodKey, slot.Price, out error))
		{
			SetHint(string.IsNullOrEmpty(error) ? "没有进行中的本局。" : error);
			return;
		}

		slot.Sold = true;   // 已售出不补位、不重排（餐厅案 §三）
		SetHint($"买入 {ItemNameResolver.Food(slot.FoodKey)}（{slot.Price} 金币 · {RunTimePoints.Format(RestaurantTrade.OrderTimePointCost)} 时间点）。");
		RefreshBuyTab();
		RefreshTitle();     // 点菜收了时间点 → 标题行的时间点要跟着变
	}

	// ── 出售页（餐厅案 §五：只收食物，现做的卖价 ×1.5）──

	private void RefreshSellTab()
	{
		ClearList(sellList);
		RunSaveData run = Run;
		if (run == null)
		{
			sellList.AddChild(MakeRow("没有进行中的本局。", Colors.Gray));
			return;
		}

		List<RunBagEntrySave> entries = RunBagSystem.EntriesOf(run, BagCategory.Food);
		if (entries.Count == 0)
		{
			sellList.AddChild(MakeRow("背包里没有食物。", Colors.Gray));
			return;
		}

		foreach (RunBagEntrySave entry in entries)
		{
			if (entry == null)
			{
				continue;
			}

			bool fresh = freshMarks.IsFresh(entry.InstanceId);
			int price = RestaurantTrade.SellPrice(RestaurantTrade.FoodBuyPrice(entry.Rarity), fresh);
			string text = $"{ItemNameResolver.Food(entry.DefinitionKey)}　×{entry.Count}　{RarityText(entry.Rarity)}"
				+ $"　有效期 {entry.ExpireDaysRemaining} 天　售价 {price} 金币"
				+ (fresh ? $"　【{FreshMarkText}】" : string.Empty);

			HBoxContainer row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			row.AddChild(MakeRow(text, fresh ? new Color(1f, 0.9f, 0.45f) : (Color?)null));

			RunBagEntrySave captured = entry;
			Button sell = new Button { Text = SellText };
			sell.Pressed += () => SellEntry(captured);
			row.AddChild(sell);
			sellList.AddChild(row);
		}
	}

	private void SellEntry(RunBagEntrySave entry)
	{
		if (entry == null)
		{
			return;
		}

		RunSession session = Session;
		if (session == null)
		{
			SetHint("没有进行中的本局。");
			return;
		}

		bool fresh = freshMarks.IsFresh(entry.InstanceId);
		int price = RestaurantTrade.SellPrice(RestaurantTrade.FoodBuyPrice(entry.Rarity), fresh);
		string name = ItemNameResolver.Food(entry.DefinitionKey);
		if (!session.TrySellFood(entry.InstanceId, price, out string error))
		{
			SetHint(error);
			return;
		}

		SetHint($"卖出 {name}（{price} 金币{(fresh ? "，现做加成" : string.Empty)}）。");
		RefreshSellTab();
	}

	// ── 烹饪页（餐厅案 §四 / §六：只列「材料 → 食物」配方）──

	/// <summary>
	/// 餐厅配方 = 输入**全是材料**的配方（本批 7 条）。
	/// 判据不读 `Enabled`：`FoodRecipe.csv` 里这 7 行仍是 `Enabled=FALSE`，而营地是按 `Enabled` 列配方的 ——
	/// 直接打开会把材料配方漏进营地（营地只做「食物 → 食物」）。餐厅案 §七 的两个门之一即此判据（详见文件头）。
	/// </summary>
	private static List<FoodRecipeDefinition> CookableRecipes() =>
		LoadingSystem.LoadFoodRecipesByKey().Values
			.Where(x => x != null && x.Inputs.Count > 0
				&& x.Inputs.All(input => input != null && input.Kind == RecipeInputKind.Material))
			.OrderBy(x => x.RecipeId)
			.ToList();

	private static string CookLine(FoodRecipeDefinition recipe) =>
		$"{ItemNameResolver.Food(recipe.ResultFoodId)}　×{recipe.ResultCount}"
		+ $"　{RarityText(ItemNameResolver.RarityOf(BagCategory.Food, recipe.ResultFoodId))}"
		+ $"　饱食 {ItemNameResolver.FoodSatietyOf(recipe.ResultFoodId)}";

	private void RefreshCookTab()
	{
		ClearList(cookList);
		List<FoodRecipeDefinition> recipes = CookableRecipes();
		if (recipes.Count == 0)
		{
			cookList.AddChild(MakeRow("（材料配方表为空）", Colors.Gray));
			return;
		}

		if (selectedRecipe == null || !recipes.Exists(x => x.RecipeId == selectedRecipe.RecipeId))
		{
			selectedRecipe = recipes[0];
		}

		foreach (FoodRecipeDefinition recipe in recipes)
		{
			FoodRecipeDefinition captured = recipe;
			bool isSelected = captured.RecipeId == selectedRecipe.RecipeId;
			Button button = new Button { Text = (isSelected ? "▶ " : string.Empty) + CookLine(recipe), Alignment = HorizontalAlignment.Left };
			button.Pressed += () =>
			{
				selectedRecipe = captured;
				RefreshCookTab();
			};
			cookList.AddChild(button);
		}

		List<SmithyRequirement> requirements = new List<SmithyRequirement>();
		if (Session != null)
		{
			requirements = Session.RestaurantRequirements(selectedRecipe, out _);
		}

		foreach (SmithyRequirement requirement in requirements)
		{
			cookList.AddChild(MakeRow($"{requirement.DisplayName} ×{requirement.Need}（持有 {requirement.Have}）",
				requirement.Satisfied ? (Color?)null : new Color(1f, 0.45f, 0.45f)));
		}

		float remaining = Run?.MapState?.RemainingToday ?? 0f;
		bool timeShort = !RestaurantTrade.CanCook(remaining);
		HBoxContainer row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(MakeRow($"本次还可烹饪 {cooksLeft} / {RestaurantTrade.CooksPerVisit} 次"));
		if (timeShort)
		{
			// 时间点不足是硬门槛（餐厅案 §一）：按钮禁用 + 一行去处指引（旅馆 / 民宿过夜）。
			cookList.AddChild(MakeRow(RestaurantTrade.CookTimePointShortText(remaining), new Color(1f, 0.45f, 0.45f)));
		}

		Button cook = new Button { Text = timeShort ? TimeShortText : CookText, Disabled = cooksLeft <= 0 || timeShort };
		cook.Pressed += CookSelected;
		row.AddChild(cook);
		cookList.AddChild(row);
	}

	private void CookSelected()
	{
		if (selectedRecipe == null)
		{
			SetHint("先选一条配方。");
			return;
		}

		RunSession session = Session;
		if (session == null)
		{
			SetHint("没有进行中的本局。");
			return;
		}

		if (!session.TryCookAtRestaurant(selectedRecipe, cooksLeft, out RunBagEntrySave result, out string error))
		{
			SetHint(error);
			return;
		}

		cooksLeft = Math.Max(0, cooksLeft - 1);
		if (result != null)
		{
			freshMarks.Mark(result.InstanceId);   // 本次进入的产物打「现做」标（离开餐厅失效）
		}

		SetHint($"现做完成：{ItemNameResolver.Food(selectedRecipe.ResultFoodId)}（已打「{FreshMarkText}」标）。");
		RefreshCookTab();
		RefreshTitle();
	}

	private static string RarityText(int rarity) => rarity switch
	{
		2 => "稀有",
		1 => "罕见",
		_ => "普通",
	};

	// ── 烟测通道 ──

	/// <summary>**烟测用**：切页签（`buy` / `cook` / `sell`）。</summary>
	public bool ShowTabForSmoke(string tab)
	{
		if (string.Equals(tab, "buy", StringComparison.OrdinalIgnoreCase))
		{
			ShowTab(TabKind.Buy);
			return true;
		}

		if (string.Equals(tab, "cook", StringComparison.OrdinalIgnoreCase))
		{
			ShowTab(TabKind.Cook);
			return true;
		}

		if (string.Equals(tab, "sell", StringComparison.OrdinalIgnoreCase))
		{
			ShowTab(TabKind.Sell);
			return true;
		}

		return false;
	}

	/// <summary>**烟测用**：买货架上第一件未售出的食物。</summary>
	public bool TriggerBuyFirstUnsold()
	{
		ShelfSlot slot = shelf.FirstOrDefault(x => !x.Sold);
		if (slot == null)
		{
			SetHint(RestaurantTrade.SoldOutText);
			return false;
		}

		BuySlot(slot);
		return true;
	}

	/// <summary>**烟测用**：烹饪当前选中的配方（与玩家点 `烹饪` 同一条路）。</summary>
	public bool TriggerCookSelected()
	{
		if (selectedRecipe == null)
		{
			return false;
		}

		CookSelected();
		return true;
	}

	/// <summary>**烟测用**：卖出背包里第一条食物实例。</summary>
	public bool TriggerSellFirst()
	{
		RunBagEntrySave entry = Run?.BagEntries?.FirstOrDefault(x => x != null && x.CategoryEnum == BagCategory.Food);
		if (entry == null)
		{
			return false;
		}

		SellEntry(entry);
		return true;
	}

	/// <summary>**烟测用**：点 `关闭`。</summary>
	public void TriggerClose() => Close();
}