// GodotSchedulerAdapter.cs
// 默认的 IBattleScheduler 实现 —— 用 SceneTreeTimer 制造异步延迟，
// 与原 BattleSytem.StartMonsterTurn 里的 await ToSignal(GetTree().CreateTimer(0.5f), ...) 一致。

using Godot;
using System.Threading.Tasks;

internal sealed class GodotSchedulerAdapter : IBattleScheduler
{
    private readonly BattleSytem battle;

    public GodotSchedulerAdapter(BattleSytem battle)
    {
        this.battle = battle;
    }

    public async Task DelayAsync(float seconds)
    {
        if (battle == null || !Godot.GodotObject.IsInstanceValid(battle))
        {
            return;
        }
        SceneTree tree = battle.GetTree();
        if (tree == null)
        {
            return;
        }
        await battle.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }
}
