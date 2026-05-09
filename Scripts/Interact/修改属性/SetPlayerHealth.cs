using Godot;

public partial class SetPlayerHealth : BaseButtonCommand
{
	protected override string ParameterFormat => "当前生命值 最大生命值";

	public override void _Ready()
	{
		Pressed += OnSetHealthPressed;
	}

	private void OnSetHealthPressed()
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
			AppendConsoleInfo($"生命 参数格式：{ParameterFormat}");
			return;
		}

		string[] args = ParseArguments(raw);
		if (args.Length < 2)
		{
			AppendConsoleError($"错误：参数不足。参数格式：{ParameterFormat}");
			return;
		}

		if (!int.TryParse(args[0], out int hp) || !int.TryParse(args[1], out int maxHp))
		{
			AppendConsoleError($"错误：参数必须为整数。参数格式：{ParameterFormat}");
			return;
		}

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法设置生命。" );
			return;
		}

		if (!battleSytem.TrySetPlayerHealth(hp, maxHp, out string resultMessage))
		{
			AppendConsoleError(resultMessage);
			return;
		}

		AppendConsoleInfo(resultMessage);
	}
}
