using Godot;

public partial class DeleteMonster : Button
{
	private const string ParameterFormat = "怪物ID [删除数量]";

	public override void _Ready()
	{
		Pressed += OnDeleteMonsterPressed;
	}

	private void OnDeleteMonsterPressed()
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
			AppendConsoleInfo($"删除敌人 参数格式：{ParameterFormat}");
			return;
		}

		string[] arguments = ParseArguments(raw);
		if (arguments.Length == 0 || !int.TryParse(arguments[0], out int monsterId))
		{
			AppendConsoleError($"错误：怪物ID '{raw}' 不是合法数字。");
			return;
		}

		int deleteCount = 1;
		if (arguments.Length >= 2)
		{
			if (!int.TryParse(arguments[1], out deleteCount) || deleteCount <= 0)
			{
				AppendConsoleError($"错误：数量 '{arguments[1]}' 不是大于0的合法数字。");
				return;
			}
		}

		AppendConsoleInfo($"删除敌人 参数解析：怪物ID={monsterId}，删除数量={deleteCount}");

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法写入 BattleSetupData。");
			return;
		}

		BattleSetupData setupData = battleSytem.EnsureSetupData();
		int currentCount = setupData.GetMonsterIdCount(monsterId);
		if (currentCount <= 0)
		{
			AppendConsoleError($"错误：BattleSetupData 中不存在怪物ID {monsterId}。");
			return;
		}

		int removedCount = setupData.RemoveMonsterId(monsterId, deleteCount);
		battleSytem.SyncSelectedMonsterIdsFromSetupData();

		int remainCount = setupData.GetMonsterIdCount(monsterId);
		int totalCount = setupData.GetTotalMonsterCount();
		AppendConsoleInfo($"已删除怪物ID {monsterId} 共 {removedCount} 个。该类型剩余：{remainCount}，怪物总数：{totalCount}");
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