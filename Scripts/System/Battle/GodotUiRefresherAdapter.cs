// GodotUiRefresherAdapter.cs
// 默认的 IBattleUiRefresher 实现 —— 调 CardBattleScene.RequestExternalUiRefresh()，
// 同时强制 BattleSytem 重新走一遍 BattleInfoPresenter（与原 RefreshBattleInfoDisplay 行为对齐）。

using Godot;
using System;

internal sealed class GodotUiRefresherAdapter : IBattleUiRefresher
{
    private readonly BattleSytem battle;

    public GodotUiRefresherAdapter(BattleSytem battle)
    {
        this.battle = battle ?? throw new ArgumentNullException(nameof(battle));
    }

    public void RequestRefresh()
    {
        battle.InvokeRefreshBattleInfoDisplay();
        Node scene = battle.GetTree()?.CurrentScene;
        CardBattleScene cardBattleScene = FindCardBattleScene(scene);
        cardBattleScene?.RequestExternalUiRefresh();
    }

    // P0#9：运行局(RunBattleScene)把原战斗场景作为子节点实例化，
    // CurrentScene 不再直接是 CardBattleScene，这里递归查找。
    private static CardBattleScene FindCardBattleScene(Node root)
    {
        if (root == null)
        {
            return null;
        }

        if (root is CardBattleScene direct)
        {
            return direct;
        }

        foreach (Node child in root.GetChildren())
        {
            CardBattleScene found = FindCardBattleScene(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
