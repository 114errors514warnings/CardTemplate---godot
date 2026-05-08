using Godot;

public partial class RemoveCharacterCard : Button
{
	private const string ParameterFormat = "卡牌ID [删除数量]";

	public override void _Ready()
	{
		Pressed += OnRemoveCharacterCardPressed;
	}

	private void OnRemoveCharacterCardPressed()
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
			AppendConsoleInfo($"删除角色卡牌 参数格式：{ParameterFormat}");
			return;
		}

		string[] arguments = ParseArguments(raw);
		if (arguments.Length == 0 || !int.TryParse(arguments[0], out int cardId))
		{
			AppendConsoleError($"错误：卡牌ID '{raw}' 不是合法数字。");
			return;
		}

		int removeCount = 1;
		if (arguments.Length >= 2)
		{
			if (!int.TryParse(arguments[1], out removeCount) || removeCount <= 0)
			{
				AppendConsoleError($"错误：数量 '{arguments[1]}' 不是大于0的合法数字。");
				return;
			}
		}

		AppendConsoleInfo($"删除角色卡牌 参数解析：卡牌ID={cardId}，删除数量={removeCount}");

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法写入 BattleSetupData。");
			return;
		}

		if (battleSytem.IsBattleStarted)
		{
			AppendConsoleError("错误：战斗已开始，当前仅支持在战斗开始前删除角色卡牌。");
			return;
		}

		BattleSetupData setupData = battleSytem.EnsureSetupData();
		int beforeCount = setupData.GetCharacterCardIdCount(cardId);
		if (beforeCount <= 0)
		{
			AppendConsoleError($"错误：当前配置中不包含卡牌ID {cardId}，无法删除。");
			return;
		}

		int actualRemovedCount = setupData.RemoveCharacterCardId(cardId, removeCount);
		battleSytem.RefreshBattleInfoDisplay();

		int sameTypeCount = setupData.GetCharacterCardIdCount(cardId);
		int totalCount = setupData.GetTotalCharacterCardCount();
		AppendConsoleInfo($"已删除角色卡牌ID {cardId} 共 {actualRemovedCount} 张。该类型剩余数量：{sameTypeCount}，角色卡牌总数：{totalCount}");
	}

	private string[] ParseArguments(string raw)
	{
		return raw.Split(new char[] { ' ', '\t', ',', '，', ';', '；', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
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
