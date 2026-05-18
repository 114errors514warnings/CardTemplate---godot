using Godot;

public partial class ChooseCard : BaseButtonCommand
{
	protected override string ParameterFormat => "手牌顺序（从1开始）";

	public override void _Ready()
	{
		Pressed += OnChooseCardPressed;
	}

	private void OnChooseCardPressed()
	{
		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("未找到 BattleSytem 节点。");
			return;
		}

		LineEdit lineEdit = FindLineEdit();
		if (lineEdit == null)
		{
			AppendConsoleError("未找到参数输入框。路径应为 操作面板/参数框/LineEdit。");
			return;
		}

		string raw = lineEdit.Text == null ? string.Empty : lineEdit.Text.Trim();
		if (string.IsNullOrWhiteSpace(raw))
		{
			AppendConsoleInfo($"选择卡牌 参数格式：{ParameterFormat}");
			string pendingPrompt = battleSytem.GetPendingCardSelectionPrompt();
			if (!string.IsNullOrWhiteSpace(pendingPrompt))
			{
				AppendConsoleInfo(pendingPrompt);
			}
			return;
		}

		string[] args = ParseArguments(raw);
		if (args.Length <= 0 || !int.TryParse(args[0], out int handOrder) || handOrder <= 0)
		{
			AppendConsoleError($"参数错误。参数格式：{ParameterFormat}");
			return;
		}

		if (!battleSytem.TrySelectPendingHandCard(handOrder - 1, out string resultMessage))
		{
			AppendConsoleError(resultMessage);
			return;
		}

		if (!string.IsNullOrWhiteSpace(resultMessage))
		{
			AppendConsoleInfo(resultMessage);
		}
	}
}