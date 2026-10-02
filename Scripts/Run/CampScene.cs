// CampScene.cs
// 营地（篝火休息界面）：一天结束后的休息交互。
// 交互案：[篝火休息交互案](../../README/施工文档/2026/2026.09/交互/篝火休息交互案.md)；
// 规则：[篝火休息与食物](../../README/玩法说明文档/系统规则/营地系统/篝火休息与食物.md)、[地图玩法 §五](../../README/玩法说明文档/系统规则/地图玩法/地图玩法.md)。
//
// 落地范围（2026-10-02 批 C：食物与烹饪已接入；只剩夜袭为遗留）：
//   ✅ 营地画面与四个独立按钮、守夜三选与实时预览、休息结算（回复 + 推进新一天）、进出转场。
//   ✅ 2026-10-01 同日修复（10 月施工文档 §4）：守夜角色行改「缩进 + 定宽单行标签」（原被 HBoxContainer
//      压成一字一行、与下拉框错位）；背景改在 `_Draw` 自绘（原 ColorRect 子节点把火堆整个盖住）。
//   ✅ 「添加食物」= 篝火草稿：效果饱食度上限 10（首个越限者及其后只给饱食度）、过期食物拒绝放入、
//      点「休息」前不消耗任何食物（从草稿取回即撤销）。
//   ✅ 「烹饪」= 已放行配方（材料通道的 7 条旧配方 Enabled = FALSE）：每次休息 ≤ 2 次，只消耗食物；
//      成菜入背包（篝火休息交互案待确认项默认口径），可立刻加入篝火草稿。
//   ⌛ 夜袭与「睡眠不佳」仍不结算（需"当天最近一次普通敌袭的 50%"遭遇生成与状态牌内容）。
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class CampScene : Control
{
	/// <summary>休息结算完成（已淡出）：宿主 `RunFlowScene` 收尾（销毁营地、回到可选地图）。</summary>
	public event Action RestCompleted;

	/// <summary>嵌入模式（由 `RunFlowScene` 持有）= true：结算完成后只广播事件；独立场景模式自己切回地图场景。</summary>
	public bool EmbeddedMode;

	/// <summary>进出营地的淡入淡出时长（交互案「转场步骤」：淡出地图 → 营地淡入 → 结算后淡出）。</summary>
	[Export] public float FadeSeconds = 0.35f;

	// ── 回复口径（篝火休息与食物 §三）──
	// `RunTimePoints.RestHealRatio` = 基础 10% + 20% ×（剩余时间点 ÷ 4）+ 2% × 饱食度；
	// 饱食度与「仍生效的食物效果」都取自本次篝火草稿 `plan`（空草稿 = 0，与 2026-10-01 的恒 0 等价）。
	/// <summary>「添加食物」按钮文案（烟测按文案定位按钮：改文案必须同步这里）。</summary>
	public const string FoodButtonText = "添加食物";

	/// <summary>「烹饪」按钮文案（烟测按文案定位按钮：改文案必须同步这里）。</summary>
	public const string CookButtonText = "烹饪";

	/// <summary>「守夜」按钮文案。</summary>
	public const string WatchButtonText = "守夜";

	/// <summary>「休息」按钮文案。</summary>
	public const string RestButtonText = "休息";

	public const string MapScenePath = "res://Scenes/Map/MapScene.tscn";

	private RunWatchMode watchMode = RunWatchMode.None;
	private int watcherSlotIndex;
	private bool resolving;
	private ColorRect fade;
	private Label previewLabel, resultLabel, hintLabel;
	private OptionButton watcherPicker;
	private readonly List<Label> partyRows = new List<Label>();
	private Button foodButton, cookButton, restButton, watchButton;
	private Control foodPanel, cookPanel, watchPanel, partyPanel;
	private Label foodProgressLabel, foodHintLabel, cookProgressLabel, cookHintLabel;
	private VBoxContainer fireColumn, bagColumn, cookColumn;

	/// <summary>
	/// 本次篝火的草稿（`RunFoodSystem.CampFirePlan`）：点「休息」前不消耗任何食物 ——
	/// 面板上的「添加 / 取回」只改草稿，「休息」才统一消耗并落效果（篝火休息交互案「添加食物」）。
	/// </summary>
	private readonly RunFoodSystem.CampFirePlan plan = new RunFoodSystem.CampFirePlan();

	/// <summary>三名角色围绕火堆的站位（以火堆为中心的扇形，下标 = 队伍槽位序，与左侧队伍列表同序）。</summary>
	private static readonly Vector2[] PartyOffsets = { new(-150f, 46f), new(0f, -110f), new(150f, 46f) };

	/// <summary>火堆位置（相对本场景尺寸的比例）：落在「守夜面板 / 回复预览」下方的空白区，不与任何文案重叠。</summary>
	private const float FireCenterRatioX = 0.26f;
	private const float FireCenterRatioY = 0.72f;

	/// <summary>背景色：由 `_Draw` 自绘（**不能**用子节点 ColorRect 铺底，见 `_Draw` 注释）。</summary>
	private static readonly Color BackgroundColor = new Color("0b1016");

	/// <summary>火芯颜色：绘制与烟测取色断言（篝火必须可见）共用同一取值。</summary>
	public static readonly Color FireCoreColor = new Color(1f, .93f, .72f, .95f);

	/// <summary>上方复选框文案的缩进：守夜角色行照此对齐，避免标签与下拉框各偏一边。</summary>
	private const float CheckBoxCaptionIndent = 28f;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		RunSession run = RunSession.Instance;
		if (run?.Current == null)
		{
			GD.PrintErr("[营地] 缺少进行中的本局，直接结束休息。");
			FinishRest();
			return;
		}

		// 进营地即记录当刻的当天剩余（主动结束当天时，这部分就是被作废的进程按比例转化的收益来源）。
		run.BeginRestDay();
		BuildUi();
		RefreshPreview();
		PlayFade(1f, 0f, () => { if (fade != null) fade.Visible = false; });
	}

	public override void _Notification(int what)
	{
		// 背景与火堆、站位都是自绘：窗口尺寸变化时 Control 不保证重绘，这里显式排队一次。
		if (what == NotificationResized) QueueRedraw();
	}

	/// <summary>转场黑幕是否已完全退场（烟测等待用：淡入完成前取色 / 截图会取到半黑画面）。</summary>
	public bool FadeFinished => fade != null && !fade.Visible;

	/// <summary>火堆中心（烟测取色断言按它定位火芯）。</summary>
	public Vector2 FireCenter => Size * new Vector2(FireCenterRatioX, FireCenterRatioY);

	public override void _Draw()
	{
		// 背景必须在这里铺：Godot 先画节点自身的 `_Draw`，再按子节点顺序画子节点 ——
		// 用 ColorRect 子节点铺底会把父节点的全部自绘（火堆与三个站位圆圈）**整个盖住**，
		// 2026-10-01 用户实测「营地画面里看不到火堆」即由此而来。
		DrawRect(new Rect2(Vector2.Zero, Size), BackgroundColor);

		// 火堆：三层光晕 + 火芯，全部代码绘制（不新增图片资源，与护盾徽标 / 状态图标同口径）。
		Vector2 fire = FireCenter;
		DrawCircle(fire, 92f, new Color(1f, .55f, .20f, .08f));
		DrawCircle(fire, 62f, new Color(1f, .60f, .25f, .16f));
		DrawCircle(fire, 30f, new Color(1f, .72f, .35f, .85f));
		DrawCircle(fire, 14f, FireCoreColor);

		// 角色站位占位：营地立绘 / 骨骼进场属遗留（B6 第 38 条骨骼进战场之后再复用）。
		RunSession run = RunSession.Instance;
		for (int i = 0; i < PartyOffsets.Length; i++)
		{
			Vector2 seat = fire + PartyOffsets[i];
			DrawCircle(seat, 20f, new Color("2b3a45"));
			DrawArc(seat, 20f, 0f, Mathf.Tau, 32, new Color("5f7f8f"), 1.5f, true);
			DrawString(ThemeDB.FallbackFont, seat + new Vector2(-4f, 6f), (i + 1).ToString(), fontSize: 16,
				modulate: new Color("c7d0d8"));
			// 站位下方标角色显示名：与左侧队伍行同序，守夜承担者一眼可对。
			if (run?.Current == null || i >= run.Current.CharacterSlots.Count) continue;
			string name = run.GetSlotDisplayName(i);
			Vector2 nameSize = ThemeDB.FallbackFont.GetStringSize(name, HorizontalAlignment.Left, -1, 14);
			DrawString(ThemeDB.FallbackFont, seat + new Vector2(-nameSize.X * .5f, 42f), name, fontSize: 14,
				modulate: new Color("9fb0bb"));
		}
	}

	private Control BuildWatchPanel()
	{
		VBoxContainer panel = new VBoxContainer();
		panel.AddThemeConstantOverride("separation", 8);
		Place(panel, .42f, .26f, .96f, .56f);

		Label caption = Text("守夜（点击底部「守夜」显示 / 隐藏）", 16);
		caption.AddThemeColorOverride("font_color", new Color("f0b27a"));
		panel.AddChild(caption);

		ButtonGroup group = new ButtonGroup();
		AddWatchOption(panel, group, RunWatchMode.None, "不守夜：全额回复");
		AddWatchOption(panel, group, RunWatchMode.Rotation, "轮流守夜：每名角色回复 ×2/3，不触发夜袭");
		AddWatchOption(panel, group, RunWatchMode.Single, "单人守夜：守夜角色不回复");

		// 守夜角色行：标签必须**单行 + 定宽**并在同一行内与下拉框相邻 ——
		// HBoxContainer 只按子节点的**最小宽度**排布，中文标签在 `AutowrapMode.WordSmart` 下的最小宽度 = 一个字，
		// 于是标签被压成一字一行、下拉框被顶到该行右侧中线（2026-10-01 用户实测「守夜角色的位置和框的位置没有对上」）。
		HBoxContainer pickerRow = new HBoxContainer();
		pickerRow.AddThemeConstantOverride("separation", 8);
		pickerRow.AddChild(new Control { CustomMinimumSize = new Vector2(CheckBoxCaptionIndent, 0) });
		pickerRow.AddChild(OneLineText("守夜角色", 16, 88f));
		watcherPicker = new OptionButton();
		RunSession run = RunSession.Instance;
		for (int i = 0; i < run.Current.CharacterSlots.Count; i++)
		{
			watcherPicker.AddItem(run.GetSlotDisplayName(i), i);
		}

		watcherPicker.Disabled = true; // 只有单人守夜才需要指定承担者
		watcherPicker.ItemSelected += index => { watcherSlotIndex = (int)index; RefreshPreview(); };
		pickerRow.AddChild(watcherPicker);
		panel.AddChild(pickerRow);
		return panel;
	}

	private void AddWatchOption(Control parent, ButtonGroup group, RunWatchMode mode, string caption)
	{
		CheckBox box = new CheckBox { Text = caption, ButtonGroup = group, ButtonPressed = mode == RunWatchMode.None };
		box.Toggled += pressed =>
		{
			if (!pressed) return; // ButtonGroup 保证同时只有一个选中
			watchMode = mode;
			if (watcherPicker != null) watcherPicker.Disabled = mode != RunWatchMode.Single;
			RefreshPreview();
		};
		parent.AddChild(box);
	}

	/// <summary>守夜与休息的实时预览（交互案「守夜」：在 UI 中实时显示生命回复）。</summary>
	private void RefreshPreview()
	{
		RunSession run = RunSession.Instance;
		if (run?.Current == null || previewLabel == null) return;
		int satiety = PlannedSatiety;
		float ratio = RunRestResolver.BaseHealRatio(run.Current, satiety);
		string watchText = watchMode switch
		{
			RunWatchMode.Rotation => "轮流守夜（×2/3）",
			RunWatchMode.Single => $"单人守夜（{run.GetSlotDisplayName(WatcherIndex(run))} 不回复）",
			_ => "不守夜（全额）",
		};
		previewLabel.Text =
			$"本次休息基础回复 {ratio * 100f:0.#}% 最大生命"
			+ $"（基础 10% + 剩余 {RunTimePoints.Format(run.Current.MapState.RestRemainingTimePoints)} / 4 × 20%"
			+ (satiety > 0 ? $" + 饱食度 {satiety} × 2%" : "，本次篝火未放食物") + $"）\n守夜：{watchText}";
		RefreshPartyRows();
		RefreshFoodPanels(); // 草稿变了（添加 / 取回 / 烹饪）→ 列表与进度同步重画
	}

	private void RefreshPartyRows()
	{
		RunSession run = RunSession.Instance;
		if (run?.Current == null) return;
		for (int i = 0; i < partyRows.Count && i < run.Current.CharacterSlots.Count; i++)
		{
			RunCharacterSlotSave slot = run.Current.CharacterSlots[i];
			int heal = RunRestResolver.PreviewHeal(run.Current, i, watchMode, WatcherIndex(run), PlannedSatiety);
			int after = Math.Min(slot.MaxHp, slot.CurrentHp + heal);
			partyRows[i].Text = $"{run.GetSlotDisplayName(i)}　HP {slot.CurrentHp}/{slot.MaxHp} → {after}（+{heal}）";
		}
	}

	private int WatcherIndex(RunSession run) =>
		run?.Current == null ? 0 : Math.Clamp(watcherSlotIndex, 0, Math.Max(0, run.Current.CharacterSlots.Count - 1));

	private void OnFoodPressed()
	{
		// 交互案「添加食物」：左侧篝火草稿 + 右侧背包食物，点「休息」前不消耗。
		ShowPanel(foodPanel != null && foodPanel.Visible ? null : foodPanel);
	}

	private void OnCookPressed()
	{
		// 交互案「烹饪」：列已放行配方与所需输入，每次休息 ≤ 2 次（RunFoodSystem.MaxCookPerRest）。
		ShowPanel(cookPanel != null && cookPanel.Visible ? null : cookPanel);
	}

	/// <summary>休息结算（交互案「休息」）：锁定选择 → 应用回复 → 推进新一天 → 淡出回地图。</summary>
	private void OnRestPressed()
	{
		if (resolving) return;
		RunSession run = RunSession.Instance;
		if (run?.Current == null)
		{
			FinishRest();
			return;
		}

		resolving = true;
		SetButtonsEnabled(false);
		// 休息结算：先消耗篝火食物并把仍生效的效果写进寿命轴，再按**真实**饱食度回复（RunSession.ApplyRest）。
		List<int> healed = run.ApplyRest(watchMode, WatcherIndex(run), plan);
		string detail = string.Join("　", healed.Select((amount, index) => $"{run.GetSlotDisplayName(index)} +{amount}"));
		resultLabel.Text = $"休息结算：{detail}\n篝火饱食度 {plan.TotalSatiety}（计入效果 {plan.EffectiveSatiety}）"
			+ $"\n第 {run.CurrentDay} 天开始 · {RunTimePoints.FormatDayAndRemaining(run.Current.MapState.TimePoints)}";
		GD.Print($"[营地] 休息结算：{detail}；第 {run.CurrentDay} 天，剩余 {RunTimePoints.Format(run.RemainingToday)} 时间点。");
		RefreshPartyRows();
		RefreshPreview();
		PlayFade(0f, 1f, FinishRest);
	}

	private void SetHint(string text)
	{
		if (hintLabel != null) hintLabel.Text = text;
	}

	private void SetButtonsEnabled(bool enabled)
	{
		if (restButton != null) restButton.Disabled = !enabled;
		if (foodButton != null) foodButton.Disabled = !enabled;   // 2026-10-02 批 C：食物 / 烹饪已接入，随结算锁定
		if (cookButton != null) cookButton.Disabled = !enabled;
		if (watchButton != null) watchButton.Disabled = !enabled;
	}

	/// <summary>休息结束：嵌入模式广播给宿主，独立场景模式自己切回地图场景。</summary>
	private void FinishRest()
	{
		if (EmbeddedMode)
		{
			if (RestCompleted != null) RestCompleted.Invoke();
			return;
		}

		GetTree().ChangeSceneToFile(MapScenePath);
		QueueFree();
	}

	/// <summary>淡入 / 淡出（交互案「转场步骤」）：用 Tween 推黑幕 alpha，结束后回调。</summary>
	private void PlayFade(float from, float to, Action onDone)
	{
		if (fade == null)
		{
			if (onDone != null) onDone.Invoke();
			return;
		}

		fade.Visible = true;
		fade.Color = new Color(fade.Color.R, fade.Color.G, fade.Color.B, from);
		Tween tween = CreateTween();
		tween.TweenProperty(fade, "color:a", to, FadeSeconds);
		if (onDone != null) tween.TweenCallback(Callable.From(onDone));
	}

	private static Label Text(string value, int size)
	{
		Label label = new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride("font_size", size);
		return label;
	}

	/// <summary>窗口自适应容器里对齐开关的整行用这种标签：定宽 + 不换行（避免被压成一字一行）。</summary>
	private static Label OneLineText(string value, int size, float width)
	{
		Label label = Text(value, size);
		label.AutowrapMode = TextServer.AutowrapMode.Off;
		label.CustomMinimumSize = new Vector2(width, 0);
		return label;
	}

	private static Button Button(string caption, Action onPressed, Control parent)
	{
		Button button = new Button { Text = caption, CustomMinimumSize = new Vector2(128, 46) };
		button.Pressed += onPressed;
		parent.AddChild(button);
		return button;
	}

	/// <summary>按锚点铺满给定比例区域（与剧情浮层的 Place 同口径：不依赖容器尺寸计算）。</summary>
	private static void Place(Control node, float left, float top, float right, float bottom)
	{
		node.AnchorLeft = left; node.AnchorTop = top; node.AnchorRight = right; node.AnchorBottom = bottom;
		node.OffsetLeft = 0; node.OffsetTop = 0; node.OffsetRight = 0; node.OffsetBottom = 0;
	}
	private void BuildUi()
	{
		RunSession run = RunSession.Instance;
		// 背景不在这里铺：父节点的 `_Draw` 会被任何子节点盖住（火堆会消失），见 `_Draw` 注释。

		Label title = Text("营地 · 篝火休息", 30);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.SetAnchorsPreset(LayoutPreset.TopWide);
		title.OffsetTop = 22; title.OffsetBottom = 66;
		AddChild(title);

		Label timeLabel = Text(RunTimePoints.FormatDayAndRemaining(run.Current.MapState.TimePoints), 18);
		timeLabel.HorizontalAlignment = HorizontalAlignment.Center;
		timeLabel.AddThemeColorOverride("font_color", new Color("f5d98c"));
		timeLabel.SetAnchorsPreset(LayoutPreset.TopWide);
		timeLabel.OffsetTop = 66; timeLabel.OffsetBottom = 96;
		AddChild(timeLabel);

		// 左：队伍（每名角色的当前生命与本次休息的回复预览）
		partyPanel = new VBoxContainer();
		partyPanel.AddThemeConstantOverride("separation", 8);
		Place(partyPanel, .04f, .26f, .38f, .60f);
		AddChild(partyPanel);
		for (int i = 0; i < run.Current.CharacterSlots.Count; i++)
		{
			Label row = Text(string.Empty, 18);
			partyPanel.AddChild(row);
			partyRows.Add(row);
		}

		// 右：守夜面板（默认收起，由「守夜」按钮开合）
		watchPanel = BuildWatchPanel();
		watchPanel.Visible = false;
		AddChild(watchPanel);

		// 右：添加食物 / 烹饪面板（2026-10-02 批 C；三块面板互斥显示，见 ShowPanel）
		foodPanel = BuildFoodPanel();
		foodPanel.Visible = false;
		AddChild(foodPanel);

		cookPanel = BuildCookPanel();
		cookPanel.Visible = false;
		AddChild(cookPanel);

		previewLabel = Text(string.Empty, 16);
		previewLabel.AddThemeColorOverride("font_color", new Color("c7d0d8"));
		Place(previewLabel, .42f, .58f, .96f, .74f);
		AddChild(previewLabel);

		resultLabel = Text(string.Empty, 18);
		resultLabel.AddThemeColorOverride("font_color", new Color("6ee59c"));
		Place(resultLabel, .42f, .74f, .96f, .86f);
		AddChild(resultLabel);

		hintLabel = Text("夜袭与「睡眠不佳」未接入（需当天最近一次普通敌袭的 50% 遭遇生成）：本期休息不结算这两项。", 14);
		hintLabel.AddThemeColorOverride("font_color", new Color("8fa1ad"));
		Place(hintLabel, .04f, .86f, .96f, .92f);
		AddChild(hintLabel);

		// 四个独立按钮（交互案「营地基础画面」）：添加食物 / 烹饪 / 守夜 / 休息。
		HBoxContainer buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		buttons.AddThemeConstantOverride("separation", 14);
		Place(buttons, .30f, .92f, .98f, .98f);
		AddChild(buttons);
		foodButton = Button(FoodButtonText, OnFoodPressed, buttons);
		cookButton = Button(CookButtonText, OnCookPressed, buttons);
		watchButton = Button(WatchButtonText,
			() => ShowPanel(watchPanel != null && watchPanel.Visible ? null : watchPanel), buttons);
		restButton = Button(RestButtonText, OnRestPressed, buttons);
		foodButton.TooltipText = "把背包食物放进本次篝火：效果饱食度上限 10，点「休息」才真正消耗。";
		cookButton.TooltipText = "用食物合成成菜（每次休息 ≤ 2 次；材料暂不参与烹饪）。";

		// 黑幕最后加入：始终盖在所有营地 UI 之上，用于进出转场。
		fade = new ColorRect { Color = new Color(0, 0, 0, 1f), MouseFilter = MouseFilterEnum.Stop };
		fade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(fade);
	}

	// ─────────────────────────────────────────────────────────────
	// 食物 / 烹饪面板（2026-10-02 批 C；篝火休息交互案「添加食物」「烹饪」）
	// ─────────────────────────────────────────────────────────────

	/// <summary>本次篝火草稿的饱食度（回复公式与真实结算都用它；空草稿 = 0）。</summary>
	public int PlannedSatiety => plan.TotalSatiety;

	/// <summary>计入食物效果的饱食度（≤ `RunFoodSystem.MaxEffectSatiety`）。</summary>
	public int PlannedEffectiveSatiety => plan.EffectiveSatiety;

	/// <summary>草稿里已添加的食物件数。</summary>
	public int PlannedEntryCount => plan.Count;

	/// <summary>「添加食物」面板是否打开（烟测断言用）。</summary>
	public bool FoodPanelVisible => foodPanel != null && foodPanel.Visible;

	/// <summary>「烹饪」面板是否打开。</summary>
	public bool CookPanelVisible => cookPanel != null && cookPanel.Visible;

	/// <summary>食物入口按钮是否被禁用（结算淡出期间应锁定）。</summary>
	public bool FoodButtonDisabled => foodButton == null || foodButton.Disabled;

	/// <summary>烹饪入口按钮是否被禁用。</summary>
	public bool CookButtonDisabled => cookButton == null || cookButton.Disabled;

	/// <summary>饱食度进度行文案（烟测断言「X / 10」与「计入效果」）。</summary>
	public string SatietyProgressText => foodProgressLabel?.Text ?? string.Empty;

	/// <summary>「添加食物」面板的提示行文案（失败原因 / 口径提示）。</summary>
	public string FoodHint => foodHintLabel?.Text ?? string.Empty;

	/// <summary>「烹饪」面板的提示行文案。</summary>
	public string CookHint => cookHintLabel?.Text ?? string.Empty;

	/// <summary>本次休息已合成的次数（`RunFoodSystem.MaxCookPerRest` 为上限）。</summary>
	public int CookedThisRest => RunSession.Instance?.Current?.CookedThisRest ?? 0;

	/// <summary>已放行的配方（2026-10-02 口径 ①：材料通道的 7 条旧配方 `Enabled = FALSE`）。</summary>
	public static List<FoodRecipeDefinition> EnabledRecipes()
	{
		Dictionary<int, FoodRecipeDefinition> recipes = LoadingSystem.FoodRecipeDictionary;
		return recipes == null
			? new List<FoodRecipeDefinition>()
			: recipes.Values.Where(x => x != null && x.Enabled).OrderBy(x => x.RecipeId).ToList();
	}

	/// <summary>
	/// 把一件背包食物放进本次篝火草稿（交互案「从背包拖到篝火列表」；面板按钮与烟测共用同一入口）。
	/// 校验（是食物 / 未过期 / 不重复）全部复用 `RunFoodSystem.TryBuildPlan`，不在这里另写一份规则。
	/// **不消耗**背包里的食物 —— 点「休息」才统一消耗。
	/// </summary>
	public bool TryAddFoodToCampFire(string instanceId, out string error)
	{
		error = string.Empty;
		RunSession run = RunSession.Instance;
		if (run?.Current == null)
		{
			error = "没有进行中的本局。";
			SetFoodHint(error);
			return false;
		}

		if (string.IsNullOrWhiteSpace(instanceId))
		{
			error = "没有选中食物。";
			SetFoodHint(error);
			return false;
		}

		if (plan.Entries.Any(x => string.Equals(x.InstanceId, instanceId, StringComparison.Ordinal)))
		{
			error = "这件食物已经在篝火里了。";
			SetFoodHint(error);
			return false;
		}

		if (!RunFoodSystem.TryBuildPlan(run.Current, new[] { instanceId }, out RunFoodSystem.CampFirePlan one, out error))
		{
			SetFoodHint(error);
			return false;
		}

		RunFoodSystem.CampFireEntry entry = one.Entries[0];
		plan.Append(entry);
		RefreshPreview();
		SetFoodHint($"已加入篝火：{entry.Name}（饱食 {entry.Satiety}）"
			+ $"{(entry.GrantsEffect ? "，本次休息生效" : "，超出效果上限只给饱食度")}；点「休息」才真正消耗。");
		return true;
	}

	/// <summary>从篝火草稿取回一件食物（交互案「从篝火列表拖回背包」= 撤销本次预览，不碰背包）。</summary>
	public bool TryRemoveFoodFromCampFire(string instanceId, out string error)
	{
		error = string.Empty;
		if (!plan.Remove(instanceId))
		{
			error = "这件食物不在篝火里。";
			SetFoodHint(error);
			return false;
		}

		RefreshPreview();
		SetFoodHint("已从篝火取回（背包未变动）。");
		return true;
	}

	/// <summary>
	/// 合成一次（交互案「烹饪」；面板按钮与烟测共用同一入口）：`RunSession.TryCook` 校验配方放行 /
	/// 本次休息次数 / 输入齐备，成功后成菜入背包并落档。
	/// </summary>
	public bool TryCookRecipe(int recipeId, out string error)
	{
		error = string.Empty;
		RunSession run = RunSession.Instance;
		if (run == null)
		{
			error = "没有进行中的本局。";
			SetCookHint(error);
			return false;
		}

		bool ok = run.TryCook(recipeId, plan.InstanceIds, out error);
		RefreshPreview();
		SetCookHint(ok
			? $"合成成功：成菜已放进背包（本次休息 {CookedThisRest} / {RunFoodSystem.MaxCookPerRest}），可立刻加入篝火。"
			: error);
		return ok;
	}

	/// <summary>三块右侧面板互斥显示（守夜 / 添加食物 / 烹饪）；传 null = 全部收起。打开时按当前状态重画。</summary>
	private void ShowPanel(Control target)
	{
		Control[] panels = { watchPanel, foodPanel, cookPanel };
		foreach (Control candidate in panels)
		{
			if (candidate != null)
			{
				candidate.Visible = ReferenceEquals(candidate, target);
			}
		}

		if (ReferenceEquals(target, foodPanel))
		{
			RefreshFoodPanel();
		}
		else if (ReferenceEquals(target, cookPanel))
		{
			RefreshCookPanel();
		}
	}

	/// <summary>草稿 / 次数变化后同步两块面板（未打开的面板不必重画）。</summary>
	private void RefreshFoodPanels()
	{
		if (foodPanel != null && foodPanel.Visible)
		{
			RefreshFoodPanel();
		}

		if (cookPanel != null && cookPanel.Visible)
		{
			RefreshCookPanel();
		}
	}


	/// <summary>「添加食物」面板：左侧篝火草稿（含「仍给效果 / 只给饱食度」标记）、右侧背包食物。</summary>
	private void RefreshFoodPanel()
	{
		RunSession run = RunSession.Instance;
		if (foodProgressLabel == null || fireColumn == null || bagColumn == null || run?.Current == null)
		{
			return;
		}

		foodProgressLabel.Text =
			$"饱食度 {plan.TotalSatiety} / {RunFoodSystem.MaxEffectSatiety}"
			+ $"（计入效果 {plan.EffectiveSatiety}）· 已添加 {plan.Count} 件";

		ClearRows(fireColumn);
		if (plan.IsEmpty)
		{
			fireColumn.AddChild(Text("（空：点右侧「添加」把背包食物放进本次篝火）", 14));
		}

		foreach (RunFoodSystem.CampFireEntry entry in plan.Entries)
		{
			HBoxContainer row = Row(fireColumn);
			string mark = entry.GrantsEffect ? "本次休息生效" : "超出上限，只给饱食度";
			row.AddChild(OneLineText(
				$"{entry.Name}　饱食 {entry.Satiety} · 剩余 {entry.ExpireDaysRemaining} 天 · {mark}", 15, 250f));
			string instanceId = entry.InstanceId;
			RowButton(row, "取回", () => TryRemoveFoodFromCampFire(instanceId, out _));
		}

		ClearRows(bagColumn);
		List<RunBagEntrySave> foods = RunBagSystem.EntriesOf(run.Current, BagCategory.Food)
			.Where(x => !plan.Entries.Any(p => string.Equals(p.InstanceId, x.InstanceId, StringComparison.Ordinal)))
			.ToList();
		if (foods.Count == 0)
		{
			bagColumn.AddChild(Text("（背包里没有可添加的食物）", 14));
		}

		foreach (RunBagEntrySave food in foods)
		{
			HBoxContainer row = Row(bagColumn);
			bool usable = RunBagSystem.CanEnterCampFire(food);
			string state = usable
				? $"饱食 {ItemNameResolver.FoodSatietyOf(food.DefinitionKey)} · 剩余 {food.ExpireDaysRemaining} 天"
				: "已过期（不可放入篝火）";
			row.AddChild(OneLineText($"{food.DefinitionId}　{state}", 15, 220f));
			string instanceId = food.InstanceId;
			Button add = RowButton(row, "添加", () => TryAddFoodToCampFire(instanceId, out _));
			add.Disabled = !usable;
		}
	}

	/// <summary>「烹饪」面板：已放行配方 + 所需输入 + 本次休息次数（未放行的配方只报数量）。</summary>
	private void RefreshCookPanel()
	{
		RunSession run = RunSession.Instance;
		if (cookProgressLabel == null || cookColumn == null || run?.Current == null)
		{
			return;
		}

		cookProgressLabel.Text =
			$"本次休息已合成 {run.Current.CookedThisRest} / {RunFoodSystem.MaxCookPerRest} 次（合成不消耗时间点）";

		ClearRows(cookColumn);
		List<FoodRecipeDefinition> recipes = EnabledRecipes();
		int locked = Math.Max(0, LoadingSystem.FoodRecipeDictionary.Count - recipes.Count);
		if (recipes.Count == 0)
		{
			cookColumn.AddChild(Text("（没有已放行的配方）", 14));
		}

		foreach (FoodRecipeDefinition recipe in recipes)
		{
			HBoxContainer row = Row(cookColumn);
			bool can = RunFoodSystem.CanCook(run.Current, recipe, out string reason);
			row.AddChild(OneLineText(
				$"{DescribeRecipe(recipe)}　→　{ItemNameResolver.Food(recipe.ResultFoodId)} ×{recipe.ResultCount}", 15, 380f));
			int recipeId = recipe.RecipeId;
			Button cook = RowButton(row, "合成", () => TryCookRecipe(recipeId, out _));
			cook.Disabled = !can;
			if (!can)
			{
				cook.TooltipText = reason;
			}
		}

		SetCookHint(locked > 0
			? $"共 {recipes.Count} 条已放行配方；另有 {locked} 条材料配方未放行（2026-10-02 口径：材料暂不参与烹饪）。"
			: $"共 {recipes.Count} 条已放行配方。");
	}

	/// <summary>配方一行的输入文案：`烤蟾蜍 ×2 + 香草炖菜 ×1`（名字取自 `ItemNameResolver` 的注册表）。</summary>
	private static string DescribeRecipe(FoodRecipeDefinition recipe)
	{
		List<string> inputs = new List<string>();
		foreach (RecipeInputSpec input in recipe.Inputs)
		{
			string name = input.Kind == RecipeInputKind.Food
				? ItemNameResolver.Food(input.Id)
				: ItemNameResolver.Material(input.Id);
			inputs.Add($"{name} ×{input.Count}");
		}

		return inputs.Count == 0 ? "（无输入）" : string.Join(" + ", inputs);
	}

	private void SetFoodHint(string text)
	{
		if (foodHintLabel != null)
		{
			foodHintLabel.Text = text;
		}
	}

	private void SetCookHint(string text)
	{
		if (cookHintLabel != null)
		{
			cookHintLabel.Text = text;
		}
	}

	/// <summary>清空一列的动态行（每次重画都重建：草稿与背包的条目数量都在变）。</summary>
	private static void ClearRows(Node column)
	{
		foreach (Node child in column.GetChildren())
		{
			column.RemoveChild(child);
			child.QueueFree();
		}
	}

	private static HBoxContainer Row(Control parent)
	{
		HBoxContainer row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		parent.AddChild(row);
		return row;
	}

	/// <summary>面板行内的小按钮（128×46 的 `Button()` 是按底部四个主按钮定的，行内用 64×28）。</summary>
	private static Button RowButton(Control parent, string caption, Action onPressed)
	{
		Button button = new Button { Text = caption, CustomMinimumSize = new Vector2(64, 28) };
		button.Pressed += onPressed;
		parent.AddChild(button);
		return button;
	}


	/// <summary>
	/// 「添加食物」面板（交互案「添加食物」：上 = 本次篝火饱食度进度，下 = 左右双侧列表）。
	/// 拖拽本期未做 —— 用「添加 / 取回」按钮表达同一份草稿语义（草稿始终在 `plan`，点「休息」才消耗）。
	/// </summary>
	private Control BuildFoodPanel()
	{
		VBoxContainer panel = new VBoxContainer();
		panel.AddThemeConstantOverride("separation", 6);
		panel.ClipContents = true; // 行数超出版面时裁掉，不许压到下方的回复预览
		Place(panel, .40f, .20f, .97f, .58f);

		Label caption = Text("添加食物（「添加」放进本次篝火 · 「取回」撤销；点「休息」才真正消耗）", 16);
		caption.AddThemeColorOverride("font_color", new Color("f0b27a"));
		panel.AddChild(caption);

		foodProgressLabel = Text(string.Empty, 16);
		foodProgressLabel.AddThemeColorOverride("font_color", new Color("f5d98c"));
		panel.AddChild(foodProgressLabel);

		HBoxContainer columns = new HBoxContainer();
		columns.AddThemeConstantOverride("separation", 18);
		panel.AddChild(columns);

		VBoxContainer fire = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		fire.AddThemeConstantOverride("separation", 4);
		fire.AddChild(Text("篝火（本次休息结算）", 15));
		fireColumn = new VBoxContainer();
		fireColumn.AddThemeConstantOverride("separation", 4);
		fire.AddChild(fireColumn);
		columns.AddChild(fire);

		VBoxContainer bag = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		bag.AddThemeConstantOverride("separation", 4);
		bag.AddChild(Text("背包食物", 15));
		bagColumn = new VBoxContainer();
		bagColumn.AddThemeConstantOverride("separation", 4);
		bag.AddChild(bagColumn);
		columns.AddChild(bag);

		foodHintLabel = Text("效果饱食度上限 10：首个越限的食物及其后只给饱食度、不给效果；过期的食物不能放入。", 14);
		foodHintLabel.AddThemeColorOverride("font_color", new Color("8fa1ad"));
		panel.AddChild(foodHintLabel);
		return panel;
	}

	/// <summary>「烹饪」面板（交互案「烹饪」：可合成食物 + 所需输入 + 本次休息次数）。</summary>
	private Control BuildCookPanel()
	{
		VBoxContainer panel = new VBoxContainer();
		panel.AddThemeConstantOverride("separation", 6);
		panel.ClipContents = true;
		Place(panel, .40f, .20f, .97f, .58f);

		Label caption = Text("烹饪（只消耗食物；材料暂不参与烹饪）", 16);
		caption.AddThemeColorOverride("font_color", new Color("f0b27a"));
		panel.AddChild(caption);

		cookProgressLabel = Text(string.Empty, 16);
		cookProgressLabel.AddThemeColorOverride("font_color", new Color("f5d98c"));
		panel.AddChild(cookProgressLabel);

		cookColumn = new VBoxContainer();
		cookColumn.AddThemeConstantOverride("separation", 4);
		panel.AddChild(cookColumn);

		cookHintLabel = Text(string.Empty, 14);
		cookHintLabel.AddThemeColorOverride("font_color", new Color("8fa1ad"));
		panel.AddChild(cookHintLabel);
		return panel;
	}

}