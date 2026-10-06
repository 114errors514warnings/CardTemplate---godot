// SmithyUi.cs
// 锻铁铺 / 锻造炉的模态界面（锻铁铺交互案 §二 / §2.1 / §四 / §五）。
// **一份实现、两处调用**：村庄锻铁铺与商人锻造炉共用本界面，差异全部走 `SmithyContext` 注入
//   （标题 / 次数 / 金币系数 / 费用文案）；界面代码里不出现「在村庄 / 在商人」这类分支。
// 规则不住这里：时间点 → 次数 → 材料 → 金币 → 背包的校验与扣料 / 扣金币 / 落档在
//   `RunSession.TryCraftEquipment`（唯一出口）+ `SmithyCrafting`（纯逻辑、已单测，装备负荷在
//   `SmithyCrafting.EquipmentLoad`）；**2026-10-05 第四轮口径：进入设施本身不是操作；
//   每次打造 = 一次「操作」，收全局数据表 `VillageOperationTimePointCost`（默认 0.1）的时间点 + 配方金币**。
//   本文件只负责列表 / 详情 / 提示行与按钮可用性（界面内不出现「在村庄 / 在商人」分支）。
using Godot;
using static Godot.Control;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>锻铁铺界面的注入项（锻铁铺案 §2.1 的字段表）。</summary>
public sealed class SmithyContext
{
	/// <summary>标题行左侧文案。</summary>
	public string Title = "锻铁铺";

	/// <summary>本次进入可打造次数。</summary>
	public int MaxCrafts = SmithyCrafting.VillageMaxCrafts;

	/// <summary>费用行金币系数（村庄 ×1、商人 ×1.5，向上取整到 5）。</summary>
	public float GoldMultiplier = SmithyCrafting.VillageGoldMultiplier;

	/// <summary>右列费用说明文案。</summary>
	public string CurrencyNote = "费用：材料 + 金币";

	/// <summary>
	/// 时间点不足、不能打造时的去处指引（村庄 = 旅馆 / 民宿过夜；商人 = 回营地结束当天）——
	/// 场景差异只走注入，界面里不出现「在村庄 / 在商人」分支（§2.1）。
	/// </summary>
	public string TimePointRestHint = RunFacilityCosts.RestHint;

	/// <summary>村庄锻铁铺预设（次数 2 / 金币 ×1）。</summary>
	public static SmithyContext VillageSmithy => new SmithyContext();

	/// <summary>商人锻造炉预设（次数 1 / 金币 ×1.5，商人交互案 §4.9）。</summary>
	public static SmithyContext MerchantForge => new SmithyContext
	{
		Title = "锻造炉",
		MaxCrafts = SmithyCrafting.MerchantMaxCrafts,
		GoldMultiplier = SmithyCrafting.MerchantGoldMultiplier,
		CurrencyNote = "费用：材料 + 金币（商人工匠费 ×1.5）",
		TimePointRestHint = "请回营地结束当天。",
	};
}

public partial class SmithyUi : Node
{
	public const string CloseText = "关闭";
	public const string CraftText = "打造";
	public const string TimeShortText = "时间点不足";
	public const string DefaultHintText = "选一件配方，看右列的材料与费用，再点「打造」。";
	public const string OneHandText = "单手";
	public const string TwoHandsText = "双手";

	private CanvasLayer layer;
	private Control root;
	private Label titleLabel;
	private Label hintLabel;
	private Label detailNameLabel;
	private Label detailAttrLabel;
	private VBoxContainer recipeList;
	private VBoxContainer requirementList;
	private Label costLabel;
	private Button craftButton;
	private readonly List<Button> recipeButtons = new List<Button>();
	private SmithyContext context = SmithyContext.VillageSmithy;
	private Action onClose;
	private int craftsLeft;
	private EquipmentRecipe selected;

	/// <summary>界面是否打开（单实例判据）。</summary>
	public bool IsOpen => root != null && GodotObject.IsInstanceValid(root) && root.Visible;

