using Godot;

public partial class DeleteCharacter : Button
{
    private const string ParameterFormat = "角色ID";

    public override void _Ready()
    {
        Pressed += OnDeleteCharacterPressed;
    }

    private void OnDeleteCharacterPressed()
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

        if (battleSytem.IsBattleStarted)
        {
            AppendConsoleInfo("删除角色仅在战斗开始前生效。当前已开始战斗，本次操作已忽略。");
            return;
        }

        BattleSetupData setupData = battleSytem.EnsureSetupData();

        if (string.IsNullOrEmpty(raw))
        {
            // 输入为空，默认删除最后一个角色
            int removedCharacterId = setupData.RemoveLastCharacter();
            if (removedCharacterId <= 0)
            {
                AppendConsoleError("错误：当前没有可删除的角色。");
                return;
            }

            battleSytem.SelectedCharacterId = setupData.GetTotalCharacterCount() > 0 ? setupData.GetCharacterIdList()[0] : 0;
            battleSytem.RefreshBattleInfoDisplay();
            AppendConsoleInfo($"已删除最后一个角色槽位，移除的角色ID={removedCharacterId}。当前角色数量={setupData.GetTotalCharacterCount()}。");
            return;
        }

        // 输入了ID，按ID匹配
        if (!int.TryParse(raw, out int characterId))
        {
            AppendConsoleError($"错误：角色ID '{raw}' 不是合法数字。参数格式：{ParameterFormat}");
            return;
        }

        int currentCount = setupData.GetCharacterIdCount(characterId);
        if (currentCount <= 0)
        {
            AppendConsoleError($"错误：BattleSetupData 中不存在角色ID {characterId}，未删除任何角色。");
            return;
        }

        int removed = setupData.RemoveCharacterId(characterId, 1);
        battleSytem.SelectedCharacterId = setupData.GetTotalCharacterCount() > 0 ? setupData.GetCharacterIdList()[0] : 0;
        battleSytem.RefreshBattleInfoDisplay();

        int remainCount = setupData.GetCharacterIdCount(characterId);
        AppendConsoleInfo($"已删除角色ID {characterId} 共 {removed} 个（删除队列中最后匹配项）。该类型剩余：{remainCount}，角色总数：{setupData.GetTotalCharacterCount()}。");
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
