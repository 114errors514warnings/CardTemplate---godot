using Godot;

public partial class EndGame : Button
{
	public override void _Ready()
	{
		Pressed += OnEndGamePressed;
	}

	private void OnEndGamePressed()
	{
		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法结束游戏。");
			return;
		}

		AppendConsoleInfo("结束游戏：重置战场并返回战前配置。");
		battleSytem.EndGame();

		// Return to setup window via the scene
		Node scene = GetTree().CurrentScene;
		if (scene != null && scene.HasMethod("RequestExternalUiRefresh"))
		{
			scene.CallDeferred("RequestExternalUiRefresh");
			scene.CallDeferred("ShowSetupWindow");
		}
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