	public int CraftsLeft => craftsLeft;
	public string HintText => hintLabel?.Text ?? string.Empty;
	public int RecipeCount => recipeButtons.Count;
	public string SelectedDisplayName => selected?.ResultDefinitionId ?? string.Empty;
	public string CostText => costLabel?.Text ?? string.Empty;
	public bool CraftDisabled => craftButton == null || craftButton.Disabled;

	/// <summary>可选配方的产物定义名（升序，与列表行序一致；只读快照按行序出）。</summary>
	public IReadOnlyList<string> RecipeDefinitionIds
	{
		get
		{
			List<string> ids = new List<string>();
			foreach (EquipmentRecipe recipe in RecipeSource())
			{
				ids.Add(recipe.ResultDefinitionId);
			}

			return ids;
		}
	}

	/// <summary>当前选中的配方（产物定义名；未选时为空串）。</summary>
	public string SelectedDefinitionId => selected?.ResultDefinitionId ?? string.Empty;

	/// <summary>本次进入已打造次数（烟测断言「次数 −1」用）。</summary>
	public int CraftedThisVisit => Math.Max(0, context.MaxCrafts - craftsLeft);

	private static RunSession Session => RunSession.Instance;
	private static RunSaveData Run => RunSession.Instance?.Current;

	public override void _Ready()
	{
		layer = new CanvasLayer { Layer = RunUiLayers.Modal };
		AddChild(layer);
		BuildUi();
	}

	/// <summary>打开界面：次数重置为注入值（锻铁铺案 §一「每次进入 2 次」）。</summary>
	public void Open(SmithyContext injected, Action closed)
	{
		context = injected ?? SmithyContext.VillageSmithy;
		onClose = closed;
		craftsLeft = context.MaxCrafts;
		selected = null;
		RefreshRecipes();
		SelectRecipe(FirstEnabledRecipe());
		SetHint(DefaultHintText);
		root.Visible = true;
		RefreshTitle();
	}

	/// <summary>关闭界面（`Esc` / `关闭` 同一个出口）。</summary>
	public void Close()
	{
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
		titleLabel.Text = $"{context.Title}　·　本次可打造 {craftsLeft} / {context.MaxCrafts}　·　金币 {Run?.Gold ?? 0}"
			+ $"　·　时间点 {RunTimePoints.Format(Run?.MapState?.RemainingToday ?? 0f)}";

	private void SetHint(string text)
	{
		if (hintLabel != null)
		{
			hintLabel.Text = text ?? string.Empty;
		}
	}

	private static readonly Color ShortageColor = new Color(1f, 0.45f, 0.45f, 1f);

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
		panel.CustomMinimumSize = new Vector2(900f, 560f);
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

		column.AddChild(new HSeparator());

		HBoxContainer body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 14);
		column.AddChild(body);

		// 左列（40%）：配方列表，一行一配方（锻铁铺案 §二：材料 / 金币不足的配方仍可选中）。
		ScrollContainer leftScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		leftScroll.SizeFlagsStretchRatio = 0.4f;
		body.AddChild(leftScroll);

		recipeList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		recipeList.AddThemeConstantOverride("separation", 4);
		leftScroll.AddChild(recipeList);

		// 右列（60%）：选中配方的属性 / 材料需求 / 费用 / 打造按钮。
		VBoxContainer right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		right.SizeFlagsStretchRatio = 0.6f;
		right.AddThemeConstantOverride("separation", 8);
		body.AddChild(right);

		detailNameLabel = new Label { Text = string.Empty };
		detailNameLabel.AddThemeFontSizeOverride("font_size", 16);
		right.AddChild(detailNameLabel);

		detailAttrLabel = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		detailAttrLabel.AddThemeFontSizeOverride("font_size", 14);
		right.AddChild(detailAttrLabel);

		Label materialTitle = new Label { Text = "材料：" };
		materialTitle.AddThemeFontSizeOverride("font_size", 14);
		right.AddChild(materialTitle);

		ScrollContainer requirementScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		right.AddChild(requirementScroll);

