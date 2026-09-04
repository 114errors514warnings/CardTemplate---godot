// MainMenuScene.cs
// 主界面：按存档状态切换按钮组。
using Godot;
using System;
using System.Collections.Generic;

public partial class MainMenuScene : Control
{
	public const string CharacterSelectScenePath = "res://Scenes/CharacterSelect/CharacterSelectScene.tscn";
	public const string MapScenePath = "res://Scenes/Map/MapScene.tscn";
	public const string RunBattleScenePath = "res://Scenes/Run/RunBattleScene.tscn";

	private VBoxContainer buttonBox;
	private Button startButton;
	private Button continueButton;
	private Button discardButton;
	private Label statusLabel;
	private readonly List<Button> menuButtons = new List<Button>();

	public override void _Ready()
	{
		// 预热全部数据（角色/怪物/卡牌/状态/默认卡组/DropTable/Stage/角色卡池）
		LoadingSystem.EnsureAllDataLoaded();
		BuildUi();
		RefreshButtons();
	}

	private void BuildUi()
	{
		ColorRect bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.12f, 1f) };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		bg.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(bg);

		CenterContainer center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		center.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(center);

		VBoxContainer vbox = new VBoxContainer();
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddThemeConstantOverride("separation", 18);
		center.AddChild(vbox);

		Label title = new Label
		{
			Text = "卡牌模拟器",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 64);
		title.AddThemeColorOverride("font_color", Colors.White);
		vbox.AddChild(title);

		statusLabel = new Label
		{
			Text = string.Empty,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		statusLabel.AddThemeFontSizeOverride("font_size", 20);
		statusLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
		vbox.AddChild(statusLabel);

		buttonBox = new VBoxContainer();
		buttonBox.AddThemeConstantOverride("separation", 14);
		vbox.AddChild(buttonBox);

		Label version = new Label
		{
			Text = "v0.9 P0#9 关卡流程闭环",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		version.AddThemeFontSizeOverride("font_size", 14);
		version.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
		vbox.AddChild(version);
	}

	private Button CreateMenuButton(string text)
	{
		Button button = new Button { Text = text };
		button.CustomMinimumSize = new Vector2(280, 52);
		button.AddThemeFontSizeOverride("font_size", 24);
		buttonBox.AddChild(button);
		menuButtons.Add(button);
		return button;
	}

	private void RefreshButtons()
	{
		// 清理旧按钮
		foreach (Button old in menuButtons)
		{
			if (old != null && old.IsInsideTree())
			{
				buttonBox.RemoveChild(old);
			}

			old?.QueueFree();
		}

		menuButtons.Clear();
		startButton = null;
		continueButton = null;
		discardButton = null;

		bool hasSave = RunSession.HasSave();
		if (hasSave)
		{
			statusLabel.Text = "检测到进行中的冒险";
			continueButton = CreateMenuButton("继续游戏");
			continueButton.Pressed += OnContinuePressed;
			discardButton = CreateMenuButton("放弃当前进度");
			discardButton.Pressed += OnDiscardPressed;
		}
		else
		{
			statusLabel.Text = "开始一段新的冒险";
			startButton = CreateMenuButton("开始游戏");
			startButton.Pressed += OnStartPressed;
		}

		Button exitButton = CreateMenuButton("退出");
		exitButton.Pressed += () => GetTree().Quit();
	}

	private void OnStartPressed()
	{
		GetTree().ChangeSceneToFile(CharacterSelectScenePath);
	}

	private void OnContinuePressed()
	{
		if (RunSession.Instance == null)
		{
			GD.PrintErr("[主菜单] RunSession 单例不存在（未配置 autoload？）。");
			return;
		}

		if (RunSession.Instance.LoadSave())
		{
			// 按存档状态机路由：地图 / 战斗中（重新开局）/ 结算未领取（重现弹窗）
			string scenePath = MapScenePath;
			if (RunSession.Instance.IsInSettlement || RunSession.Instance.IsInBattleStart)
			{
				scenePath = RunBattleScenePath;
			}

			GetTree().ChangeSceneToFile(scenePath);
		}
		else
		{
			statusLabel.Text = "存档读取失败";
			RefreshButtons();
		}
	}

	private void OnDiscardPressed()
	{
		RunSession.DeleteSave();
		if (RunSession.Instance != null)
		{
			RunSession.Instance.ClearCurrent();
		}

		statusLabel.Text = "已放弃当前进度";
		RefreshButtons();
	}
}
