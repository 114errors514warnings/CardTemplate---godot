using Godot;

public partial class AddPlayerEnergyRaw : BaseButtonCommand
{
	protected override string ParameterFormat => "[玩家UniqueInGameId] 增加能量值";

	public override void _Ready()
	{
		Pressed += OnAddEnergyPressed;
	}

	private void OnAddEnergyPressed()
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
			AppendConsoleInfo($"增加能量 参数格式：{ParameterFormat}");
			AppendConsoleInfo(BuildPlayerHint(FindBattleSystem()));
			return;
		}

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法增加能量。" );
			return;
		}

		if (!TryResolvePlayerScopedArguments(battleSytem, raw, 1, out int playerUniqueInGameId, out string[] valueArgs, out string resolveError))
		{
			AppendConsoleError($"错误：{resolveError} 参数格式：{ParameterFormat}");
			AppendConsoleInfo(BuildPlayerHint(battleSytem));
			return;
		}

		if (!int.TryParse(valueArgs[0], out int addEnergy))
		{
			AppendConsoleError($"错误：参数不是合法数字。参数格式：{ParameterFormat}");
			return;
		}

		if (!battleSytem.TryAddPlayerEnergyRaw(playerUniqueInGameId, addEnergy, out string resultMessage))
		{
			AppendConsoleError(resultMessage);
			return;
		}

		AppendConsoleInfo(resultMessage);
	}
}
