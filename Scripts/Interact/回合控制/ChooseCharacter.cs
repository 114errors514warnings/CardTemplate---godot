using Godot;

public partial class ChooseCharacter : Button
{
	private const string DefaultCharacterCsvPath = "res://DataBase/Unit/Character.csv";
	private const string ParameterFormat = "角色ID";

	public override void _Ready()
	{
		Pressed += OnChooseCharacterPressed;
	}

	private void OnChooseCharacterPressed()
	{
		LineEdit lineEdit = FindLineEdit();
		if (lineEdit == null)
		{
			AppendConsoleError("错误：未找到参数框 LineEdit。路径应为 操作面板/参数框/LineEdit。");
			return;
		}

		string raw = lineEdit.Text == null ? string.Empty : lineEdit.Text.Trim();
		EnsureCharacterCacheLoaded();

		int characterId;
		if (string.IsNullOrEmpty(raw))
		{
			characterId = GetDefaultCharacterId();
			if (characterId <= 0)
			{
				AppendConsoleInfo($"选择角色 参数格式：{ParameterFormat}。留空时默认使用第一个角色。当前未找到可用角色ID。");
				return;
			}

			AppendConsoleInfo($"选择角色 未填写参数，默认使用第一个角色ID={characterId}");
		}
		else
		{
			if (!int.TryParse(raw, out characterId))
			{
				AppendConsoleError($"错误：角色ID '{raw}' 不是合法数字。参数格式：{ParameterFormat}");
				return;
			}

			AppendConsoleInfo($"选择角色 参数解析：角色ID={characterId}");
		}

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

		if (battleSytem.SetupData == null)
		{
			battleSytem.SetupData = new BattleSetupData();
		}

		battleSytem.SetupData.CharacterId = characterId;
		battleSytem.SelectedCharacterId = characterId;
		battleSytem.RefreshBattleInfoDisplay();
		AppendConsoleInfo($"已设置角色ID为 {characterId}。");
	}

	private int GetDefaultCharacterId()
	{
		int defaultCharacterId = -1;
		foreach (int candidateId in LoadingSystem.CharacterDictionary.Keys)
		{
			if (defaultCharacterId < 0 || candidateId < defaultCharacterId)
			{
				defaultCharacterId = candidateId;
			}
		}

		return defaultCharacterId;
	}

	private void EnsureCharacterCacheLoaded()
	{
		if (LoadingSystem.CharacterDictionary.Count == 0)
		{
			LoadingSystem.LoadCharacters(DefaultCharacterCsvPath, true);
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