		requirementList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		requirementList.AddThemeConstantOverride("separation", 2);
		requirementScroll.AddChild(requirementList);

		costLabel = new Label { Text = string.Empty };
		costLabel.AddThemeFontSizeOverride("font_size", 14);
		right.AddChild(costLabel);

		craftButton = new Button { Text = CraftText };
		craftButton.Pressed += OnCraftPressed;
		right.AddChild(craftButton);

		column.AddChild(new HSeparator());

		hintLabel = new Label { Text = DefaultHintText, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		hintLabel.AddThemeFontSizeOverride("font_size", 14);
		column.AddChild(hintLabel);
	}

	// ── 配方列表与详情 ──

	/// <summary>配方来源：`EquipmentRecipe.csv` 的现役行（`Enabled = TRUE`），按 `RecipeId` 升序。</summary>
	private static List<EquipmentRecipe> RecipeSource() =>
		LoadEquipmentRecipeCsv.LoadByKey().Values.Where(x => x != null && x.Enabled).OrderBy(x => x.RecipeId).ToList();

	private static EquipmentRecipe FirstEnabledRecipe()
	{
		List<EquipmentRecipe> recipes = RecipeSource();
		return recipes.Count > 0 ? recipes[0] : null;
	}

	private void RefreshRecipes()
	{
		foreach (Node child in recipeList.GetChildren())
		{
			recipeList.RemoveChild(child);
			child.QueueFree();
		}

		recipeButtons.Clear();
		foreach (EquipmentRecipe recipe in RecipeSource())
		{
			EquipmentRecipe captured = recipe;
			Button button = new Button { Text = RecipeLine(recipe), Alignment = HorizontalAlignment.Left };
			button.Pressed += () => SelectRecipe(captured);
			recipeList.AddChild(button);
			recipeButtons.Add(button);
		}
	}

	private string RecipeLine(EquipmentRecipe recipe)
	{
		int price = SmithyCrafting.GoldFor(recipe.Gold, context.GoldMultiplier);
		return $"{recipe.ResultDefinitionId}　{HandsText(recipe.ResultDefinitionId)}　{price} 金币";
	}

	/// <summary>选中一件配方（材料 / 金币不足也允许选中，锻铁铺案 §二）。</summary>
	public bool SelectRecipe(EquipmentRecipe recipe)
	{
		selected = recipe;
		RefreshDetails();
		RefreshTitle();
		return recipe != null;
	}

	/// <summary>按产物定义名选中配方（烟测 / API 用）。</summary>
	public bool SelectByDefinitionId(string definitionId)
	{
		EquipmentRecipe recipe = RecipeSource().FirstOrDefault(x => string.Equals(x.ResultDefinitionId, definitionId, StringComparison.Ordinal));
		return recipe != null && SelectRecipe(recipe);
	}

	private void RefreshDetails()
	{
		ClearRequirements();
		if (selected == null)
		{
			detailNameLabel.Text = "（配方表为空）";
			detailAttrLabel.Text = string.Empty;
			costLabel.Text = string.Empty;
			craftButton.Disabled = true;
			return;
		}

		detailNameLabel.Text = selected.ResultDefinitionId;
		detailAttrLabel.Text = AttributeLine(selected.ResultDefinitionId);

		foreach (SmithyRequirement requirement in Requirements(selected))
		{
			Label row = new Label { Text = $"{requirement.DisplayName} ×{requirement.Need}（持有 {requirement.Have}）" };
			row.AddThemeFontSizeOverride("font_size", 14);
			if (!requirement.Satisfied)
			{
				row.AddThemeColorOverride("font_color", ShortageColor);
			}

			requirementList.AddChild(row);
		}

		int price = SmithyCrafting.GoldFor(selected.Gold, context.GoldMultiplier);
		int gold = Run?.Gold ?? 0;
		costLabel.Text = $"{context.CurrencyNote}　{price} 金币（当前 {gold}）"
			+ $"　·　时间点 {RunTimePoints.Format(SmithyCrafting.CraftTimePointCost)}";
		costLabel.AddThemeColorOverride("font_color", gold >= price ? Colors.White : ShortageColor);

		// 时间点不足 / 次数用尽 / 背包超载 → 按钮禁用并给一行说明；材料 / 金币不足保持可点（锻铁铺案 §五）。
		float remaining = Run?.MapState?.RemainingToday ?? 0f;
		bool timeShort = !SmithyCrafting.CanCraft(remaining);
		bool overload = Run != null && RunBagSystem.WouldExceedLoad(Run, SmithyCrafting.EquipmentLoad(selected.ResultDefinitionId));
		if (timeShort)
		{
			// 去处指引随 `SmithyContext` 注入（村庄 = 旅馆 / 民宿；商人 = 回营地）。
			costLabel.Text = $"{context.CurrencyNote}　{price} 金币（当前 {gold}）　·　"
				+ SmithyCrafting.CraftTimePointShortText(remaining, context.TimePointRestHint);
		}

		craftButton.Disabled = craftsLeft <= 0 || overload || timeShort;
		craftButton.Text = craftsLeft <= 0 ? "次数已用尽" : overload ? "背包已超载" : timeShort ? TimeShortText : CraftText;
	}

