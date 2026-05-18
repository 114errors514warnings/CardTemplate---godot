using Godot;

public partial class DeleteState : BaseButtonCommand
{
	protected override string ParameterFormat => "目标UniqueInGameId 状态ID [层数]";

	public override void _Ready()
	{
		Pressed += OnDeleteStatePressed;
	}

	private void OnDeleteStatePressed()
	{
		LineEdit lineEdit = FindLineEdit();
		if (lineEdit == null)
		{
			AppendConsoleError("错误：未找到参数框 LineEdit。路径应为 操作面板/参数框/LineEdit。");
			return;
		}

		string raw = lineEdit.Text == null ? string.Empty : lineEdit.Text.Trim();
		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法删除状态。");
			return;
		}

		if (string.IsNullOrEmpty(raw))
		{
			AppendConsoleInfo($"删除状态 参数格式：{ParameterFormat}");
			AppendConsoleInfo(BuildUnitHint(battleSytem));
			return;
		}

		string[] args = ParseArguments(raw);
		if (args.Length < 2 || args.Length > 3)
		{
			AppendConsoleError($"错误：参数格式不正确。参数格式：{ParameterFormat}");
			AppendConsoleInfo(BuildUnitHint(battleSytem));
			return;
		}

		if (!int.TryParse(args[0], out int targetUniqueInGameId))
		{
			AppendConsoleError($"错误：目标UniqueInGameId '{args[0]}' 不是合法数字。参数格式：{ParameterFormat}");
			return;
		}

		if (!int.TryParse(args[1], out int rawStateType))
		{
			AppendConsoleError($"错误：状态ID '{args[1]}' 不是合法数字。参数格式：{ParameterFormat}");
			return;
		}

		int? stacks = null;
		if (args.Length >= 3)
		{
			if (!int.TryParse(args[2], out int parsedStacks))
			{
				AppendConsoleError($"错误：层数 '{args[2]}' 不是合法数字。参数格式：{ParameterFormat}");
				return;
			}

			stacks = parsedStacks;
		}

		if (!battleSytem.TryRemoveStateFromUnit(targetUniqueInGameId, rawStateType, stacks, out string resultMessage))
		{
			AppendConsoleError(resultMessage);
			AppendConsoleInfo(BuildUnitHint(battleSytem));
			return;
		}

		AppendConsoleInfo(resultMessage);
	}
}