// StateCardPipeline.cs
// 状态牌专用管线：状态牌出牌前解析 AddState 目标 + 状态结束后回收 StatePile 卡牌。
// 不依赖 Godot，可单测（间接通过 Card.Apply + StateSystem 行为）。

using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;

public sealed class StateCardApplication
{
    public IUnitInstance TargetUnit { get; }
    public StateType StateType { get; }
    public int Stacks { get; }

    public StateCardApplication(IUnitInstance targetUnit, StateType stateType, int stacks)
    {
        TargetUnit = targetUnit;
        StateType = stateType;
        Stacks = stacks;
    }
}

public sealed class StateCardPipeline
{
    private readonly BattleSytem battle;
    private readonly IBattleConsole console;
    private readonly IBattleUiRefresher uiRefresher;
    private readonly BattleUnitRegistry unitRegistry;

    public StateCardPipeline(BattleSytem battle, IBattleConsole console, IBattleUiRefresher uiRefresher, BattleUnitRegistry unitRegistry)
    {
        this.battle = battle ?? throw new ArgumentNullException(nameof(battle));
        this.console = console ?? throw new ArgumentNullException(nameof(console));
        this.uiRefresher = uiRefresher ?? throw new ArgumentNullException(nameof(uiRefresher));
        this.unitRegistry = unitRegistry ?? throw new ArgumentNullException(nameof(unitRegistry));
    }

    /// <summary>
    /// 状态牌出牌后注册"状态结束"回调：状态结束时由 StateSystem 触发 BattleSytem 来回收 StatePile 卡牌。
    /// </summary>
    public void RegisterStateCardEndCallbacks(List<StateCardApplication> applications, Card sourceCard, IUnitInstance ownerUnit)
    {
        if (applications == null || applications.Count == 0)
        {
            return;
        }

        foreach (StateCardApplication application in applications)
        {
            if (application?.TargetUnit == null)
            {
                continue;
            }

            StateSystem.RegisterStateEndCallback(application.TargetUnit, application.StateType, application.Stacks, sourceCard?.UniqueInGameId, ownerUnit);
        }
    }

    /// <summary>
    /// 从目标单位的 StatePile 摘出指定 UniqueInGameId 的状态牌并返回。找不到返回 null。
    /// </summary>
    public Card FindAndRemoveCardFromStatePile(IUnitInstance targetUnit, string stateCardUniqueInGameId)
    {
        if (targetUnit?.StatePile == null || string.IsNullOrWhiteSpace(stateCardUniqueInGameId))
        {
            return null;
        }

        for (int index = 0; index < targetUnit.StatePile.Count; index++)
        {
            Card stateCard = targetUnit.StatePile[index];
            if (stateCard == null || !string.Equals(stateCard.UniqueInGameId, stateCardUniqueInGameId, StringComparison.Ordinal))
            {
                continue;
            }

            targetUnit.StatePile.RemoveAt(index);
            return stateCard;
        }

        return null;
    }

    /// <summary>
    /// 状态牌出牌前解析：扫描 card.EffectTypes 找 AddState，解析出 (目标, 状态类型, 层数) 列表。
    /// 要求所有 AddState 必须作用于同一目标（同一张牌不能同时进多个状态牌堆）。
    /// </summary>
    public bool TryResolveStateCardApplications(
        Card card,
        IUnitInstance source,
        IUnitInstance selectedTarget,
        out List<StateCardApplication> applications,
        out string errorMessage)
    {
        applications = new List<StateCardApplication>();
        errorMessage = string.Empty;

        if (card == null)
        {
            errorMessage = "错误：状态牌为空，无法解析状态牌堆目标。";
            return false;
        }

        for (int effectIndex = 0; effectIndex < card.EffectTypes.Length; effectIndex++)
        {
            if (card.EffectTypes[effectIndex] != EffectType.AddState)
            {
                continue;
            }

            int[] rawEffectParams = (card.Params != null && effectIndex < card.Params.Length) ? card.Params[effectIndex] : Array.Empty<int>();
            EffectTargetType effectTargetType = ParseEffectTargetType(rawEffectParams);
            int[] effectArgs = GetEffectArguments(rawEffectParams);
            List<IUnitInstance> resolvedTargets = unitRegistry.ResolveEffectTargets(source, selectedTarget, effectTargetType);

            if (effectArgs.Length <= 0)
            {
                errorMessage = $"错误：状态牌 CardId={card.CardId} 的 AddState 缺少 stateType 参数。";
                return false;
            }

            StateType stateType = (StateType)effectArgs[0];
            if (!Enum.IsDefined(typeof(StateType), stateType) || stateType == StateType.None)
            {
                errorMessage = $"错误：状态牌 CardId={card.CardId} 的 AddState 参数非法，stateType={effectArgs[0]}。";
                return false;
            }

            int stacks = effectArgs.Length > 1 ? effectArgs[1] : 1;
            foreach (IUnitInstance resolvedTarget in resolvedTargets)
            {
                applications.Add(new StateCardApplication(resolvedTarget, stateType, stacks));
            }
        }

        if (applications.Count == 0)
        {
            errorMessage = $"错误：状态牌 CardId={card.CardId} 未配置 AddState，无法放入目标状态牌堆。";
            return false;
        }

        IUnitInstance uniqueTarget = applications[0].TargetUnit;
        if (uniqueTarget == null)
        {
            errorMessage = $"错误：状态牌 CardId={card.CardId} 未解析出有效状态目标。";
            return false;
        }

        if (applications.Any(current => current.TargetUnit == null || current.TargetUnit.UniqueInGameId != uniqueTarget.UniqueInGameId))
        {
            errorMessage = $"错误：状态牌 CardId={card.CardId} 当前不支持同时作用于多个不同单位，因为同一张牌无法同时进入多个状态牌堆。";
            return false;
        }

        return true;
    }

    public static EffectTargetType ParseEffectTargetType(int[] rawEffectParams)
    {
        if (rawEffectParams == null || rawEffectParams.Length == 0)
        {
            return EffectTargetType.Auto;
        }

        EffectTargetType parsed = (EffectTargetType)rawEffectParams[0];
        return Enum.IsDefined(typeof(EffectTargetType), parsed) ? parsed : EffectTargetType.Auto;
    }

    public static int[] GetEffectArguments(int[] rawEffectParams)
    {
        if (rawEffectParams == null || rawEffectParams.Length <= 1)
        {
            return Array.Empty<int>();
        }

        int[] args = new int[rawEffectParams.Length - 1];
        Array.Copy(rawEffectParams, 1, args, 0, args.Length);
        return args;
    }
}
