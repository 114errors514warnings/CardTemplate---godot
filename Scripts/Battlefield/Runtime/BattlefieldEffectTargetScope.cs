using System;
using System.Collections.Generic;
using System.Threading;
using CardSimulator;

namespace CardSimulator.Battlefield;

/// <summary>
/// Limits Card.Apply target expansion to the spatially validated units for one synchronous action.
/// The existing effect/state/damage implementation remains the only effect executor.
/// </summary>
public sealed class BattlefieldEffectTargetScope : IDisposable
{
    private static readonly AsyncLocal<BattlefieldEffectTargetScope> current = new();
    private readonly BattlefieldEffectTargetScope previous;
    private readonly IUnitInstance source;
    private readonly IUnitInstance selected;
    private readonly IReadOnlyList<IUnitInstance> enemies;
    private readonly IReadOnlyList<IUnitInstance> allies;
    private readonly int sourceLostHp;
    private readonly bool playerTurn;
    private readonly Func<IUnitInstance, IUnitInstance, bool> canAttack;

    public static BattlefieldEffectTargetScope Current => current.Value;

    public BattlefieldEffectTargetScope(IUnitInstance source, IUnitInstance selected,
        IReadOnlyList<IUnitInstance> enemies, IReadOnlyList<IUnitInstance> allies, int sourceLostHp = 0,
        bool playerTurn = true, Func<IUnitInstance, IUnitInstance, bool> canAttack = null)
    {
        this.source = source;
        this.selected = selected;
        this.enemies = enemies ?? Array.Empty<IUnitInstance>();
        this.allies = allies ?? Array.Empty<IUnitInstance>();
        this.sourceLostHp = Math.Max(0, sourceLostHp);
        this.playerTurn = playerTurn;
        this.canAttack = canAttack;
        previous = current.Value;
        current.Value = this;
    }

    public int GetBattleLostHp(IUnitInstance requestedSource) => ReferenceEquals(source, requestedSource) ? sourceLostHp : 0;
    public bool IsOutOfTurn(IUnitInstance unit) => unit is CharacterInstance ? !playerTurn : unit is MonsterInstance && playerTurn;
    public bool CanAttack(IUnitInstance attacker, IUnitInstance target) => canAttack?.Invoke(attacker, target) ?? true;
    public IReadOnlyList<IUnitInstance> Enemies => enemies;

    public bool TryResolve(IUnitInstance requestedSource, IUnitInstance requestedSelected,
        EffectTargetType type, out List<IUnitInstance> result)
    {
        result = new List<IUnitInstance>();
        if (!ReferenceEquals(source, requestedSource)) return false;
        switch (type)
        {
            case EffectTargetType.Self:
                if (source != null) result.Add(source);
                break;
            case EffectTargetType.SelectedTarget:
                IUnitInstance target = selected ?? requestedSelected;
                if (target != null) result.Add(target);
                break;
            case EffectTargetType.AllEnemies:
                result.AddRange(enemies);
                break;
            case EffectTargetType.AllAllies:
                result.AddRange(allies);
                break;
            case EffectTargetType.AllUnits:
                result.AddRange(allies);
                foreach (var enemy in enemies) if (!result.Contains(enemy)) result.Add(enemy);
                if (source != null && !result.Contains(source)) result.Add(source);
                break;
            case EffectTargetType.Auto:
            default:
                result.Add(selected ?? requestedSelected ?? source);
                break;
        }
        result.RemoveAll(x => x == null || x.HP <= 0);
        return true;
    }

    public void Dispose()
    {
        if (ReferenceEquals(current.Value, this)) current.Value = previous;
    }
}
