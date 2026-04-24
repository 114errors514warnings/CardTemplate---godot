using Godot;
using System.Collections.Generic;

public partial class PlayCard : Button
{
	private const string ParameterFormat = "手牌index [目标敌人UniqueInGameId]";
	private const string MonsterKeyDescription = "目标敌人参数当前对应 BattleSytem.Monsters 的 key，即怪物实例的 UniqueInGameId。";

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
			AppendConsoleInfo(MonsterKeyDescription);
			AppendConsoleInfo("如果卡牌不需要目标，可将目标敌人UniqueInGameId填写为 -1。");
			AppendConsoleInfo(GetMonsterKeyHint(battleSytem));
			return;
		}

		string[] arguments = ParseArguments(raw);
		if (arguments.Length < 1)
		{
			AppendConsoleError($"错误：参数不足。参数格式：{ParameterFormat}", true);
			AppendConsoleInfo(MonsterKeyDescription);
			return;
		}

		if (!int.TryParse(arguments[0], out int handIndex) || handIndex < 0)
		{
			AppendConsoleError($"错误：手牌index '{arguments[0]}' 不是大于等于0的合法数字。", true);
			return;
		}

		if (battleSytem.Player == null)
		{
			AppendConsoleError("错误：玩家角色尚未初始化，无法执行出牌。", true);
			return;
		}

		if (handIndex >= battleSytem.Player.handcards.Count)
		{
			AppendConsoleError($"错误：手牌index {handIndex} 超出范围，当前手牌数量为 {battleSytem.Player.handcards.Count}。", true);
			return;
		}

		Card selectedCard = battleSytem.Player.handcards[handIndex];
		int targetMonsterUniqueInGameId = -1;
		if (arguments.Length >= 2)
		{
			if (!int.TryParse(arguments[1], out targetMonsterUniqueInGameId))
			{
				AppendConsoleError($"错误：目标敌人UniqueInGameId '{arguments[1]}' 不是合法数字。", true);
				AppendConsoleInfo(MonsterKeyDescription);
				return;
			}
		}

		if (selectedCard.NeedTarget && targetMonsterUniqueInGameId < 0)
		{
			AppendConsoleError($"错误：手牌index={handIndex} 的卡牌需要目标，请提供目标敌人UniqueInGameId。", true);
			AppendConsoleInfo(MonsterKeyDescription);
			AppendConsoleInfo(GetMonsterKeyHint(battleSytem));
			return;
		}

		IUnitInstance target = null;
		if (targetMonsterUniqueInGameId >= 0)
		{
			if (battleSytem.Monsters == null || !battleSytem.Monsters.TryGetValue(targetMonsterUniqueInGameId, out MonsterInstance monster))
			{
				AppendConsoleError($"错误：未找到目标敌人UniqueInGameId={targetMonsterUniqueInGameId}。{GetMonsterKeyHint(battleSytem)}", true);
				AppendConsoleInfo(MonsterKeyDescription);
				return;
			}

			target = monster;
		}

		AppendConsoleInfo($"出牌 参数解析：手牌index={handIndex}，目标敌人UniqueInGameId={targetMonsterUniqueInGameId}");
		bool played = battleSytem.PlayHandCard(handIndex, target);
		if (played)
		{
			AppendConsoleInfo("出牌操作完成。\n" + MonsterKeyDescription);
		}
	}

	private string[] ParseArguments(string raw)
	{
		return raw.Split(new char[] { ' ', '\t', ',', '，', ';', '；', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
	}

	private string GetMonsterKeyHint(BattleSytem battleSytem)
	{
		if (battleSytem.Monsters == null || battleSytem.Monsters.Count == 0)
		{
			return "当前没有已实例化怪物UniqueInGameId。";
		}

		List<int> keys = new List<int>(battleSytem.Monsters.Keys);
		keys.Sort();
		return "当前可用怪物UniqueInGameId：" + string.Join(", ", keys);
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