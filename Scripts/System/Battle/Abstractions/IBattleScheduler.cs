// Abstractions/IBattleScheduler.cs
// 异步调度端口 —— 替代 BattleSytem.GetTree().CreateTimer(...) 制造的 0.5s 间隔。
// 单测里用同步推进器即可跑过怪物回合。

using System.Threading.Tasks;

public interface IBattleScheduler
{
    Task DelayAsync(float seconds);
}
