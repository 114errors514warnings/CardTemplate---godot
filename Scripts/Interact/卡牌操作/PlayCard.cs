using Godot;
using System.Collections.Generic;

public partial class PlayCard : Button
{
	private const string ParameterFormat = "[操作者玩家UniqueInGameId] 手牌顺序 [目标UniqueInGameId]";
	private const string TargetDescription = "目标参数当前对应任意单位的 UniqueInGameId；若卡牌不需要目标，可省略。";

	public override void _Ready()
	{
		Pressed += OnPlayCardPressed;
	}

	private void OnPlayCardPressed()
	{
		LineEdit lineEdit = FindLineEdit();
		if (lineEdit == null)
		{
			AppendConsoleError("错误：未找到参数框 LineEdit。路径应为 操作面板/参数框/LineEdit。", true);
			return;
		}

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法执行出牌。", true);
			return;
		}

		string raw = lineEdit.Text == null ? string.Empty : lineEdit.Text.Trim();
		if (string.IsNullOrEmpty(raw))
		{
			AppendConsoleInfo($"出牌 参数格式：{ParameterFormat}");
			AppendConsoleInfo(TargetDescription);
			AppendConsoleInfo("如果卡牌不需要目标，可将目标敌人UniqueInGameId填写为 -1。");
			AppendConsoleInfo(GetPlayerKeyHint(battleSytem));
			AppendConsoleInfo(GetUnitKeyHint(battleSytem));
			return;
		}

		string[] arguments = ParseArguments(raw);
		if (arguments.Length < 1)
		{
			AppendConsoleError($"错误：参数不足。参数格式：{ParameterFormat}", true);
			AppendConsoleInfo(TargetDescription);
			return;
		}

		int playerUniqueInGameId;
		int handOrder;
		int targetUniqueInGameId = -1;

		if (arguments.Length >= 3)
		{
			if (!int.TryParse(arguments[0], out playerUniqueInGameId))
			{
				AppendConsoleError($"错误：操作者玩家UniqueInGameId '{arguments[0]}' 不是合法数字。", true);
				return;
			}

			if (!int.TryParse(arguments[1], out handOrder) || handOrder <= 0)
			{
				AppendConsoleError($"错误：手牌顺序 '{arguments[1]}' 不是大于0的合法数字。", true);
				return;
			}

			if (!int.TryParse(arguments[2], out targetUniqueInGameId))
			{
				AppendConsoleError($"错误：目标UniqueInGameId '{arguments[2]}' 不是合法数字。", true);
				return;
			}
		}
		else
		{
			if (!int.TryParse(arguments[0], out handOrder) || handOrder <= 0)
			{
				AppendConsoleError($"错误：手牌顺序 '{arguments[0]}' 不是大于0的合法数字。", true);
				return;
			}

			playerUniqueInGameId = battleSytem.Player?.UniqueInGameId ?? -1;
			if (arguments.Length >= 2)
			{
				if (!int.TryParse(arguments[1], out targetUniqueInGameId))
				{
					AppendConsoleError($"错误：目标UniqueInGameId '{arguments[1]}' 不是合法数字。", true);
					return;
				}
			}
		}

		int handIndex = handOrder - 1;

		if (!battleSytem.TryGetPlayerByUniqueId(playerUniqueInGameId, out CharacterInstance player))
		{
			AppendConsoleError("错误：玩家角色尚未初始化，无法执行出牌。", true);
			AppendConsoleInfo(GetPlayerKeyHint(battleSytem));
			return;
		}

		if (handIndex >= player.handcards.Count)
		{
			AppendConsoleError($"错误：手牌顺序 {handOrder} 超出范围，当前手牌数量为 {player.handcards.Count}。", true);
			return;
		}

		Card selectedCard = player.handcards[handIndex];

		if (selectedCard.NeedTarget && targetUniqueInGameId < 0)
		{
			AppendConsoleError($"错误：手牌顺序={handOrder} 的卡牌需要目标，请提供目标敌人UniqueInGameId。", true);
			AppendConsoleInfo(TargetDescription);
			AppendConsoleInfo(GetUnitKeyHint(battleSytem));
			return;
		}

		IUnitInstance target = null;
		if (targetUniqueInGameId >= 0)
		{
			foreach (IUnitInstance unit in battleSytem.GetAllUnits())
			{
				if (unit != null && unit.UniqueInGameId == targetUniqueInGameId)
				{
					target = unit;
					break;
				}
			}

			if (target == null)
			{
				AppendConsoleError($"错误：未找到目标UniqueInGameId={targetUniqueInGameId}。{GetUnitKeyHint(battleSytem)}", true);
				AppendConsoleInfo(TargetDescription);
				return;
			}
		}

		AppendConsoleInfo($"出牌 参数解析：操作者玩家UniqueInGameId={playerUniqueInGameId}，手牌顺序={handOrder}，内部index={handIndex}，目标UniqueInGameId={targetUniqueInGameId}");
		bool played = battleSytem.PlayHandCard(playerUniqueInGameId, handIndex, target);
		if (played)
		{
			AppendConsoleInfo("出牌操作完成。");
		}
	}

	private string[] ParseArguments(string raw)
	{
		return raw.Split(new char[] { ' ', '\t', ',', '，', ';', '；', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
	}

	private string GetPlayerKeyHint(BattleSytem battleSytem)
	{
		List<CharacterInstance> players = battleSytem.GetAlivePlayers();
		if (players.Count == 0)
		{
			return "当前没有可操作的玩家UniqueInGameId。";
		}

		List<string> parts = new List<string>();
		foreach (CharacterInstance player in players)
		{
			parts.Add($"{player.UniqueInGameId}({player.Name})");
		}

		return "当前可用玩家UniqueInGameId：" + string.Join(", ", parts);
	}

	private string GetUnitKeyHint(BattleSytem battleSytem)
	{
		List<string> parts = new List<string>();
		foreach (IUnitInstance unit in battleSytem.GetAllUnits())
		{
			if (unit is Unit typedUnit)
			{
				parts.Add($"{unit.UniqueInGameId}({typedUnit.Name})");
			}
		}

		return parts.Count == 0
			? "当前没有可用目标UniqueInGameId。"
			: "当前可用目标UniqueInGameId：" + string.Join(", ", parts);
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

	private void AppendConsoleInfo(string message)
	{
		AppendConsole("[信息] " + message, false);
	}

	private void AppendConsoleError(string message, bool alsoPrintError)
	{
		AppendConsole("[错误] " + message, alsoPrintError);
	}

	private void AppendConsole(string message, bool alsoPrintError)
	{
		if (alsoPrintError)
		{
			GD.PrintErr(message);
		}

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