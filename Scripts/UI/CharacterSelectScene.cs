// CharacterSelectScene.cs
// 选人界面：3 个槽位从可用角色池中选择（允许重复，当前 2 名角色）。
using Godot;
using System;
using System.Collections.Generic;

public partial class CharacterSelectScene : Control
{
	public const string MainMenuScenePath = "res://Scenes/MainMenu/MainMenuScene.tscn";
	public const string MapScenePath = "res://Scenes/Map/MapScene.tscn";

	[Export] public Godot.Collections.Array<int> AvailableCharacterIds = new Godot.Collections.Array<int> { 1001, 1002 };

	private const int SlotCount = 3;
	private readonly int[] selectedIds = new int[SlotCount];
	private readonly Button[] slotButtons = new Button[SlotCount];
	private VBoxContainer rosterBox;
	private Button confirmButton;
	private int activeSlot = -1;
	private readonly List<Button> rosterButtons = new List<Button>();

	public override void _Ready()
	{
		for (int i = 0; i < SlotCount; i++)
		{
			selectedIds[i] = 0;
		}

		BuildUi();
		RefreshAll();
	}

	private void BuildUi()
	{
		ColorRect bg = new ColorRect { Color = new Color(0.10f, 0.10f, 0.14f, 1f) };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		bg.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(bg);

		MarginContainer margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 40);
		margin.AddThemeConstantOverride("margin_top", 24);
		margin.AddThemeConstantOverride("margin_right", 40);
		margin.AddThemeConstantOverride("margin_bottom", 24);
		AddChild(margin);

		VBoxContainer vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 16);
		margin.AddChild(vbox);

		Label title = new Label { Text = "选择出战角色（3 个槽位）", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 40);
		title.AddThemeColorOverride("font_color", Colors.White);
		vbox.AddChild(title);

		// 槽位行
		HBoxContainer slotRow = new HBoxContainer();
		slotRow.Alignment = BoxContainer.AlignmentMode.Center;
		slotRow.AddThemeConstantOverride("separation", 18);
		vbox.AddChild(slotRow);
		for (int i = 0; i < SlotCount; i++)
		{
			int index = i;
			Button button = new Button();
			button.CustomMinimumSize = new Vector2(220, 120);
			button.AddThemeFontSizeOverride("font_size", 20);
			button.Pressed += () => OnSlotPressed(index);
			slotRow.AddChild(button);
			slotButtons[index] = button;
		}

		// 可选角色列表（点击槽位后显示）
		rosterBox = new VBoxContainer();
		rosterBox.AddThemeConstantOverride("separation", 8);
		vbox.AddChild(rosterBox);

		// 底部按钮行
		HBoxContainer bottomRow = new HBoxContainer();
		bottomRow.Alignment = BoxContainer.AlignmentMode.Center;
		bottomRow.AddThemeConstantOverride("separation", 24);
		vbox.AddChild(bottomRow);

		Button backButton = new Button { Text = "返回主菜单" };
		backButton.CustomMinimumSize = new Vector2(180, 48);
		backButton.Pressed += () => GetTree().ChangeSceneToFile(MainMenuScenePath);
		bottomRow.AddChild(backButton);

		confirmButton = new Button { Text = "确认出战", Disabled = true };
		confirmButton.CustomMinimumSize = new Vector2(220, 52);
		confirmButton.AddThemeFontSizeOverride("font_size", 24);
		confirmButton.Pressed += OnConfirmPressed;
		bottomRow.AddChild(confirmButton);
	}

	private void OnSlotPressed(int slotIndex)
	{
		activeSlot = slotIndex;
		RebuildRoster();
	}

	private void RebuildRoster()
	{
		foreach (Button old in rosterButtons)
		{
			if (old != null && old.IsInsideTree())
			{
				rosterBox.RemoveChild(old);
			}

			old?.QueueFree();
		}

		rosterButtons.Clear();
		if (activeSlot < 0)
		{
			return;
		}

		foreach (int characterId in AvailableCharacterIds)
		{
			if (!LoadingSystem.CharacterDictionary.TryGetValue(characterId, out Character character))
			{
				continue;
			}

			Button option = new Button
			{
				Text = $"{character.Name}　HP {character.MAX_HP}　攻击 {character.Ini_Attack}　防御 {character.Ini_Defend}　抽牌 {character.drawCardNum}",
			};
			option.CustomMinimumSize = new Vector2(0, 54);
			option.AddThemeFontSizeOverride("font_size", 20);
			option.Pressed += () => OnPickCharacter(characterId);
			rosterBox.AddChild(option);
			rosterButtons.Add(option);
		}

		Button cancel = new Button { Text = "取消选择" };
		cancel.CustomMinimumSize = new Vector2(0, 42);
		cancel.Pressed += () =>
		{
			activeSlot = -1;
			RebuildRoster();
		};
		rosterBox.AddChild(cancel);
		rosterButtons.Add(cancel);
	}

	private void OnPickCharacter(int characterId)
	{
		if (activeSlot >= 0 && activeSlot < SlotCount)
		{
			selectedIds[activeSlot] = characterId;
		}

		activeSlot = -1;
		RebuildRoster();
		RefreshSlots();
	}

	private void RefreshSlots()
	{
		for (int i = 0; i < SlotCount; i++)
		{
			int characterId = selectedIds[i];
			if (characterId > 0 && LoadingSystem.CharacterDictionary.TryGetValue(characterId, out Character character))
			{
				slotButtons[i].Text = $"槽位 {i + 1}\n{character.Name}";
			}
			else
			{
				slotButtons[i].Text = $"槽位 {i + 1}\n点击选择";
			}
		}

		bool allChosen = true;
		for (int i = 0; i < SlotCount; i++)
		{
			if (selectedIds[i] <= 0)
			{
				allChosen = false;
				break;
			}
		}

		confirmButton.Disabled = !allChosen;
	}

	private void RefreshAll()
	{
		RebuildRoster();
		RefreshSlots();
	}

	private void OnConfirmPressed()
	{
		if (RunSession.Instance == null)
		{
			GD.PrintErr("[选人] RunSession 单例不存在。");
			return;
		}

		List<int> ids = new List<int>();
		for (int i = 0; i < SlotCount; i++)
		{
			if (selectedIds[i] > 0)
			{
				ids.Add(selectedIds[i]);
			}
		}

		if (ids.Count == 0)
		{
			return;
		}

		RunSession.Instance.StartNewRun(ids);
		GetTree().ChangeSceneToFile(MapScenePath);
	}
}
