using Godot;
using System.Collections.Generic;

/// <summary>
/// 按钮命令基类 - 提供所有按钮脚本共用的错误检查和辅助方法
/// </summary>
public abstract partial class BaseButtonCommand : Button
{
	/// <summary>
	/// 获取该命令的参数格式说明（由子类提供）
	/// </summary>
	protected abstract string ParameterFormat { get; }

	/// <summary>
	/// 查找参数输入框
	/// </summary>
	protected LineEdit FindLineEdit()
	{
		for (Node current = this; current != null; current = current.GetParent())
		{
			if (current.Name != "操作面板")
			{
				continue;
			}

			LineEdit localLineEdit = current.GetNodeOrNull<LineEdit>("参数框/LineEdit");
			if (localLineEdit != null)
			{
				return localLineEdit;
			}
		}

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

		lineEdit = scene.GetNodeOrNull<LineEdit>("DebugBattle/操作面板/参数框/LineEdit");
		if (lineEdit != null)
		{
			return lineEdit;
		}

		return FindNodeRecursive<LineEdit>(scene);
	}

	/// <summary>
	/// 查找战斗系统节点
	/// </summary>
	protected BattleSytem FindBattleSystem()
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

	/// <summary>
	/// 递归查找指定类型的节点
	/// </summary>
	protected T FindNodeRecursive<T>(Node root) where T : Node
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

	/// <summary>
	/// 解析参数字符串为数组（支持多种分隔符）
	/// </summary>
	protected string[] ParseArguments(string raw)
	{
		return raw.Split(new char[] { ' ', '\t', ',', '，', ';', '；', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
	}

	/// <summary>
	/// 解析“[玩家UniqueInGameId] 值...”格式；仅传值时默认使用第一个可用玩家。
	/// </summary>
	protected bool TryResolvePlayerScopedArguments(BattleSytem battleSytem, string raw, int valueCount, out int playerUniqueInGameId, out string[] valueArgs, out string errorMessage)
	{
		playerUniqueInGameId = -1;
		valueArgs = System.Array.Empty<string>();
		errorMessage = string.Empty;

		if (battleSytem == null)
		{
			errorMessage = "未找到 BattleSytem 节点。";
			return false;
		}

		string[] args = ParseArguments(raw ?? string.Empty);
		if (args.Length < valueCount)
		{
			errorMessage = $"参数不足，需要至少 {valueCount} 个数值参数。";
			return false;
		}

		if (args.Length == valueCount)
		{
			CharacterInstance defaultPlayer = battleSytem.Player;
			if (defaultPlayer == null)
			{
				errorMessage = "当前没有可用玩家。";
				return false;
			}

			playerUniqueInGameId = defaultPlayer.UniqueInGameId;
			valueArgs = args;
			return true;
		}

		if (!int.TryParse(args[0], out playerUniqueInGameId))
		{
			errorMessage = $"玩家UniqueInGameId '{args[0]}' 不是合法数字。";
			return false;
		}

		if (!battleSytem.TryGetPlayerByUniqueId(playerUniqueInGameId, out _))
		{
			errorMessage = $"未找到玩家UniqueInGameId={playerUniqueInGameId}。";
			return false;
		}

		valueArgs = new string[args.Length - 1];
		for (int index = 1; index < args.Length; index++)
		{
			valueArgs[index - 1] = args[index];
		}

		if (valueArgs.Length < valueCount)
		{
			errorMessage = $"参数不足，需要至少 {valueCount} 个数值参数。";
			return false;
		}

		return true;
	}

	protected string BuildPlayerHint(BattleSytem battleSytem)
	{
		if (battleSytem == null)
		{
			return "当前没有可用玩家UniqueInGameId。";
		}

		List<CharacterInstance> players = battleSytem.GetAlivePlayers();
		if (players.Count == 0)
		{
			return "当前没有可用玩家UniqueInGameId。";
		}

		List<string> parts = new List<string>();
		foreach (CharacterInstance player in players)
		{
			parts.Add($"{player.UniqueInGameId}({player.Name})");
		}

		return "当前可用玩家UniqueInGameId：" + string.Join(", ", parts);
	}

	protected string BuildUnitHint(BattleSytem battleSytem)
	{
		if (battleSytem == null)
		{
			return "当前没有可用单位UniqueInGameId。";
		}

		List<string> parts = new List<string>();
		foreach (CharacterInstance player in battleSytem.GetAlivePlayers())
		{
			parts.Add($"{player.UniqueInGameId}({player.Name})");
		}

		if (battleSytem.Monsters != null)
		{
			foreach (MonsterInstance monster in battleSytem.Monsters.Values)
			{
				if (monster == null || monster.HP <= 0)
				{
					continue;
				}

				parts.Add($"{monster.UniqueInGameId}({monster.Name})");
			}
		}

		return parts.Count == 0
			? "当前没有可用单位UniqueInGameId。"
			: "当前可用单位UniqueInGameId：" + string.Join(", ", parts);
	}

	/// <summary>
	/// 在控制台输出错误信息
	/// </summary>
	protected void AppendConsoleError(string message)
	{
		SceneConsoleRouter.AppendError(message);
	}

	/// <summary>
	/// 在控制台输出信息
	/// </summary>
	protected void AppendConsoleInfo(string message)
	{
		SceneConsoleRouter.AppendInfo(message);
	}

	/// <summary>
	/// 在控制台输出消息（不带前缀）
	/// </summary>
	protected void AppendConsole(string message)
	{
		SceneConsoleRouter.AppendRaw(message);
	}
}
