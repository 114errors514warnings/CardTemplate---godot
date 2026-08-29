using System;
using System.Collections.Generic;
using CardSimulator;

internal sealed class MonsterIntentionService
{
    private sealed class MonsterIntentionExecutionContext
    {
        public IUnitInstance SharedRandomDamageTarget { get; set; }
        public IUnitInstance LastResolvedDamageTarget { get; set; }
    }

    private readonly BattleSytem battle;

    public MonsterIntentionService(BattleSytem battle)
    {
        this.battle = battle;
    }

    public void SelectIntentionsForAllMonsters()
    {
        if (battle.Monsters == null || battle.Monsters.Count == 0)
        {
            return;
        }

        foreach (MonsterInstance monster in battle.Monsters.Values)
        {
            SelectIntentionForMonster(monster);
        }
    }

    public void SelectIntentionForMonster(MonsterInstance monster)
    {
        if (monster == null || monster.HP <= 0)
        {
            return;
        }

        if (monster.Table == null || monster.Table.Length == 0)
        {
            monster.ClearSelectedIntention();
            return;
        }

        List<int> availableIndices = new List<int>();
        for (int index = 0; index < monster.Table.Length; index++)
        {
            int[][] intention = monster.Table[index];
            if (intention != null && intention.Length > 0)
            {
                availableIndices.Add(index);
            }
        }

        if (availableIndices.Count == 0)
        {
            monster.ClearSelectedIntention();
            return;
        }

        int randomPosition = BattleSytem.RandomGenerator.Next(availableIndices.Count);
        int selectedIndex = availableIndices[randomPosition];
        monster.SetSelectedIntention(selectedIndex, monster.Table[selectedIndex]);
        UpdateMonsterIntentionPreviewTarget(monster);
    }

    public bool TrySwitchMonsterIntention(int monsterUniqueInGameId, int intentionIndex, out string resultMessage)
    {
        resultMessage = string.Empty;

        if (!battle.IsBattleStarted)
        {
            resultMessage = "当前不在战斗中，无法修改怪物意图。";
            return false;
        }

        if (battle.Monsters == null || battle.Monsters.Count == 0)
        {
            resultMessage = "当前没有已实例化怪物，无法修改意图。";
            return false;
        }

        if (!battle.Monsters.TryGetValue(monsterUniqueInGameId, out MonsterInstance monster) || monster == null)
        {
            resultMessage = $"未找到怪物UniqueInGameID={monsterUniqueInGameId}。";
            return false;
        }

        if (monster.HP <= 0)
        {
            resultMessage = $"怪物UniqueInGameID={monsterUniqueInGameId} 已死亡，无法修改意图。";
            return false;
        }

        if (intentionIndex <= 0)
        {
            resultMessage = $"意图index={intentionIndex} 非法，意图序号从1开始。";
            return false;
        }

        if (monster.Table == null || monster.Table.Length == 0)
        {
            resultMessage = $"怪物 {monster.Name} 未配置任何意图。";
            return false;
        }

        int targetIndex = intentionIndex - 1;
        if (targetIndex >= monster.Table.Length)
        {
            resultMessage = $"怪物 {monster.Name} 的意图index={intentionIndex} 超出范围，当前共 {monster.Table.Length} 种意图。";
            return false;
        }

        int[][] targetIntention = monster.Table[targetIndex];
        if (targetIntention == null || targetIntention.Length == 0)
        {
            resultMessage = $"怪物 {monster.Name} 的第 {intentionIndex} 种意图为空，无法切换。";
            return false;
        }

        monster.SetSelectedIntention(targetIndex, targetIntention);
        UpdateMonsterIntentionPreviewTarget(monster);
        battle.RefreshBattleInfoDisplay();
        resultMessage = $"已将怪物 {monster.Name}#{monster.UniqueInGameId} 切换到第 {intentionIndex} 种意图：{battle.GetMonsterIntentionDisplay(monster)}";
        return true;
    }

