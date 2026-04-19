using Godot;

public partial class AddMonster : Button
{
	private const string DefaultMonsterCsvPath = "res://DataBase/Unit/Monster.csv";
	private const string ParameterFormat = "怪物ID [增加数量]";

	public override void _Ready()
	{
		Pressed += OnAddMonsterPressed;
	}

	private void OnAddMonsterPressed()
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
			AppendConsoleInfo($"新增敌人 参数格式：{ParameterFormat}");
			return;
		}

		string[] arguments = ParseArguments(raw);
		if (arguments.Length == 0 || !int.TryParse(arguments[0], out int monsterId))
		{
			AppendConsoleError($"错误：怪物ID '{raw}' 不是合法数字。");
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

		AppendConsoleInfo($"新增敌人 参数解析：怪物ID={monsterId}，增加数量={addCount}");

		EnsureMonsterCacheLoaded();
		if (!LoadingSystem.MonsterDictionary.ContainsKey(monsterId))
		{
			AppendConsoleError($"错误：怪物ID {monsterId} 未在 Monster.csv 中找到。");
			return;
		}

		BattleSytem battleSytem = FindBattleSystem();
		if (battleSytem == null)
		{
			AppendConsoleError("错误：未找到 BattleSytem 节点，无法写入 BattleSetupData。");
			return;
		}

		BattleSetupData setupData = battleSytem.EnsureSetupData();

		int actualAddedCount;
		if (battleSytem.IsBattleStarted)
		{
			actualAddedCount = battleSytem.AddMonsterInstancesByTemplateId(monsterId, addCount);
			if (actualAddedCount <= 0)
			{
				AppendConsoleError($"错误：战斗中怪物总数已达到上限 {BattleSetupData.MaxMonsterCapacity}，无法继续添加。当前已加载：{battleSytem.GetCurrentMonsterInstanceCount()}。");
				return;
			}

			// 仅同步实际成功创建的数量，避免配置与运行态不一致。
			setupData.AddMonsterId(monsterId, actualAddedCount);
			battleSytem.SyncSelectedMonsterIdsFromSetupData();

			if (actualAddedCount < addCount)
			{
				AppendConsoleInfo($"战斗中已达到怪物总数上限 {BattleSetupData.MaxMonsterCapacity}：请求添加 {addCount} 个，实际添加 {actualAddedCount} 个（模板ID: {monsterId}）。");
			}

			AppendConsoleInfo($"战斗中已新增怪物模板ID {monsterId} 共 {actualAddedCount} 个。当前战斗怪物数量：{battleSytem.GetCurrentMonsterInstanceCount()}/{BattleSetupData.MaxMonsterCapacity}");
		}
		else
		{
			int beforeTotalCount = setupData.GetTotalMonsterCount();
			if (beforeTotalCount >= BattleSetupData.MaxMonsterCapacity)
			{
				AppendConsoleError($"错误：怪物总数已达到上限 {BattleSetupData.MaxMonsterCapacity}，无法继续添加。");
				return;
			}

			actualAddedCount = setupData.AddMonsterId(monsterId, addCount);
			if (actualAddedCount <= 0)
			{
				AppendConsoleError($"错误：怪物总数已达到上限 {BattleSetupData.MaxMonsterCapacity}，无法继续添加。");
				return;
			}

			battleSytem.SyncSelectedMonsterIdsFromSetupData();

			int sameTypeCount = setupData.GetMonsterIdCount(monsterId);
			int totalCount = setupData.GetTotalMonsterCount();
			if (actualAddedCount < addCount)
			{
				AppendConsoleInfo($"已达到怪物总数上限 {BattleSetupData.MaxMonsterCapacity}：请求添加 {addCount} 个，实际添加 {actualAddedCount} 个（ID: {monsterId}）。");
			}

			AppendConsoleInfo($"已创建怪物ID {monsterId} 共 {actualAddedCount} 个。该类型当前数量：{sameTypeCount}，怪物总数：{totalCount}/{BattleSetupData.MaxMonsterCapacity}");
		}
	}

	private string[] ParseArguments(string raw)
	{
		return raw.Split(new char[] { ' ', '\t', ',', '，', ';', '；', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
	}

	private void EnsureMonsterCacheLoaded()
	{
		if (LoadingSystem.MonsterDictionary.Count == 0)
		{
			LoadingSystem.LoadMonsters(DefaultMonsterCsvPath, true);
		}
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

	private void AppendConsoleError(string message)
	{
		AppendConsole("[错误] " + message);
		GD.PrintErr(message);
	}

	private void AppendConsoleInfo(string message)
	{
		AppendConsole("[信息] " + message);
	}

	private void AppendConsole(string message)
	{
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
