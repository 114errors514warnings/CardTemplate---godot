using System;
using CardSimulator;

public sealed class EffectResult
{
    public string EffectName { get; }
    public IUnitInstance Source { get; }
    public IUnitInstance Target { get; }
    public int TotalValue { get; }
    public int ShieldAbsorbed { get; }
    public int HpDamage { get; }
    public int ShieldGained { get; }
    public int SourceShieldBefore { get; }
    public int SourceShieldAfter { get; }
    public int TargetShieldBefore { get; }
    public int TargetShieldAfter { get; }
    public int TargetHpBefore { get; }
    public int TargetHpAfter { get; }

    public EffectResult(
        string effectName,
        IUnitInstance source,
        IUnitInstance target,
        int totalValue = 0,
        int shieldAbsorbed = 0,
        int hpDamage = 0,
        int shieldGained = 0,
        int sourceShieldBefore = 0,
        int sourceShieldAfter = 0,
        int targetShieldBefore = 0,
        int targetShieldAfter = 0,
        int targetHpBefore = 0,
        int targetHpAfter = 0)
    {
        EffectName = effectName;
        Source = source;
        Target = target;
        TotalValue = totalValue;
        ShieldAbsorbed = shieldAbsorbed;
        HpDamage = hpDamage;
        ShieldGained = shieldGained;
        SourceShieldBefore = sourceShieldBefore;
        SourceShieldAfter = sourceShieldAfter;
        TargetShieldBefore = targetShieldBefore;
        TargetShieldAfter = targetShieldAfter;
        TargetHpBefore = targetHpBefore;
        TargetHpAfter = targetHpAfter;
    }

    public string BuildSummary()
    {
        if (string.Equals(EffectName, "Attack", StringComparison.OrdinalIgnoreCase))
        {
            return $"来源={BuildUnitLabel(Source)}，目标={BuildUnitLabel(Target)}，总伤害={TotalValue}，护盾抵扣={ShieldAbsorbed}，HP伤害={HpDamage}，目标护盾 {TargetShieldBefore}->{TargetShieldAfter}，目标HP {TargetHpBefore}->{TargetHpAfter}";
        }

        if (string.Equals(EffectName, "Shield", StringComparison.OrdinalIgnoreCase))
        {
            return $"来源={BuildUnitLabel(Source)}，目标=自身，获得护盾={ShieldGained}，来源护盾 {SourceShieldBefore}->{SourceShieldAfter}";
        }

        return $"来源={BuildUnitLabel(Source)}，目标={BuildUnitLabel(Target)}，效果={EffectName}";
    }

    private static string BuildUnitLabel(IUnitInstance unit)
    {
        if (unit == null)
        {
            return "无";
        }

        Unit typedUnit = unit as Unit;
        string name = typedUnit?.Name ?? unit.GetType().Name;
        return $"{name}(UniqueInGameId={unit.UniqueInGameId})";
    }
}

public sealed class EffectContext
{
    public IUnitInstance Source { get; }
    public IUnitInstance Target { get; }
    // 当前效果的参数数组，params[0]为第一个参数，依此类推
    public int[] Params { get; }
    public bool IsCounterAttack { get; }

    public EffectContext(IUnitInstance source, IUnitInstance target = null, int[] effectParams = null, bool isCounterAttack = false)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target;
        Params = effectParams ?? Array.Empty<int>();
        IsCounterAttack = isCounterAttack;
    }

    public int GetParam(int index, int defaultValue = 0)
    {
        return (Params != null && index < Params.Length) ? Params[index] : defaultValue;
    }
}

public interface IEffect
{
    string Name { get; }
    EffectResult Apply(EffectContext context);
}

public sealed class AttackEffect : IEffect
{
    public string Name => "Attack";

    public EffectResult Apply(EffectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Target == null)
        {
            throw new ArgumentException("Attack effect requires a target.", nameof(context));
        }

        int damage = Math.Max(0, context.Source.Attack + context.GetParam(0));
        damage = StateSystem.ModifyIncomingDamage(context.Source, context.Target, damage);
        int targetShieldBefore = context.Target.Shield;
        int targetHpBefore = context.Target.HP;

        int absorbedByShield = Math.Min(context.Target.Shield, damage);
        context.Target.Shield -= absorbedByShield;

        int hpDamage = damage - absorbedByShield;
        if (hpDamage > 0)
        {
            context.Target.HP = Math.Max(0, context.Target.HP - hpDamage);
        }

        TryTriggerCounterAttack(context);

        return new EffectResult(
            Name,
            context.Source,
            context.Target,
            totalValue: damage,
            shieldAbsorbed: absorbedByShield,
            hpDamage: hpDamage,
            targetShieldBefore: targetShieldBefore,
            targetShieldAfter: context.Target.Shield,
            targetHpBefore: targetHpBefore,
            targetHpAfter: context.Target.HP);
    }

    private static void TryTriggerCounterAttack(EffectContext context)
    {
        if (context == null || context.IsCounterAttack)
        {
            return;
        }

        if (context.Source == null || context.Target == null)
        {
            return;
        }

        if (context.Source.UniqueInGameId == context.Target.UniqueInGameId)
        {
            return;
        }

        if (context.Source.HP <= 0 || context.Target.HP <= 0)
        {
            return;
        }

        if (!StateSystem.TryGetStateStacks(context.Target, StateType.CounterAttack, out int counterStacks) || counterStacks <= 0)
        {
            return;
        }

        if (!IsOutOfTurn(context.Target))
        {
            return;
        }

        EffectResult counterAttackResult = EffectSystem.ApplyAttack(context.Target, context.Source, isCounterAttack: true);
        BattleSytem.Current?.AppendPanelConsoleInfo($"反击触发：{counterAttackResult.BuildSummary()}");
    }

    private static bool IsOutOfTurn(IUnitInstance unit)
    {
        BattleSytem battle = BattleSytem.Current;
        if (battle == null || !battle.IsBattleStarted)
        {
            return false;
        }

        if (unit is CharacterInstance)
        {
            return !battle.IsPlayerTurn;
        }

        if (unit is MonsterInstance)
        {
            return battle.IsPlayerTurn;
        }

        return false;
    }
}

public sealed class ShieldEffect : IEffect
{
    public string Name => "Shield";

    public EffectResult Apply(EffectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        int shieldGain = Math.Max(0, context.Source.Defend + context.GetParam(0));
        int sourceShieldBefore = context.Source.Shield;
        if (shieldGain == 0)
        {
            return new EffectResult(Name, context.Source, context.Target, sourceShieldBefore: sourceShieldBefore, sourceShieldAfter: context.Source.Shield);
        }

        context.Source.Shield += shieldGain;
        return new EffectResult(
            Name,
            context.Source,
            context.Target,
            totalValue: shieldGain,
            shieldGained: shieldGain,
            sourceShieldBefore: sourceShieldBefore,
            sourceShieldAfter: context.Source.Shield);
    }
}

public static class EffectSystem
{
    public static readonly IEffect Attack = new AttackEffect();
    public static readonly IEffect Shield = new ShieldEffect();

    public static EffectResult Apply(IEffect effect, EffectContext context)
    {
        if (effect == null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        return effect.Apply(context);
    }

    public static EffectResult ApplyAttack(IUnitInstance source, IUnitInstance target, int[] effectParams = null, bool isCounterAttack = false)
    {
        return Apply(Attack, new EffectContext(source, target, effectParams, isCounterAttack));
    }

    public static EffectResult ApplyShield(IUnitInstance source, int[] effectParams = null)
    {
        return Apply(Shield, new EffectContext(source, effectParams: effectParams));
    }
}