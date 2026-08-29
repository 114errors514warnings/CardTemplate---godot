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
        if (scene is CardBattleScene cardBattleScene)
        {
            cardBattleScene.RequestExternalUiRefresh();
        }
    }
}
