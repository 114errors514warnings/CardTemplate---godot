// BattleCommandRouter.cs
// 给 Interact/* 按钮命令用的统一入口：TrySetPlayer* / TryAddPlayer* / TryAddStateToUnit / TryRemoveStateFromUnit
// 不依赖 Godot（DrawCards 调用是延迟的，靠 battle.CardPlay.DrawCardsToHand）。

using System;
using CardSimulator;

public sealed class BattleCommandRouter
{
    private readonly BattleSytem battle;
    private readonly IBattleConsole console;
    private readonly IBattleUiRefresher uiRefresher;
    private readonly BattleUnitRegistry unitRegistry;

    public BattleCommandRouter(BattleSytem battle, IBattleConsole console, IBattleUiRefresher uiRefresher, BattleUnitRegistry unitRegistry)
    {
        this.battle = battle ?? throw new ArgumentNullException(nameof(battle));
        this.console = console ?? throw new ArgumentNullException(nameof(console));
        this.uiRefresher = uiRefresher ?? throw new ArgumentNullException(nameof(uiRefresher));
        this.unitRegistry = unitRegistry ?? throw new ArgumentNullException(nameof(unitRegistry));
    }

    // ===================== 抽牌命令 =====================

    public bool TryDrawCardsByCommand(int count, out string resultMessage)
    {
        resultMessage = string.Empty;

        CharacterInstance player = battle.Player;
        if (player == null)
        {
            resultMessage = "玩家角色尚未初始化，无法抽牌。";
            return false;
        }

        if (count <= 0)
        {
            resultMessage = $"抽牌数={count} 非法，需大于0。";
            return false;
        }

        int drawn = battle.DrawCardsToHand(player, count);
        battle.InvokeRefreshBattleInfoDisplay();
        resultMessage = $"抽牌完成：{drawn}/{count}。当前手牌 {player.handcards.Count} 张，抽牌堆 {player.drawpile.Count} 张，弃牌堆 {player.discardpile.Count} 张。";
        return true;
    }

    // ===================== 玩家属性设置 =====================

    public bool TrySetPlayerHealth(int hp, int maxHp, out string resultMessage) => TrySetPlayerHealth(battle.Player?.UniqueInGameId ?? -1, hp, maxHp, out resultMessage);

    public bool TrySetPlayerHealth(int playerUniqueInGameId, int hp, int maxHp, out string resultMessage)
    {
        int oldHp = 0;
        int oldMaxHp = 0;
        return TryApplyPlayerMutation(
            playerUniqueInGameId,
            "玩家角色尚未初始化，无法设置生命。",
            _ =>
            {
                if (maxHp <= 0) return $"最大生命值={maxHp} 非法，必须大于0。";
                if (hp < 0) return $"当前生命值={hp} 非法，不能小于0。";
                return null;
            },
            player =>
            {
                oldHp = player.HP;
                oldMaxHp = player.Max_HP;
                player.Max_HP = maxHp;
                player.HP = hp > maxHp ? maxHp : hp;
            },
            player => $"玩家 {player.Name} 生命已设置：HP {oldHp}->{player.HP}，MaxHP {oldMaxHp}->{player.Max_HP}。",
            out resultMessage);
    }

    public bool TrySetPlayerAttack(int attack, out string resultMessage) => TrySetPlayerAttack(battle.Player?.UniqueInGameId ?? -1, attack, out resultMessage);

    public bool TrySetPlayerAttack(int playerUniqueInGameId, int attack, out string resultMessage)
    {
        int oldAttack = 0;
        return TryApplyPlayerMutation(
            playerUniqueInGameId,
            "玩家角色尚未初始化，无法设置攻击。",
            null,
            player =>
            {
                oldAttack = player.Attack;
                player.Attack = attack;
            },
            player => $"玩家 {player.Name} 攻击已设置：{oldAttack}->{player.Attack}。",
            out resultMessage);
    }

    public bool TrySetPlayerDefend(int defend, out string resultMessage) => TrySetPlayerDefend(battle.Player?.UniqueInGameId ?? -1, defend, out resultMessage);

