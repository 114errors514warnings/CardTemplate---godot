using Godot;

public partial class SetPlayerMaxEnergy : BaseButtonCommand
{
	protected override string ParameterFormat => "能量上限";

	public override void _Ready()
	{
		Pressed += OnSetMaxEnergyPressed;
	}

	private void OnSetMaxEnergyPressed()
	{
		LineEdit lineEdit = FindLineEdit();
		if (lineEdit == null)
		{
			AppendConsoleError("错误：未找到参数框 LineEdit。路径应为 操作面板/参数框/LineEdit。");
			return;
		}

		string raw = lineEdit.Text == null ? string.Empty : lineEdit.Text.Trim();
		if (!int.TryParse(raw, out int maxEnergy))
		{
			AppendConsoleError($"错误：参数不是合法数字。参数格式：{ParameterFormat}");
			return;
		}

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法设置能量上限。" );
			return;
		}

		if (!battleSytem.TrySetPlayerMaxEnergy(maxEnergy, out string resultMessage))
		{
			AppendConsoleError(resultMessage);
			return;
		}

		AppendConsoleInfo(resultMessage);
	}
}
