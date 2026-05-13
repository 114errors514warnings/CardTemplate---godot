using Godot;

public partial class DeleteCharacter : Button
{
	public override void _Ready()
	{
		Pressed += OnDeleteCharacterPressed;
	}

	private void OnDeleteCharacterPressed()
	{
		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法写入 BattleSetupData。");
			return;
		}

		if (battleSytem.IsBattleStarted)
		{
			AppendConsoleInfo("删除角色仅在战斗开始前生效。当前已开始战斗，本次操作已忽略。");
			return;
		}

		BattleSetupData setupData = battleSytem.EnsureSetupData();
		int removedCharacterId = setupData.RemoveLastCharacter();
		if (removedCharacterId <= 0)
		{
			AppendConsoleError("错误：当前没有可删除的角色。");
			return;
		}

		battleSytem.SelectedCharacterId = setupData.GetTotalCharacterCount() > 0 ? setupData.GetCharacterIdList()[0] : 0;
		battleSytem.RefreshBattleInfoDisplay();
		AppendConsoleInfo($"已删除最后一个角色槽位，移除的角色ID={removedCharacterId}。当前角色数量={setupData.GetTotalCharacterCount()}。");
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