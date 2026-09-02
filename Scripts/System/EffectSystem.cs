using System;
using CardSimulator;

public sealed class EffectResult
{
    public string EffectName { get; }
    public IUnitInstance Source { get; }
    public IUnitInstance Target { get; }
    public string SummaryOverride { get; }
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
        string summaryOverride = null,
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
        SummaryOverride = summaryOverride ?? string.Empty;
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
        if (!string.IsNullOrWhiteSpace(SummaryOverride))
        {
            return SummaryOverride;
        }

        if (string.Equals(EffectName, "Attack", StringComparison.OrdinalIgnoreCase))
        {
            return $"来源={BuildUnitLabel(Source)}，目标={BuildUnitLabel(Target)}，总伤害={TotalValue}，护盾抵扣={ShieldAbsorbed}，HP伤害={HpDamage}，目标护盾 {TargetShieldBefore}->{TargetShieldAfter}，目标HP {TargetHpBefore}->{TargetHpAfter}";
        }

        if (string.Equals(EffectName, "ShieldSlam", StringComparison.OrdinalIgnoreCase))
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
    public int[] Params { get; }
    public bool IsCounterAttack { get; }
    public bool SkipOutOfTurnMultiTarget { get; }
    public Card Card { get; }

    public EffectContext(IUnitInstance source, IUnitInstance target = null, int[] effectParams = null, bool isCounterAttack = false, bool skipOutOfTurnMultiTarget = false, Card card = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target;
        Params = effectParams ?? Array.Empty<int>();
        IsCounterAttack = isCounterAttack;
        SkipOutOfTurnMultiTarget = skipOutOfTurnMultiTarget;
        Card = card;
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

        if (ShouldHitAllEnemies(context))
        {
            return ApplyAttackToAllEnemies(context);
        }

        if (context.Target == null)
        {
            throw new ArgumentException("Attack effect requires a target.", nameof(context));
        }

        int damage = Math.Max(0, context.Source.Attack + context.GetParam(0));
        damage = StateSystem.ModifyIncomingDamage(context.Card, context.Source, context.Target, damage);
        int targetShieldBefore = context.Target.Shield;
        int targetHpBefore = context.Target.HP;

        int absorbedByShield = Math.Min(context.Target.Shield, damage);
        context.Target.Shield -= absorbedByShield;

        int hpDamage = damage - absorbedByShield;
        if (hpDamage > 0)
        {
            context.Target.HP = Math.Max(0, context.Target.HP - hpDamage);
        }

        if (hpDamage > 0)
        {
            BattleSytem.RaiseOnDamageApplied(context.Target, hpDamage);
        }

        // 受到伤害后：按 DecayTiming=OnDamaged 处理目标状态（如反击触发、燃血+1 攻击等）
        if (hpDamage > 0 && context.Target != null)
        {
            StateDecayProcessor.ProcessDecayAtTiming(context.Target, DecayTrigger.OnDamaged);
        }

        TryTriggerCounterAttack(context);

        if (!context.IsCounterAttack && context.Source is MonsterInstance && context.Target is CharacterInstance)
        {
            StateSystem.OnMonsterAttackPlayer(context.Source, context.Target);
        }

        EffectResult attackResult = new EffectResult(
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

        BattleSytem.Current?.EnqueueDeferredCombatInfo(
            $"攻击：来源={context.Source?.UniqueInGameId ?? 0} 攻击 目标={context.Target?.UniqueInGameId ?? 0}，造成 {damage} 伤害（护盾抵扣 {absorbedByShield}，HP 伤害 {hpDamage}），目标 HP {targetHpBefore}->{context.Target.HP}");

        return attackResult;
    }

    private static bool ShouldHitAllEnemies(EffectContext context)
    {
        if (context == null || context.SkipOutOfTurnMultiTarget)
        {
            return false;
        }

        if (context.Source == null || !IsOutOfTurn(context.Source))
        {
            return false;
        }

        return StateSystem.TryGetStateStacks(context.Source, StateType.WhirlwindSlash, out int stacks) && stacks > 0;
    }

    private static EffectResult ApplyAttackToAllEnemies(EffectContext context)
    {
        var battle = BattleSytem.Current;
        var enemies = battle?.GetEnemyUnits(context.Source) ?? new System.Collections.Generic.List<IUnitInstance>();
        if (enemies.Count == 0)
        {
            return new EffectResult("Attack", context.Source, context.Target, summaryOverride: $"来源={BuildUnitLabel(context.Source)}，目标=全体敌人，未命中任何有效目标。");
        }

        int totalDamage = 0;
        int totalShieldAbsorbed = 0;
        int totalHpDamage = 0;
        foreach (IUnitInstance enemy in enemies)
        {
            EffectResult singleResult = EffectSystem.ApplyAttack(context.Source, enemy, context.Params, context.IsCounterAttack, true);
            totalDamage += singleResult.TotalValue;
            totalShieldAbsorbed += singleResult.ShieldAbsorbed;
            totalHpDamage += singleResult.HpDamage;
        }

        return new EffectResult(
            "Attack",
            context.Source,
            null,
            summaryOverride: $"来源={BuildUnitLabel(context.Source)}，目标=全体敌人，共{enemies.Count}个，总伤害={totalDamage}，护盾抵扣={totalShieldAbsorbed}，HP伤害={totalHpDamage}",
            totalValue: totalDamage,
            shieldAbsorbed: totalShieldAbsorbed,
            hpDamage: totalHpDamage);
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
        BattleSytem.Current?.EnqueueDeferredCombatInfo($"反击触发：{counterAttackResult.BuildSummary()}");

        // 蓄势待发 (RetainAllBattleCards) 替代反击：自动免费打出施法者手牌中的第一张战斗牌。
        TryTriggerRetainAllBattleCardsCounter(context);
    }

    private static void TryTriggerRetainAllBattleCardsCounter(EffectContext context)
    {
        if (context == null || context.IsCounterAttack) return;
        if (context.Target is not CharacterInstance counterAttacker) return;
        if (!StateSystem.TryGetStateStacks(counterAttacker, StateType.RetainAllBattleCards, out int retainStacks) || retainStacks <= 0) return;

        BattleSytem battle = BattleSytem.Current;
        if (battle == null) return;

        Card firstBattleCard = null;
        for (int i = 0; i < counterAttacker.handcards.Count; i++)
        {
            Card c = counterAttacker.handcards[i];
            if (c != null && c.Category == CardCategory.Attack) { firstBattleCard = c; break; }
        }
        if (firstBattleCard == null)
        {
            BattleSytem.Current?.EnqueueDeferredCombatInfo($"蓄势待发：{counterAttacker.Name} 手牌中无战斗牌可替代打出。");
            return;
        }

        // 临时设 free override：蓄势待发反击免费出第一张战斗牌（出牌后还原）。
        System.Func<IUnitInstance, int> prevOverride = firstBattleCard.EnergyCostOverride;
        firstBattleCard.EnergyCostOverride = _ => 0;
        try
        {
            battle.PlayHandCard(counterAttacker, firstBattleCard, context.Source);
            BattleSytem.Current?.EnqueueDeferredCombatInfo($"蓄势待发：{counterAttacker.Name} 反击替代为免费打出 {firstBattleCard.CardName}");
        }
        finally
        {
            firstBattleCard.EnergyCostOverride = prevOverride;
        }
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
        // 持盾防守：拥有 ShieldGuard 状态时，Defend 属性 +3，本回合后续每个防御效果多 3 格挡。
        if (context.Source != null
            && StateSystem.TryGetStateStacks(context.Source, StateType.ShieldGuard, out int _))
        {
            shieldGain += 3;
        }
        shieldGain = Math.Max(0, shieldGain);
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

/// <summary>
/// 不叠加目标防御力的护盾分配效果（用于大地"把累积护盾复制给友军"等场景）。
/// 公式 = target.Shield += extraShield（不叠加 target.Defend）。
/// </summary>
public sealed class DistributeShieldEffect : IEffect
{
    public string Name => "DistributeShield";

    public EffectResult Apply(EffectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IUnitInstance target = context.Target ?? context.Source;
        if (target == null)
        {
            return new EffectResult(Name, context.Source, context.Target);
        }

        int shieldGain = Math.Max(0, context.GetParam(0));
        int targetShieldBefore = target.Shield;
        if (shieldGain == 0)
        {
            return new EffectResult(Name, context.Source, target, sourceShieldBefore: targetShieldBefore, sourceShieldAfter: target.Shield);
        }

        target.Shield += shieldGain;
        return new EffectResult(
            Name,
            context.Source,
            target,
            totalValue: shieldGain,
            shieldGained: shieldGain,
            sourceShieldBefore: targetShieldBefore,
            sourceShieldAfter: target.Shield);
    }
}

/// <summary>
/// 纯扣血效果（用于燃血等"按固定值扣血"场景）。
/// 公式 = target.HP -= extraHp（**不**叠加 source.Attack，与 ApplyAttack 公式不同）。
/// </summary>
public sealed class HpLossEffect : IEffect
{
    public string Name => "HpLoss";

    public EffectResult Apply(EffectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IUnitInstance target = context.Target ?? context.Source;
        if (target == null)
        {
            return new EffectResult(Name, context.Source, context.Target);
        }

        int hpLoss = Math.Max(0, context.GetParam(0));
        if (hpLoss == 0)
        {
            return new EffectResult(Name, context.Source, target, targetHpBefore: target.HP, targetHpAfter: target.HP);
        }

        int targetHpBefore = target.HP;
        target.HP = Math.Max(0, target.HP - hpLoss);
        return new EffectResult(
            Name,
            context.Source,
            target,
            totalValue: hpLoss,
            hpDamage: hpLoss,
            targetHpBefore: targetHpBefore,
            targetHpAfter: target.HP);
    }
}

public sealed class AddCostEffect : IEffect
{
    public string Name => "AddCost";

    public EffectResult Apply(EffectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        int baseEnergy = context.GetParam(0, 0);
        if (baseEnergy <= 0)
        {
            return new EffectResult(Name, context.Source, context.Target, summaryOverride: $"来源={BuildUnitLabel(context.Source)}，增加能量=0");
        }

        int modifiedEnergy = StateSystem.ModifyAddedEnergy(context.Source, baseEnergy);
        int sourceCostBefore = context.Source.Energy;
        context.Source.Energy += modifiedEnergy;
        int sourceCostAfter = context.Source.Energy;

        return new EffectResult(
            Name,
            context.Source,
            context.Target,
            summaryOverride: $"来源={BuildUnitLabel(context.Source)}，增加能量={modifiedEnergy}（基础={baseEnergy}），能量 {sourceCostBefore}->{sourceCostAfter}",
            totalValue: modifiedEnergy);
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

public sealed class ShieldSlamEffect : IEffect
{
    public string Name => "ShieldSlam";

    public EffectResult Apply(EffectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Target == null)
        {
            throw new ArgumentException("ShieldSlam effect requires a target.", nameof(context));
        }

        int sourceValue = context.Source?.Shield ?? 0;
        int extraDamage = context.GetParam(0);
        return ApplySourceValueDamage(context.Source, context.Target, sourceValue, extraDamage, Name);
    }

    public static EffectResult ApplySourceValueDamage(IUnitInstance source, IUnitInstance target, int sourceValue, int extraDamage = 0, string effectName = "VariableDamage")
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        int damage = Math.Max(0, source.Attack + sourceValue + extraDamage);
        damage = StateSystem.ModifyIncomingDamage(null, source, target, damage);

        int targetShieldBefore = target.Shield;
        int targetHpBefore = target.HP;

        int absorbedByShield = Math.Min(target.Shield, damage);
        target.Shield -= absorbedByShield;

        int hpDamage = damage - absorbedByShield;
        if (hpDamage > 0)
        {
            target.HP = Math.Max(0, target.HP - hpDamage);
        }

        return new EffectResult(
            effectName,
            source,
            target,
            totalValue: damage,
            shieldAbsorbed: absorbedByShield,
            hpDamage: hpDamage,
            targetShieldBefore: targetShieldBefore,
            targetShieldAfter: target.Shield,
            targetHpBefore: targetHpBefore,
            targetHpAfter: target.HP);
    }
}

public static class EffectSystem
{
    public static readonly IEffect Attack = new AttackEffect();
    public static readonly IEffect Shield = new ShieldEffect();
    public static readonly IEffect DistributeShield = new DistributeShieldEffect();
    public static readonly IEffect HpLoss = new HpLossEffect();
    public static readonly IEffect AddCost = new AddCostEffect();
    public static readonly IEffect ShieldSlam = new ShieldSlamEffect();

    public static EffectResult Apply(IEffect effect, EffectContext context)
    {
        if (effect == null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        return effect.Apply(context);
    }

    public static EffectResult ApplyAttack(IUnitInstance source, IUnitInstance target, int[] effectParams = null, bool isCounterAttack = false, bool skipOutOfTurnMultiTarget = false, Card card = null)
    {
        return Apply(Attack, new EffectContext(source, target, effectParams, isCounterAttack, skipOutOfTurnMultiTarget, card));
    }

    public static EffectResult ApplyShield(IUnitInstance source, int[] effectParams = null)
    {
        return Apply(Shield, new EffectContext(source, effectParams: effectParams));
    }

    public static EffectResult ApplyDistributeShield(IUnitInstance target, int extraShield)
    {
        return Apply(DistributeShield, new EffectContext(target, target, new int[] { extraShield }));
    }

    public static EffectResult ApplyHpLoss(IUnitInstance target, int hpLoss)
    {
        return Apply(HpLoss, new EffectContext(target, target, new int[] { hpLoss }));
    }

    public static EffectResult ApplyAddCost(IUnitInstance source, int[] effectParams = null)
    {
        return Apply(AddCost, new EffectContext(source, effectParams: effectParams));
    }

    public static EffectResult ApplyShieldSlam(IUnitInstance source, IUnitInstance target, int[] effectParams = null)
    {
        return Apply(ShieldSlam, new EffectContext(source, target, effectParams));
    }

    public static EffectResult ApplyExhaustCards(IUnitInstance source, params string[] cardUniqueInGameIds)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        BattleSytem battle = BattleSytem.Current;
        if (battle == null)
        {
            return new EffectResult("ExhaustCards", source, null, summaryOverride: $"来源={source.UniqueInGameId}，当前不存在 BattleSytem，无法执行消耗效果。", totalValue: 0);
        }

        return battle.ApplyExhaustCards(source, cardUniqueInGameIds);
    }
}