using Godot;

public partial class DeleteMonster : Button
{
    private const string ParameterFormat = "怪物ID [删除数量]";

    public override void _Ready()
    {
        Pressed += OnDeleteMonsterPressed;
    }

    private void OnDeleteMonsterPressed()
    {
        LineEdit lineEdit = FindLineEdit();
        if (lineEdit == null)
        {
            AppendConsoleError("错误：未找到参数框 LineEdit。路径应为 操作面板/参数框/LineEdit。");
            return;
        }

        string raw = lineEdit.Text == null ? string.Empty : lineEdit.Text.Trim();

        BattleSytem battleSytem = FindBattleSystem();
        if (battleSytem == null)
        {
            AppendConsoleError("错误：未找到 BattleSytem 节点，无法写入 BattleSetupData。");
            return;
        }

        BattleSetupData setupData = battleSytem.EnsureSetupData();

        if (string.IsNullOrEmpty(raw))
        {
            // 输入为空，默认删除最后一个敌人
            System.Collections.Generic.List<int> monsterList = setupData.GetMonsterIdList();
            if (monsterList.Count == 0)
            {
                AppendConsoleError("错误：当前没有可删除的敌人。");
                return;
            }

            int lastMonsterId = monsterList[monsterList.Count - 1];
            int removedCount = setupData.RemoveMonsterId(lastMonsterId, 1);
            battleSytem.SyncSelectedMonsterIdsFromSetupData();

            int remainCount = setupData.GetMonsterIdCount(lastMonsterId);
            int totalCount = setupData.GetTotalMonsterCount();
            AppendConsoleInfo($"已删除最后一个敌人（怪物ID {lastMonsterId}）共 {removedCount} 个。该类型剩余：{remainCount}，怪物总数：{totalCount}");
            return;
        }

        string[] arguments = ParseArguments(raw);
        if (arguments.Length == 0 || !int.TryParse(arguments[0], out int monsterId))
        {
            AppendConsoleError($"错误：怪物ID '{raw}' 不是合法数字。");
            return;
        }

        int deleteCount = 1;
        if (arguments.Length >= 2)
        {
            if (!int.TryParse(arguments[1], out deleteCount) || deleteCount <= 0)
            {
                AppendConsoleError($"错误：数量 '{arguments[1]}' 不是大于0的合法数字。");
                return;
            }
        }

        AppendConsoleInfo($"删除敌人 参数解析：怪物ID={monsterId}，删除数量={deleteCount}");

        int currentCount = setupData.GetMonsterIdCount(monsterId);
        if (currentCount <= 0)
        {
            AppendConsoleError($"错误：BattleSetupData 中不存在怪物ID {monsterId}。");
            return;
        }

        int removed = setupData.RemoveMonsterId(monsterId, deleteCount);
        battleSytem.SyncSelectedMonsterIdsFromSetupData();

        int remain = setupData.GetMonsterIdCount(monsterId);
        int total = setupData.GetTotalMonsterCount();
        AppendConsoleInfo($"已删除怪物ID {monsterId} 共 {removed} 个。该类型剩余：{remain}，怪物总数：{total}");
    }

    private string[] ParseArguments(string raw)
    {
        return raw.Split(new char[] { ' ', '\t', ',', '，', ';', '；', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
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
