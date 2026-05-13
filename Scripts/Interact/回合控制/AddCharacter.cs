using Godot;

public partial class AddCharacter : Button
{
	private const string ParameterFormat = "角色ID";

	public override void _Ready()
	{
		Pressed += OnAddCharacterPressed;
	}

	private void OnAddCharacterPressed()
	{
		LineEdit lineEdit = FindLineEdit();
		if (lineEdit == null)
		{
			AppendConsoleError("错误：未找到参数框 LineEdit。路径应为 操作面板/参数框/LineEdit。");
			return;
		}

		string raw = lineEdit.Text == null ? string.Empty : lineEdit.Text.Trim();
		if (string.IsNullOrEmpty(raw))
		{
			AppendConsoleInfo($"新增角色 参数格式：{ParameterFormat}");
			return;
		}

		if (!int.TryParse(raw, out int characterId))
		{
			AppendConsoleError($"错误：角色ID '{raw}' 不是合法数字。参数格式：{ParameterFormat}");
			return;
		}

		EnsureCharacterCacheLoaded();
		if (!LoadingSystem.CharacterDictionary.ContainsKey(characterId))
		{
			AppendConsoleError($"错误：角色ID {characterId} 未在 Character.csv 中找到。");
			return;
		}

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法写入 BattleSetupData。");
			return;
		}

		if (battleSytem.IsBattleStarted)
		{
			AppendConsoleInfo("新增角色仅在战斗开始前生效。当前已开始战斗，本次操作已忽略。");
			return;
		}

		BattleSetupData setupData = battleSytem.EnsureSetupData();
		int addedCount = setupData.AddCharacterId(characterId);
		if (addedCount <= 0)
		{
			AppendConsoleError($"错误：角色数量已达到上限 {BattleSetupData.MaxCharacterCapacity}，无法继续新增角色。");
			return;
		}

		battleSytem.SelectedCharacterId = setupData.GetCharacterIdList()[0];
		battleSytem.RefreshBattleInfoDisplay();
		AppendConsoleInfo($"已新增角色ID={characterId}。当前角色数量={setupData.GetTotalCharacterCount()}。");
	}

	private void EnsureCharacterCacheLoaded()
	{
		if (LoadingSystem.CharacterDictionary.Count == 0)
		{
			LoadingSystem.LoadCharactersByKey(LoadingSystem.CharacterCsvPathKey, true);
		}
	}

	private LineEdit FindLineEdit()
	{
		Node scene = GetTree().CurrentScene;
		if (scene == null)
		{
			return null;
		}

		LineEdit lineEdit = scene.GetNodeOrNull<LineEdit>("操作面板/参数框/LineEdit");
		if (lineEdit != null)
		{
			return lineEdit;
		}

		return scene.GetNodeOrNull<LineEdit>("UI_Main/操作面板/参数框/LineEdit");
	}

	private BattleSytem FindBattleSystem()
	{
		Node scene = GetTree().CurrentScene;
		if (scene == null)
		{
			return null;
		}

		BattleSytem direct = scene.GetNodeOrNull<BattleSytem>("BattleSytem");
		if (direct != null)
		{
			return direct;
		}

		return FindNodeRecursive<BattleSytem>(scene);
	}

	private T FindNodeRecursive<T>(Node root) where T : Node
	{
		if (root is T found)
		{
			return found;
		}

		foreach (Node child in root.GetChildren())
		{
			T childFound = FindNodeRecursive<T>(child);
			if (childFound != null)
			{
				return childFound;
			}
		}

		return null;
	}

	private void AppendConsoleError(string message)
	{
		AppendConsole("[错误] " + message);
		GD.PrintErr(message);
	}

	private void AppendConsoleInfo(string message)
	{
		AppendConsole("[信息] " + message);
	}

	private void AppendConsole(string message)
	{
		Node scene = GetTree().CurrentScene;
		if (scene == null)
		{
			return;
		}

		RichTextLabel console = scene.GetNodeOrNull<RichTextLabel>("ConsoleContainer/Console");
		if (console == null)
		{
			console = scene.GetNodeOrNull<RichTextLabel>("UI_Main/ConsoleContainer/Console");
		}

		if (console == null)
		{
			return;
		}

		if (!string.IsNullOrEmpty(console.Text))
		{
			console.Text += "\n";
		}

		console.Text += message;
	}
}