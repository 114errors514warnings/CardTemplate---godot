using Godot;

public partial class SetPlayerDefend : BaseButtonCommand
{
	protected override string ParameterFormat => "[玩家UniqueInGameId] 防御值";

	public override void _Ready()
	{
		Pressed += OnSetDefendPressed;
	}

	private void OnSetDefendPressed()
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
			AppendConsoleInfo($"防御 参数格式：{ParameterFormat}");
			AppendConsoleInfo(BuildPlayerHint(FindBattleSystem()));
			return;
		}

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法设置防御。" );
			return;
		}

		if (!TryResolvePlayerScopedArguments(battleSytem, raw, 1, out int playerUniqueInGameId, out string[] valueArgs, out string resolveError))
		{
			AppendConsoleError($"错误：{resolveError} 参数格式：{ParameterFormat}");
			AppendConsoleInfo(BuildPlayerHint(battleSytem));
			return;
		}

		if (!int.TryParse(valueArgs[0], out int defend))
		{
			AppendConsoleError($"错误：参数不是合法数字。参数格式：{ParameterFormat}");
			return;
		}

		if (!battleSytem.TrySetPlayerDefend(playerUniqueInGameId, defend, out string resultMessage))
		{
			AppendConsoleError(resultMessage);
			return;
		}

		AppendConsoleInfo(resultMessage);
	}
}
