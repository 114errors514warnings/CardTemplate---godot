// GodotConsoleAdapter.cs
// 默认的 IBattleConsole 实现 —— 走 RichTextLabel（BattleInfoLabelPath），
// 找不到时回落 GD.Print，行为和原 BattleSytem.AppendPanelConsole 保持一致。

using Godot;
using System;
using CardSimulator;

internal sealed class GodotConsoleAdapter : IBattleConsole
{
    private readonly BattleSytem battle;
    private const string BattleInfoLabelPath = "局内信息/对局信息滚动/对局信息显示";
    private const string BattleInfoLabelPathInRoot = "UI_Main/局内信息/对局信息滚动/对局信息显示";

    public GodotConsoleAdapter(BattleSytem battle)
    {
        this.battle = battle ?? throw new ArgumentNullException(nameof(battle));
    }

    public void Info(string message) => Append("[信息]" + message);

    public void Error(string message) => Append("[错误]" + message);

    private void Append(string message)
    {
        Node scene = battle.GetTree()?.CurrentScene;
        if (scene == null)
        {
            GD.Print(message);
            return;
        }

        RichTextLabel label = scene.GetNodeOrNull<RichTextLabel>(BattleInfoLabelPath)
                              ?? scene.GetNodeOrNull<RichTextLabel>(BattleInfoLabelPathInRoot);
        if (label == null)
        {
            GD.Print(message);
            return;
        }

        label.Text += message + "\n";
    }
}
