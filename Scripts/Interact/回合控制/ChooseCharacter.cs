using Godot;

public partial class ChooseCharacter : Button
{
	private const string ParameterFormat = "角色ID [index]";

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

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法写入 BattleSetupData。");
			return;
		}

		if (battleSytem.IsBattleStarted)
		{
			AppendConsoleInfo("选择角色仅在战斗开始前生效。当前已开始战斗，本次操作已忽略。");
			return;
		}

		int characterId;
		int characterIndex = 1;
		if (string.IsNullOrEmpty(raw))
		{
			characterId = GetDefaultCharacterId();
			if (characterId <= 0)
			{
				AppendConsoleInfo($"选择角色 参数格式：{ParameterFormat}。留空时默认使用第一个角色，并修改第1个角色槽位。当前未找到可用角色ID。");
				return;
			}

			AppendConsoleInfo($"选择角色 未填写参数，默认使用第一个角色ID={characterId}，修改第1个角色槽位。");
		}
		else
		{
			string[] arguments = raw.Split(new char[] { ' ', '\t', ',', '，', ';', '；', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
			if (arguments.Length == 0 || !int.TryParse(arguments[0], out characterId))
			{
				AppendConsoleError($"错误：角色ID '{raw}' 不是合法数字。参数格式：{ParameterFormat}");
				return;
			}

			if (arguments.Length >= 2)
			{
				if (!int.TryParse(arguments[1], out characterIndex) || characterIndex <= 0)
				{
					AppendConsoleError($"错误：index '{arguments[1]}' 不是大于0的合法数字。参数格式：{ParameterFormat}");
					return;
				}
			}

			AppendConsoleInfo($"选择角色 参数解析：角色ID={characterId}，index={characterIndex}");
		}

		if (!LoadingSystem.CharacterDictionary.ContainsKey(characterId))
		{
			AppendConsoleError($"错误：角色ID {characterId} 未在 Character.csv 中找到。");
			return;
		}

		if (characterIndex > BattleSetupData.MaxCharacterCapacity)
		{
			AppendConsoleError($"错误：index={characterIndex} 超出角色上限 {BattleSetupData.MaxCharacterCapacity}。");
			return;
		}

		BattleSetupData setupData = battleSytem.EnsureSetupData();
		setupData.EnsureCharacterOrderInitialized();
		bool success = setupData.SetCharacterIdAt(characterIndex - 1, characterId);
		if (!success)
		{
			AppendConsoleError($"错误：无法修改第 {characterIndex} 个角色槽位。");
			return;
		}

		battleSytem.SelectedCharacterId = setupData.GetTotalCharacterCount() > 0 ? setupData.GetCharacterIdList()[0] : characterId;
		battleSytem.RefreshBattleInfoDisplay();
		AppendConsoleInfo($"已将第 {characterIndex} 个角色修改为角色ID={characterId}。当前角色数量={setupData.GetTotalCharacterCount()}。");
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
