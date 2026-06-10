using Godot;

public partial class ChangePileOrder : Button
{
	public override void _Ready()
	{
		Pressed += OnChangePileOrderPressed;
	}

	private void OnChangePileOrderPressed()
	{
		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法切换牌堆显示顺序。", true);
			return;
		}

		bool isPileOrder = battleSytem.TogglePileDisplayOrderMode();
		if (isPileOrder)
		{
			AppendConsoleInfo("已切换为按牌堆顺序显示抽牌堆和弃牌堆卡牌。", false);
			return;
		}

		AppendConsoleInfo("已切换为按ID排序显示抽牌堆和弃牌堆卡牌（CardId升序，其次UniqueInGameId升序）。", false);
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

	private void AppendConsoleInfo(string message, bool alsoPrint)
	{
		SceneConsoleRouter.AppendRaw("[信息] " + message, alsoPrint);
	}

	private void AppendConsoleError(string message, bool alsoPrint)
	{
		SceneConsoleRouter.AppendRaw("[错误] " + message, alsoPrint);
	}

	private void AppendConsole(string message, bool alsoPrint)
	{
		SceneConsoleRouter.AppendRaw(message, alsoPrint);
	}
}