	private void ClearRequirements()
	{
		foreach (Node child in requirementList.GetChildren())
		{
			requirementList.RemoveChild(child);
			child.QueueFree();
		}
	}

	// ── 打造 ──

	private void OnCraftPressed()
	{
		if (selected == null)
		{
			SetHint("先选一件配方。");
			return;
		}

		RunSession session = Session;
		if (session == null)
		{
			SetHint("没有进行中的本局。");
			return;
		}

		// 结算全部走 `RunSession`（地点场景的唯一出口）：时间点 → 次数 → 材料 → 金币 → 背包，
		// 通过才扣料 / 扣金币 / 落档（锻铁铺案 §四 第 2–3 步；2026-10-05 第四轮口径：
		// 打造一次 = 一次「操作」= 表值时间点（默认 0.1）+ 配方金币）。
		if (!session.TryCraftEquipment(selected, context.GoldMultiplier, craftsLeft, out string error))
		{
			SetHint(error);
			return;
		}

		craftsLeft = Math.Max(0, craftsLeft - 1);
		SetHint(SmithyCrafting.CraftedText(selected.ResultDefinitionId));
		RefreshTitle();
		RefreshDetails();
	}

	/// <summary>配方的材料需求清单（名称走材料表、持有数走背包实例）。</summary>
	public List<SmithyRequirement> Requirements(EquipmentRecipe recipe) =>
		SmithyCrafting.Requirements(recipe, VillagePlaceData.MaterialName,
			id => Run == null ? 0 : RunBagSystem.CountOf(Run, BagCategory.Material, id));

	private static string HandsText(string definitionId) =>
		ItemNameResolver.HandsRequiredOfDefinition(definitionId) >= 2 ? TwoHandsText : OneHandText;

	private static string AttributeLine(string definitionId)
	{
		List<string> parts = new List<string> { HandsText(definitionId) };
		if (ItemNameResolver.TryGetBodySlotOfDefinition(definitionId, out EquipmentSlotKind slot))
		{
			parts.Add($"部位：{slot}");
		}

		if (ItemNameResolver.TryGetEquipmentKey(definitionId, out int key)
			&& LoadingSystem.LoadWeaponsByKey().TryGetValue(key, out WeaponDefinition weapon)
			&& weapon != null)
		{
			parts.Add($"攻击距离 {weapon.AttackRange} / 防御 {weapon.DefenseValue} / 移动 {weapon.MoveBonus}");
		}

		return "属性：" + string.Join("　", parts);
	}

	// ── 烟测通道 ──

	/// <summary>**烟测用**：点一次 `打造`（与玩家点按钮同一条路）。</summary>
	public void TriggerCraft() => OnCraftPressed();

	/// <summary>**烟测用**：点 `关闭`。</summary>
	public void TriggerClose() => Close();
}
