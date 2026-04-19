using Godot;

public partial class EndTurn : Button
{
    public override void _Ready()
    {
        Pressed += OnEndTurnPressed;
    }

    private void OnEndTurnPressed()
    {
        BattleSytem battleSytem = FindBattleSystem();
        if (battleSytem == null)
        {
            AppendConsoleError("错误：未找到 BattleSytem 节点，无法结束回合。", true);
            return;
        }

        battleSytem.EndPlayerTurn();
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
