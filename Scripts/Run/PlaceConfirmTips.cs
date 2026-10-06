// PlaceConfirmTips.cs
// 地点场景（村庄 / 商人）里「跟随角色的确认进入 tips」—— 村庄案 §六 的共用组件。
// 用户口径 2026-10-03：旅馆 / 民宿 / 树林只弹这个气泡；锻铁铺 / 餐厅走各自的专用界面。
// 结构（村庄案 §六）：4 行 = 设施名 / 效果预览（与结算结果逐字一致）/ 代价 / 两个按钮（`进入` / `稍后`）。
// 组件**不含任何设施规则**：文案与按钮可用性都由调用方给；本组件只负责显示、跟随角色与回调一个「进入 / 稍后」。
using Godot;
using System;

public partial class PlaceConfirmTips : Control
{
	/// <summary>确认按钮文案（村庄案 §六）。</summary>
	public const string EnterText = "进入";

	/// <summary>取消按钮文案（村庄案 §十二 第 3 条：点它关掉、不原地重弹）。</summary>
	public const string LaterText = "稍后";

	/// <summary>气泡宽度（村庄案 §六：宽 260）。</summary>
	public const int BubbleWidth = 260;

	/// <summary>玩家点了按钮：true = 进入、false = 稍后（气泡已自行关闭）。</summary>
	public event Action<bool> Resolved;

	private PanelContainer panel;
	private Label titleLabel;
	private Label effectLabel;
	private Label costLabel;
	private Button enterButton;
	private Button laterButton;
	private Control follow;
	private string enterText = EnterText;

	/// <summary>气泡是否正在显示。</summary>
	public bool IsOpen => panel != null && GodotObject.IsInstanceValid(panel) && panel.Visible;

	public string TitleText => titleLabel?.Text ?? string.Empty;
	public string EffectText => effectLabel?.Text ?? string.Empty;
	public string CostText => costLabel?.Text ?? string.Empty;
	public string EnterButtonText => enterButton?.Text ?? string.Empty;
	public bool EnterEnabled => enterButton != null && !enterButton.Disabled;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		BuildUi();
	}

	/// <summary>跟随目标（角色棋子）：气泡始终浮在它上方（村庄案 §六「跟随角色」）。</summary>
	public void Follow(Control target) => follow = target;

	/// <summary>
	/// 显示气泡。`enterEnabled = false` 时 `进入` 按钮禁用并显示 `enterButtonText`（例如 `金币不足`），
	/// 原因由调用方写进 `costLine`（旅馆案 §二 第 3 行）。
	/// </summary>
	public void ShowTips(string title, string effectLine, string costLine, bool enterEnabled = true, string enterButtonText = null)
	{
		if (panel == null || !GodotObject.IsInstanceValid(panel))
		{
			BuildUi();
		}

		titleLabel.Text = title ?? string.Empty;
		effectLabel.Text = effectLine ?? string.Empty;
		costLabel.Text = costLine ?? string.Empty;
		enterText = string.IsNullOrEmpty(enterButtonText) ? EnterText : enterButtonText;
		enterButton.Text = enterText;
		enterButton.Disabled = !enterEnabled;
		panel.Visible = true;
		Visible = true;
		Reposition();
	}

	/// <summary>关掉气泡（不改任何游戏状态）。</summary>
	public void Close()
	{
		if (panel != null && GodotObject.IsInstanceValid(panel))
		{
			panel.Visible = false;
		}

		Visible = false;
	}

	public override void _Process(double delta)
	{
		if (IsOpen)
		{
			Reposition();
		}
	}

	private void OnEnterPressed()
	{
		Close();
		Resolved?.Invoke(true);
	}

	private void OnLaterPressed()
	{
		Close();
		Resolved?.Invoke(false);
	}

	/// <summary>把气泡摆到跟随目标上方（夹在视口内，避免贴边被裁）。</summary>
	private void Reposition()
	{
		if (panel == null || follow == null || !GodotObject.IsInstanceValid(follow) || !GodotObject.IsInstanceValid(panel))
		{
			return;
		}

		Vector2 size = panel.Size.X > 1f ? panel.Size : panel.GetCombinedMinimumSize();
		Vector2 target = follow.GlobalPosition + follow.Size * 0.5f;
		Vector2 pos = target + new Vector2(-size.X * 0.5f, -size.Y - 18f);
		Vector2 viewport = GetViewportRect().Size;
		pos.X = Mathf.Clamp(pos.X, 8f, Mathf.Max(8f, viewport.X - size.X - 8f));
		pos.Y = Mathf.Clamp(pos.Y, 8f, Mathf.Max(8f, viewport.Y - size.Y - 8f));
		panel.GlobalPosition = pos;
	}

	private void BuildUi()
	{
		panel = new PanelContainer { CustomMinimumSize = new Vector2(BubbleWidth, 0f) };
		AddChild(panel);

		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		panel.AddChild(margin);

		VBoxContainer column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 6);
		margin.AddChild(column);

		titleLabel = new Label { Text = string.Empty };
		titleLabel.AddThemeFontSizeOverride("font_size", 18);
		column.AddChild(titleLabel);

		effectLabel = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		effectLabel.AddThemeFontSizeOverride("font_size", 14);
		column.AddChild(effectLabel);

		costLabel = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		costLabel.AddThemeFontSizeOverride("font_size", 14);
		column.AddChild(costLabel);

		HBoxContainer buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		buttons.AddThemeConstantOverride("separation", 8);
		column.AddChild(buttons);

		enterButton = new Button { Text = EnterText };
		enterButton.Pressed += OnEnterPressed;
		buttons.AddChild(enterButton);

		laterButton = new Button { Text = LaterText };
		laterButton.Pressed += OnLaterPressed;
		buttons.AddChild(laterButton);
	}
}