    public bool TrySetPlayerDefend(int playerUniqueInGameId, int defend, out string resultMessage)
    {
        int oldDefend = 0;
        return TryApplyPlayerMutation(
            playerUniqueInGameId,
            "玩家角色尚未初始化，无法设置防御。",
            null,
            player =>
            {
                oldDefend = player.Defend;
                player.Defend = defend;
            },
            player => $"玩家 {player.Name} 防御已设置：{oldDefend}->{player.Defend}。",
            out resultMessage);
    }

    public bool TrySetPlayerMaxEnergy(int maxEnergy, out string resultMessage) => TrySetPlayerMaxEnergy(battle.Player?.UniqueInGameId ?? -1, maxEnergy, out resultMessage);

    public bool TrySetPlayerMaxEnergy(int playerUniqueInGameId, int maxEnergy, out string resultMessage)
    {
        int oldMaxEnergy = 0;
        int oldEnergy = 0;
        return TryApplyPlayerMutation(
            playerUniqueInGameId,
            "玩家角色尚未初始化，无法设置能量上限。",
            _ => maxEnergy < 1 ? $"能量上限={maxEnergy} 非法，不能小于1。" : null,
            player =>
            {
                oldMaxEnergy = player.Max_costs;
                oldEnergy = player.costs;
                player.Max_costs = maxEnergy;
                if (player.costs > player.Max_costs) player.costs = player.Max_costs;
            },
            player => $"玩家 {player.Name} 能量上限已设置：{oldMaxEnergy}->{player.Max_costs}，当前能量 {oldEnergy}->{player.costs}。",
            out resultMessage);
    }

    public bool TryAddPlayerEnergyRaw(int addEnergy, out string resultMessage) => TryAddPlayerEnergyRaw(battle.Player?.UniqueInGameId ?? -1, addEnergy, out resultMessage);

    public bool TryAddPlayerEnergyRaw(int playerUniqueInGameId, int addEnergy, out string resultMessage)
    {
        int oldEnergy = 0;
        return TryApplyPlayerMutation(
            playerUniqueInGameId,
            "玩家角色尚未初始化，无法增加能量。",
            _ => addEnergy <= 0 ? $"增加能量值={addEnergy} 非法，需大于0。" : null,
            player =>
            {
                oldEnergy = player.costs;
                player.costs += addEnergy;
            },
            player => $"玩家 {player.Name} 增加能量（跳过状态修正）：{oldEnergy}->{player.costs}（+{addEnergy}）。",
            out resultMessage);
    }

    // ===================== 状态操作 =====================

    public bool TryAddStateToUnit(int targetUniqueInGameId, int rawStateType, int stacks, out string resultMessage)
    {
        resultMessage = string.Empty;

        if (stacks <= 0)
        {
            resultMessage = $"层数={stacks} 非法，需大于0。";
            return false;
        }

        if (!Enum.IsDefined(typeof(StateType), rawStateType))
        {
            resultMessage = $"状态ID={rawStateType} 非法，未定义对应 StateType。";
            return false;
        }

        StateType stateType = (StateType)rawStateType;
        if (stateType == StateType.None)
        {
            resultMessage = "状态ID=0 对应 None，不能添加。";
            return false;
        }

        battle.EnsureUnitCachesLoaded();
        if (!LoadingSystem.StateDictionary.ContainsKey(stateType))
        {
            resultMessage = $"状态ID={rawStateType} 未在状态配置中找到。";
            return false;
        }

        if (!unitRegistry.TryGetUnitByUniqueId(targetUniqueInGameId, out IUnitInstance targetUnit))
        {
            resultMessage = $"未找到目标UniqueInGameId={targetUniqueInGameId} 对应的单位。";
            return false;
        }

        StateSystem.AddOrUpdateState(targetUnit, stateType, stacks);
        battle.InvokeRefreshBattleInfoDisplay();

        string targetLabel = unitRegistry.BuildUnitLabel(targetUnit);
        string stateName = LoadingSystem.StateDictionary.TryGetValue(stateType, out StateDefinition definition) && !string.IsNullOrWhiteSpace(definition.Name)
            ? definition.Name
            : stateType.ToString();
        resultMessage = $"已为 {targetLabel} 添加状态 {stateName}（StateId={(int)stateType}）x{stacks}。";
        return true;
    }

