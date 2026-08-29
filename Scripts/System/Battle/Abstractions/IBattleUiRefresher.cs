// Abstractions/IBattleUiRefresher.cs
// UI 刷新请求端口 —— 替代 BattleSytem.RefreshBattleInfoDisplay() + NotifyBattleSceneRefresh()
// 任何"操作完成后请 UI 重新拉一遍"的需求都走这个口子，单测里直接 no-op 即可。

public interface IBattleUiRefresher
{
    void RequestRefresh();
}
