using Godot;

public partial class AddCharacterCard : Button
{
	private const string ParameterFormat = "卡牌ID [增加数量]";

	public override void _Ready()
	{
		Pressed += OnAddCharacterCardPressed;
	}

	private void OnAddCharacterCardPressed()
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
			AppendConsoleInfo($"新增角色卡牌 参数格式：{ParameterFormat}");
			return;
		}

		string[] arguments = ParseArguments(raw);
		if (arguments.Length == 0 || !int.TryParse(arguments[0], out int cardId))
		{
			AppendConsoleError($"错误：卡牌ID '{raw}' 不是合法数字。");
			return;
		}

		int addCount = 1;
		if (arguments.Length >= 2)
		{
			if (!int.TryParse(arguments[1], out addCount) || addCount <= 0)
			{
				AppendConsoleError($"错误：数量 '{arguments[1]}' 不是大于0的合法数字。");
				return;
			}
		}

		AppendConsoleInfo($"新增角色卡牌 参数解析：卡牌ID={cardId}，增加数量={addCount}");

		EnsureCardCacheLoaded();
		if (!LoadingSystem.CardDictionary.ContainsKey(cardId))
		{
			AppendConsoleError($"错误：卡牌ID {cardId} 未在 Card.csv 中找到。");
			return;
		}

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法写入 BattleSetupData。");
			return;
		}

		if (battleSytem.IsBattleStarted)
		{
			AppendConsoleError("错误：战斗已开始，当前仅支持在战斗开始前添加角色卡牌。");
			return;
		}

		BattleSetupData setupData = battleSytem.EnsureSetupData();
		int actualAddedCount = setupData.AddCharacterCardId(cardId, addCount);
		battleSytem.RefreshBattleInfoDisplay();

		int sameTypeCount = setupData.GetCharacterCardIdCount(cardId);
		int totalCount = setupData.GetTotalCharacterCardCount();
		AppendConsoleInfo($"已添加角色卡牌ID {cardId} 共 {actualAddedCount} 个。该类型当前数量：{sameTypeCount}，角色卡牌总数：{totalCount}");
	}

	private string[] ParseArguments(string raw)
	{
		return raw.Split(new char[] { ' ', '\t', ',', '，', ';', '；', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
	}

	private void EnsureCardCacheLoaded()
	{
		if (LoadingSystem.CardDictionary.Count == 0)
		{
			LoadingSystem.LoadCardsByKey(LoadingSystem.CardCsvPathKey, true);
		}
	}

	private LineEdit FindLineEdit()
	{
		Node panel = FindAncestorByName(this, "操作面板");
		if (panel != null)
		{
			LineEdit panelLineEdit = panel.GetNodeOrNull<LineEdit>("参数框/LineEdit");
			if (panelLineEdit != null)
			{
				return panelLineEdit;
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

		return scene.GetNodeOrNull<LineEdit>("DebugBattle/操作面板/参数框/LineEdit");
	}

	private Node FindAncestorByName(Node start, string targetName)
	{
		Node current = start;
		while (current != null)
		{
			if (current.Name == targetName)
			{
				return current;
			}

			current = current.GetParent();
		}

		return null;
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
		SceneConsoleRouter.AppendError(message);
	}

	private void AppendConsoleInfo(string message)
	{
		SceneConsoleRouter.AppendInfo(message);
	}

	private void AppendConsole(string message)
	{
		SceneConsoleRouter.AppendRaw(message);
	}
}