    public bool TryRemoveStateFromUnit(int targetUniqueInGameId, int rawStateType, int? stacks, out string resultMessage)
    {
        resultMessage = string.Empty;

        if (!Enum.IsDefined(typeof(StateType), rawStateType))
        {
            resultMessage = $"状态ID={rawStateType} 非法，未定义对应 StateType。";
            return false;
        }

        StateType stateType = (StateType)rawStateType;
        if (stateType == StateType.None)
        {
            resultMessage = "状态ID=0 对应 None，不能删除。";
            return false;
        }

        if (stacks.HasValue && stacks.Value <= 0)
        {
            resultMessage = $"层数={stacks.Value} 非法，需大于0。";
            return false;
        }

        battle.EnsureUnitCachesLoaded();
        if (!LoadingSystem.StateDictionary.ContainsKey(stateType))
        {
            resultMessage = $"状态ID={rawStateType} 未在状态配置中找到。";
            return false;
        }

        if (!unitRegistry.TryGetUnitByUniqueId(targetUniqueInGameId, out IUnitInstance targetUnit))
        {
            resultMessage = $"未找到目标UniqueInGameId={targetUniqueInGameId} 对应的单位。";
            return false;
        }

        if (!StateSystem.TryGetStateStacks(targetUnit, stateType, out int currentStacks) || currentStacks <= 0)
        {
            resultMessage = $"目标 {unitRegistry.BuildUnitLabel(targetUnit)} 当前不存在状态 {(int)stateType}。";
            return false;
        }

        int removedStacks;
        if (!stacks.HasValue)
        {
            removedStacks = currentStacks;
            StateSystem.RemoveState(targetUnit, stateType);
        }
        else
        {
            removedStacks = StateSystem.RemoveStateStacks(targetUnit, stateType, stacks.Value);
        }

        battle.InvokeRefreshBattleInfoDisplay();

        string targetLabel = unitRegistry.BuildUnitLabel(targetUnit);
        string stateName = LoadingSystem.StateDictionary.TryGetValue(stateType, out StateDefinition definition) && !string.IsNullOrWhiteSpace(definition.Name)
            ? definition.Name
            : stateType.ToString();
        int remainingStacks = StateSystem.TryGetStateStacks(targetUnit, stateType, out int leftStacks) ? leftStacks : 0;
        resultMessage = remainingStacks > 0
            ? $"已为 {targetLabel} 删除状态 {stateName}（StateId={(int)stateType}）x{removedStacks}，剩余 {remainingStacks} 层。"
            : $"已为 {targetLabel} 删除状态 {stateName}（StateId={(int)stateType}）全部 {removedStacks} 层。";
        return true;
    }

    // ===================== 私有助手 =====================

    internal bool TryResolvePlayerForCommand(int playerUniqueInGameId, string missingPlayerMessage, out CharacterInstance player, out string resultMessage)
    {
        resultMessage = string.Empty;
        if (!unitRegistry.TryGetPlayerByUniqueId(playerUniqueInGameId, out player))
        {
            resultMessage = missingPlayerMessage;
            return false;
        }
        return true;
    }

    private bool TryApplyPlayerMutation(
        int playerUniqueInGameId,
        string missingPlayerMessage,
        Func<CharacterInstance, string> validate,
        Action<CharacterInstance> apply,
        Func<CharacterInstance, string> buildSuccessMessage,
        out string resultMessage)
    {
        resultMessage = string.Empty;
        if (!TryResolvePlayerForCommand(playerUniqueInGameId, missingPlayerMessage, out CharacterInstance player, out resultMessage))
        {
            return false;
        }

        string validationMessage = validate?.Invoke(player);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            resultMessage = validationMessage;
            return false;
        }

        apply?.Invoke(player);
        battle.InvokeRefreshBattleInfoDisplay();
        resultMessage = buildSuccessMessage?.Invoke(player) ?? string.Empty;
        return true;
    }
}