    public void ExecuteMonsterIntention(MonsterInstance monster)
    {
        if (monster == null || monster.HP <= 0)
        {
            return;
        }

        if (monster.SelectedIntention == null || monster.SelectedIntention.Length == 0)
        {
            SelectIntentionForMonster(monster);
        }

        if (monster.SelectedIntention == null || monster.SelectedIntention.Length == 0)
        {
            battle.AppendPanelConsoleInfo($"怪物行动（{monster.Name}#{monster.UniqueInGameId}）跳过：未配置可执行意图。");
            return;
        }

        battle.AppendPanelConsoleInfo($"怪物行动（{monster.Name}#{monster.UniqueInGameId}）执行意图：{battle.GetMonsterIntentionDisplay(monster)}");

        MonsterIntentionExecutionContext executionContext = new MonsterIntentionExecutionContext();
        foreach (int[] effectConfig in monster.SelectedIntention)
        {
            if (!TryExecuteMonsterEffect(monster, effectConfig, executionContext, out string resultSummary))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(resultSummary))
            {
                battle.AppendPanelConsoleInfo($"怪物行动（{monster.Name}#{monster.UniqueInGameId}）{resultSummary}");
            }
        }
    }


    private void ShowDamageOnCharacterView(IUnitInstance target, EffectResult result)
    {
        if (target == null || result == null) return;
        int totalDamage = result.HpDamage;
        if (totalDamage <= 0) return;

        CardBattleScene scene = battle.GetTree().CurrentScene as CardBattleScene;
        if (scene == null) return;

        scene.ShowDamageNumberOnUnit(target, totalDamage);
    }

    private bool TryExecuteMonsterEffect(MonsterInstance monster, int[] effectConfig, MonsterIntentionExecutionContext executionContext, out string resultSummary)
    {
        resultSummary = string.Empty;

        if (monster == null || effectConfig == null || effectConfig.Length == 0)
        {
            return false;
        }

        EffectType effectType = (EffectType)effectConfig[0];
        int[] effectArgs = GetMonsterEffectArguments(effectConfig);

        switch (effectType)
        {
            case EffectType.Damage:
                MonsterDamageTargetMode targetMode = ParseMonsterDamageTargetMode(effectArgs, out int[] damageArgs);
                IUnitInstance target = ResolveMonsterTarget(monster, targetMode, executionContext);
                if (target == null)
                {
                    resultSummary = "伤害效果跳过：玩家目标不存在。";
                    return true;
                }

                executionContext.LastResolvedDamageTarget = target;

                battle.BeginOrderedCombatLog();
                try
                {
                    EffectResult attackResult = EffectSystem.ApplyAttack(monster, target, damageArgs);
                    battle.AppendPanelConsoleInfo($"怪物行动（{monster.Name}#{monster.UniqueInGameId}）Damage：{attackResult.BuildSummary()}");
                    ShowDamageOnCharacterView(target, attackResult);
                    battle.FlushDeferredCombatResolution();
                }
                finally
                {
                    battle.EndOrderedCombatLog();
                }

                return true;

            case EffectType.AddState:
                if (!TryApplyAddState(monster, executionContext, effectArgs, out resultSummary))
                {
                    return false;
                }

                return true;

            case EffectType.Shield:
                EffectResult shieldResult = EffectSystem.ApplyShield(monster, effectArgs);
                resultSummary = $"Shield：{shieldResult.BuildSummary()}";
                return true;

            default:
                battle.AppendPanelConsoleError($"错误：怪物意图暂不支持效果类型 {effectType}。当前仅支持 Damage 与 Shield。");
                return false;
        }
    }

    private bool TryApplyAddState(MonsterInstance monster, MonsterIntentionExecutionContext executionContext, int[] effectArgs, out string resultSummary)
    {
        resultSummary = string.Empty;

        EffectTargetType effectTargetType = ParseMonsterEffectTargetType(effectArgs);
        int[] normalizedArgs = GetMonsterEffectArgumentsWithoutTargetType(effectArgs);
        if (normalizedArgs.Length <= 0)
        {
            battle.AppendPanelConsoleError("错误：怪物 AddState 意图缺少 stateType 参数。当前格式为 AddState;目标类型;状态类型;层数。");
            return false;
        }

        StateType stateType = (StateType)normalizedArgs[0];
        if (!Enum.IsDefined(typeof(StateType), stateType) || stateType == StateType.None)
        {
            battle.AppendPanelConsoleError($"错误：怪物 AddState 意图的 stateType={normalizedArgs[0]} 非法。");
            return false;
        }

        int stacks = normalizedArgs.Length > 1 ? normalizedArgs[1] : 1;
        List<IUnitInstance> targets = ResolveMonsterStateTargets(monster, executionContext, effectTargetType);
        if (targets.Count == 0)
        {
            resultSummary = $"AddState 跳过：未解析出有效目标（{stateType}）。";
            return true;
        }

        foreach (IUnitInstance target in targets)
        {
            StateSystem.AddOrUpdateState(target, stateType, stacks);
        }

        resultSummary = $"AddState：{stateType} +{stacks} -> {string.Join("、", targets.ConvertAll(target => BuildUnitShortLabel(target)))}";
        return true;
    }

    private static EffectTargetType ParseMonsterEffectTargetType(int[] effectArgs)
    {
        if (effectArgs == null || effectArgs.Length == 0)
        {
            return EffectTargetType.Self;
        }

        EffectTargetType targetType = (EffectTargetType)effectArgs[0];
        return Enum.IsDefined(typeof(EffectTargetType), targetType) ? targetType : EffectTargetType.Self;
    }

    private static int[] GetMonsterEffectArgumentsWithoutTargetType(int[] effectArgs)
    {
        if (effectArgs == null || effectArgs.Length <= 1)
        {
            return Array.Empty<int>();
        }

        int[] normalizedArgs = new int[effectArgs.Length - 1];
        Array.Copy(effectArgs, 1, normalizedArgs, 0, normalizedArgs.Length);
        return normalizedArgs;
    }

    private List<IUnitInstance> ResolveMonsterStateTargets(MonsterInstance monster, MonsterIntentionExecutionContext executionContext, EffectTargetType targetType)
    {
        List<IUnitInstance> targets = new List<IUnitInstance>();
        switch (targetType)
        {
            case EffectTargetType.Self:
                if (monster != null)
                {
                    targets.Add(monster);
                }
                break;

            case EffectTargetType.SelectedTarget:
                if (executionContext?.LastResolvedDamageTarget != null && executionContext.LastResolvedDamageTarget.HP > 0)
                {
                    targets.Add(executionContext.LastResolvedDamageTarget);
                }
                break;

            case EffectTargetType.AllEnemies:
                targets.AddRange(battle.GetEnemyUnits(monster));
                break;

            case EffectTargetType.AllUnits:
                targets.AddRange(battle.GetAllUnits());
                break;

            default:
                if (monster != null)
                {
                    targets.Add(monster);
                }
                break;
        }

        return targets;
    }

    private string BuildUnitShortLabel(IUnitInstance unit)
    {
        if (unit is CharacterInstance player)
        {
            return $"{player.Name}#{battle.FormatUniqueInGameId(player.UniqueInGameId)}";
        }

        if (unit is MonsterInstance monster)
        {
            return $"{monster.Name}#{battle.FormatUniqueInGameId(monster.UniqueInGameId)}";
        }

        return $"Unit#{battle.FormatUniqueInGameId(unit?.UniqueInGameId ?? 0)}";
    }

    public void UpdateMonsterIntentionPreviewTarget(MonsterInstance monster)
    {
        if (monster == null)
        {
            return;
        }

        monster.SetSelectedIntentionTarget(-1);
        if (monster.SelectedIntention == null || monster.SelectedIntention.Length == 0)
        {
            return;
        }

        foreach (int[] effectConfig in monster.SelectedIntention)
        {
            if (effectConfig == null || effectConfig.Length == 0 || (EffectType)effectConfig[0] != EffectType.Damage)
            {
                continue;
            }

            int[] effectArgs = GetMonsterEffectArguments(effectConfig);
            MonsterDamageTargetMode targetMode = ParseMonsterDamageTargetMode(effectArgs, out _);
            if (targetMode != MonsterDamageTargetMode.RandomSameTargetWithinIntention)
            {
                continue;
            }

            IUnitInstance target = ResolveRandomAlivePlayerTarget();
            monster.SetSelectedIntentionTarget(target?.UniqueInGameId ?? -1);
            return;
        }
    }

    public static int[] GetMonsterEffectArguments(int[] effectConfig)
    {
        if (effectConfig == null || effectConfig.Length <= 1)
        {
            return Array.Empty<int>();
        }

        int[] args = new int[effectConfig.Length - 1];
        Array.Copy(effectConfig, 1, args, 0, args.Length);
        return args;
    }

    public static int GetEffectArgument(int[] effectArgs, int index, int defaultValue = 0)
    {
        return effectArgs != null && index >= 0 && index < effectArgs.Length
            ? effectArgs[index]
            : defaultValue;
    }

    public static MonsterDamageTargetMode ParseMonsterDamageTargetMode(int[] effectArgs, out int[] normalizedEffectArgs)
    {
        normalizedEffectArgs = effectArgs ?? Array.Empty<int>();
        if (effectArgs == null || effectArgs.Length == 0)
        {
            return MonsterDamageTargetMode.RandomPerHit;
        }

        if (effectArgs.Length <= 1)
        {
            return MonsterDamageTargetMode.RandomPerHit;
        }

        int firstArg = effectArgs[0];
        if (Enum.IsDefined(typeof(MonsterDamageTargetMode), firstArg))
        {
            normalizedEffectArgs = CopyEffectArgsWithoutFirst(effectArgs);
            return (MonsterDamageTargetMode)firstArg;
        }

        return MonsterDamageTargetMode.RandomPerHit;
    }

    private static int[] CopyEffectArgsWithoutFirst(int[] effectArgs)
    {
        if (effectArgs == null || effectArgs.Length <= 1)
        {
            return Array.Empty<int>();
        }

        int[] normalizedArgs = new int[effectArgs.Length - 1];
        Array.Copy(effectArgs, 1, normalizedArgs, 0, normalizedArgs.Length);
        return normalizedArgs;
    }

    private IUnitInstance ResolveMonsterTarget(MonsterInstance monster, MonsterDamageTargetMode targetMode, MonsterIntentionExecutionContext executionContext)
    {
        if (targetMode == MonsterDamageTargetMode.RandomSameTargetWithinIntention)
        {
            if (executionContext == null)
            {
                return ResolvePreviewedMonsterTarget(monster);
            }

            if (executionContext.SharedRandomDamageTarget == null)
            {
                executionContext.SharedRandomDamageTarget = ResolvePreviewedMonsterTarget(monster);
            }

            return executionContext.SharedRandomDamageTarget != null && executionContext.SharedRandomDamageTarget.HP > 0
                ? executionContext.SharedRandomDamageTarget
                : null;
        }

        return ResolveRandomAlivePlayerTarget();
    }

    private IUnitInstance ResolvePreviewedMonsterTarget(MonsterInstance monster)
    {
        if (monster != null && monster.SelectedIntentionTargetUniqueInGameId > 0 && battle.TryGetPlayerByUniqueId(monster.SelectedIntentionTargetUniqueInGameId, out CharacterInstance previewedTarget) && previewedTarget.HP > 0)
        {
            return previewedTarget;
        }

        IUnitInstance fallbackTarget = ResolveRandomAlivePlayerTarget();
        if (monster != null)
        {
            monster.SetSelectedIntentionTarget(fallbackTarget?.UniqueInGameId ?? -1);
        }

        return fallbackTarget;
    }

    private IUnitInstance ResolveRandomAlivePlayerTarget()
    {
        List<CharacterInstance> alivePlayers = battle.GetAlivePlayers();
        if (alivePlayers.Count == 0)
        {
            return null;
        }

        // 城墙 (ForcedTaunt) 优先级最高：从拥有 ForcedTaunt 状态且护盾>0 的玩家中随机选。
        List<CharacterInstance> taunters = null;
        for (int i = 0; i < alivePlayers.Count; i++)
        {
            CharacterInstance player = alivePlayers[i];
            if (player == null) continue;
            if (player.Shield <= 0) continue;
            if (!StateSystem.TryGetStateStacks(player, StateType.ForcedTaunt, out int stacks) || stacks <= 0) continue;
            (taunters ??= new List<CharacterInstance>()).Add(player);
        }
        if (taunters != null && taunters.Count > 0)
        {
            return taunters[BattleSytem.RandomGenerator.Next(taunters.Count)];
        }

        return alivePlayers[BattleSytem.RandomGenerator.Next(alivePlayers.Count)];
    }
}