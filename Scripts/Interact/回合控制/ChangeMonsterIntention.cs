using Godot;

public partial class ChangeMonsterIntention : Button
{
	private const string ParameterFormat = "怪物UniqueInGameID 意图index";

	public override void _Ready()
	{
		Pressed += OnChangeMonsterIntentionPressed;
	}

	private void OnChangeMonsterIntentionPressed()
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
			AppendConsoleInfo($"修改意图 参数格式：{ParameterFormat}");
			return;
		}

		string[] arguments = ParseArguments(raw);
		if (arguments.Length < 2)
		{
			AppendConsoleError($"错误：参数不足。参数格式：{ParameterFormat}");
			return;
		}

		if (!int.TryParse(arguments[0], out int monsterUniqueInGameId))
		{
			AppendConsoleError($"错误：怪物UniqueInGameID '{arguments[0]}' 不是合法数字。");
			return;
		}

		if (!int.TryParse(arguments[1], out int intentionIndex) || intentionIndex <= 0)
		{
			AppendConsoleError($"错误：意图index '{arguments[1]}' 不是大于0的合法数字。");
			return;
		}

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法修改怪物意图。");
			return;
		}

		AppendConsoleInfo($"修改意图 参数解析：怪物UniqueInGameID={monsterUniqueInGameId}，意图index={intentionIndex}");

		if (!battleSytem.TrySwitchMonsterIntention(monsterUniqueInGameId, intentionIndex, out string resultMessage))
		{
			AppendConsoleError(resultMessage);
			return;
		}

		AppendConsoleInfo(resultMessage);
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