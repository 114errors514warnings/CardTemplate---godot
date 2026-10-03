using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;
using CardSimulator.Battlefield;

/// <summary>
/// `--battlefield-smoke` 2026-10-04 第二批断言（施工清单 T3–T9）：区域物 / 格点效果表（T3 + T8）、
/// 燃烧回合结算（T7）、`AllAllies` 口径（T4）、空间集合敌我过滤（T5）、消耗选定手牌（T6）、
/// 掉落候选等级过滤（T9）。与 <c>BattlefieldSceneSmoke</c> 同属一个烟测入口（partial）。
/// </summary>
public static partial class BattlefieldSceneSmoke
{
    /// <summary>自建一场（3 名角色 + 1 只怪）并铺好武器，供本批断言共用；牌堆按参数给。</summary>
    private static BattlefieldSession BuildCardBatchBattle(out List<RunCharacterSlotSave> slots, List<List<RunDeckEntry>> decks)
    {
        string path = BattleLevelCatalog.ResolveMapPath("M-F1-001");
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var definition = BattleMapDefinition.Parse(file.GetAsText());
        definition.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
        definition.MonsterIds = new List<int> { 3101 };
        var battle = new BattlefieldSession(definition);
        slots = new List<RunCharacterSlotSave>
        {
            new() { CharacterId = 1002, CurrentHp = 30, MaxHp = 30, EquippedWeaponDefinitionId = "长刀" },
            new() { CharacterId = 1003, CurrentHp = 30, MaxHp = 30, EquippedWeaponDefinitionId = "弓箭" },
            new() { CharacterId = 1004, CurrentHp = 30, MaxHp = 30, EquippedWeaponDefinitionId = "法典" },
        };
        battle.RestoreRunState(slots, decks ?? new List<List<RunDeckEntry>> { new(), new(), new() });
        return battle;
    }

    private static List<List<RunDeckEntry>> EmptyDecks() => new() { new(), new(), new() };

    /// <summary>找一个可站人、无单位 / 无物件的相邻空格。</summary>
    private static AxialHex FindFreeNeighbor(BattlefieldSession battle, AxialHex from) =>
        BattleRangeResolver.Neighbors(from).First(cell => battle.Board.IsWalkable(cell)
            && battle.Occupancy.At(cell) == null && battle.Board.Cells[cell].Trigger == null
            && battle.Board.Cells[cell].Items.Count == 0);

    /// <summary>T3（P2-30 后半）：区域物进入触发改表驱动 —— 藤蔓哨卫 2 点伤害 + 1 层虚弱、每次进入都触发；
    /// 寒霜之地进入只给 1 层虚弱；未登记 TrapId 仍按旧 3 点伤害兜底（`test_trap` 现在也在表里，等价于旧行为）。</summary>
    private static void VerifyAreaObjectTriggers()
    {
        Dictionary<string, AreaObjectSpec> specs = BattlefieldAreaObjectRepository.LoadAll();
        Check(BattlefieldAreaObjectRepository.ForId("vine_sentinel") is { TriggersOnEnter: true, SideFilter: CellEffectSideFilter.Triggerer }
            && BattlefieldAreaObjectRepository.ForId("frost_field") is { TriggersOnEnter: true }
            && BattlefieldAreaObjectRepository.ForId("test_trap") is { TriggersOnEnter: true },
            $"区域物表已进内存字典（{specs.Count} 行：{string.Join("/", specs.Keys)}）");

        var battle = BuildCardBatchBattle(out _, EmptyDecks());
        BattleUnitPlacement enemy = battle.Occupancy.Placements.Values
            .First(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active);
        AxialHex vineCell = FindFreeNeighbor(battle, enemy.Coord);
        Check(battle.DebugPlaceTrap(vineCell.Q, vineCell.R, "vine_sentinel", out string placeError), "布置 vine_sentinel：" + placeError);

        int before = enemy.Unit.HP + enemy.Unit.Shield;
        Check(battle.Movement.TryMoveWithoutPlayerCost(enemy.UnitId, vineCell, out string moveError), "敌人踏入藤蔓哨卫：" + moveError);
        Check(enemy.Unit.HP + enemy.Unit.Shield == before - 2,
            $"敌人踩藤蔓哨卫受到 2 点伤害（{before} → {enemy.Unit.HP + enemy.Unit.Shield}）");
        Check(StateSystem.TryGetStateStacks(enemy.Unit, StateType.Weak, out int weakAfterFirst) && weakAfterFirst >= 1,
            "敌人踩藤蔓哨卫被施加 1 层虚弱");

        AxialHex stepCell = FindFreeNeighbor(battle, vineCell);
        battle.Movement.TryMoveWithoutPlayerCost(enemy.UnitId, stepCell, out _);
        int beforeSecond = enemy.Unit.HP + enemy.Unit.Shield;
        battle.Movement.TryMoveWithoutPlayerCost(enemy.UnitId, vineCell, out _);
        Check(enemy.Unit.HP + enemy.Unit.Shield == beforeSecond - 2, "藤蔓哨卫每次进入都触发（EveryEntry 再踩再减 2）");

        battle.Movement.TryMoveWithoutPlayerCost(enemy.UnitId, stepCell, out _);
        AxialHex frostCell = FindFreeNeighbor(battle, stepCell);
        Check(battle.DebugPlaceTrap(frostCell.Q, frostCell.R, "frost_field", out string frostError), "布置 frost_field：" + frostError);
        int hpBeforeFrost = enemy.Unit.HP;
        int shieldBeforeFrost = enemy.Unit.Shield;
        StateSystem.TryGetStateStacks(enemy.Unit, StateType.Weak, out int weakBeforeFrost);
        Check(battle.Movement.TryMoveWithoutPlayerCost(enemy.UnitId, frostCell, out _), "敌人踏入寒霜之地");
        Check(enemy.Unit.HP == hpBeforeFrost && enemy.Unit.Shield == shieldBeforeFrost, "寒霜之地进入不造成伤害");
        Check(StateSystem.TryGetStateStacks(enemy.Unit, StateType.Weak, out int weakAfterFrost) && weakAfterFrost == weakBeforeFrost + 1,
            $"寒霜之地进入再给 1 层虚弱（{weakBeforeFrost} → {weakAfterFrost}）");

        battle.Movement.TryMoveWithoutPlayerCost(enemy.UnitId, stepCell, out _);
        AxialHex testCell = FindFreeNeighbor(battle, stepCell);
        Check(battle.DebugPlaceTrap(testCell.Q, testCell.R, "test_trap", out _), "布置 test_trap");
        int beforeTest = enemy.Unit.HP + enemy.Unit.Shield;
        battle.Movement.TryMoveWithoutPlayerCost(enemy.UnitId, testCell, out _);
        Check(enemy.Unit.HP + enemy.Unit.Shield == beforeTest - 3, "test_trap 仍是 3 点伤害（与改动前一致）");

        GD.Print("BATTLEFIELD_AREA_OBJECT_PASS: 区域物进入触发按 AreaObject.csv 结算（藤蔓哨卫 2 伤 + 1 虚弱、EveryEntry 可重复；寒霜之地只给 1 虚弱；test_trap 保持 3 伤）");
    }

    /// <summary>T8（D7）：格点效果通道 —— 格上施加 **buff 与 debuff** 同一条路；进入触发一次、回合末再结算一次
    /// （一回合只跑一遍），`SideFilter` 决定敌我。</summary>
    private static void VerifyTerrainStates()
    {
        var battle = BuildCardBatchBattle(out _, EmptyDecks());
        BattleUnitPlacement ally = battle.Occupancy.Placements[battle.PlayerIds[1]];
        AxialHex allyCell = FindFreeNeighbor(battle, ally.Coord);
        Check(battle.Board.TryAddTerrainState(allyCell, new BattleTerrainState("terrain-smoke", "blessed_ground", ally.UnitId, 1, 0), out string stateError),
            "向格点写入地形状态：" + stateError);
        Check(battle.Movement.TryMoveWithoutPlayerCost(ally.UnitId, allyCell, out _), "友军进入庇护之地");
        Check(StateSystem.TryGetStateStacks(ally.Unit, StateType.AddAttack, out int afterEnter) && afterEnter == 1,
            $"友军踩增益格获得 1 层增加攻击力（实际 {afterEnter}）—— 证明格点通道不是 debuff 专用");

        battle.EndCurrentTurn();
        Check(StateSystem.TryGetStateStacks(ally.Unit, StateType.AddAttack, out int afterTurnEnd) && afterTurnEnd == afterEnter + 1,
            $"回合末地形状态独立结算一次（{afterEnter} → {afterTurnEnd}，含首次进入共 2 层）");
        Check(battle.Board.Cells[allyCell].TerrainStates.Any(x => x.InstanceId == "terrain-smoke"),
            "DecayTiming=Never 的地形状态在回合末保留");

        BattleUnitPlacement enemy = battle.Occupancy.Placements.Values
            .First(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active);
        Check(!StateSystem.TryGetStateStacks(enemy.Unit, StateType.AddAttack, out _), "SideFilter=Ally：敌人不因同款地形获得增益");

        GD.Print("BATTLEFIELD_TERRAIN_STATE_PASS: 格点地形状态可施加 buff（庇护之地进入 +1 攻击、回合末再结算一次）；SideFilter=Ally 不作用于敌人");
    }

    /// <summary>T7（D4）：`Ignite` 回合结算 DoT —— 单位回合开始每层 1 点（先吃护盾），结算后层数 −1。</summary>
    private static void VerifyIgniteTurnTick()
    {
        var battle = BuildCardBatchBattle(out _, EmptyDecks());
        BattleUnitPlacement enemy = battle.Occupancy.Placements.Values
            .First(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active);
        enemy.Unit.Shield = 0;
        Check(battle.DebugAddState(enemy.UnitId, (int)StateType.Ignite, 1, out string igniteError), "给怪物贴 1 层燃烧：" + igniteError);
        int hpBefore = enemy.Unit.HP;

        battle.EndCurrentTurn();
        while (battle.Phase == BattlefieldSession.BattlePhase.Monsters && battle.ExecuteNextMonsterTurnStep()) { }
        Check(enemy.Unit.HP == hpBefore - 1, $"燃烧在怪物回合开始结算 1 点（HP {hpBefore} → {enemy.Unit.HP}）");
        Check(!StateSystem.TryGetStateStacks(enemy.Unit, StateType.Ignite, out _), "燃烧结算后层数 −1 归零移除");

        GD.Print("BATTLEFIELD_IGNITE_TICK_PASS: 燃烧在单位回合开始结算（每层 1 点、先吃护盾），结算后 −1 层");
    }

    /// <summary>T4（D5）：`AllAllies` = 全场同阵营（含自身）—— 法术共鸣自身只 +1 层，且不在相邻格的友军也吃到。</summary>
    private static void VerifyAllAlliesScope()
    {
        const int resonanceCardId = 21004002;   // 法师 法术共鸣：AddState + 5;6;1
        var decks = new List<List<RunDeckEntry>>
        {
            new() { new() { CardId = resonanceCardId } },
            new() { new() { CardId = resonanceCardId } },
            new() { new() { CardId = resonanceCardId } },
        };
        var battle = BuildCardBatchBattle(out _, decks);
        battle.Select(battle.PlayerIds[2]);   // 法师
        BattleUnitPlacement caster = battle.Selected;
        BattleUnitPlacement farAlly = battle.PlayerIds.Select(id => battle.Occupancy.Placements[id])
            .Where(x => x.UnitId != caster.UnitId)
            .OrderByDescending(x => AxialHex.Distance(caster.Coord, x.Coord)).First();

        Check(battle.TryCastCard(resonanceCardId, caster.Coord, out string castError), "法术共鸣出牌：" + castError);
        Check(StateSystem.TryGetStateStacks(caster.Unit, StateType.AddAttack, out int selfStacks) && selfStacks == 1,
            $"AllAllies 含自身但不再重复：施法者只 +1 层（实际 {selfStacks}，旧实现叠到 2）");
        Check(StateSystem.TryGetStateStacks(farAlly.Unit, StateType.AddAttack, out int allyStacks) && allyStacks == 1,
            $"全场覆盖：并非相邻（距离 {AxialHex.Distance(caster.Coord, farAlly.Coord)}）的友军也 +1 层（实际 {allyStacks}）");

        GD.Print("BATTLEFIELD_ALL_ALLIES_PASS: AllAllies = 全场同阵营（含自身）—— 法术共鸣自身 1 层、全场友军各 1 层");
    }

    /// <summary>T5（D6）：空间受影响集合只留敌方 —— 直线 / 半径里的友军不再被误伤，敌人照常命中。</summary>
    private static void VerifySpatialFactionFilter()
    {
        const int arrowCardId = 11003001;   // 精灵 穿林箭：Line + Pierce
        var decks = new List<List<RunDeckEntry>> { new(), new() { new() { CardId = arrowCardId } }, new() };
        var battle = BuildCardBatchBattle(out _, decks);
        battle.Select(battle.PlayerIds[1]);   // 精灵 + 弓箭

        AxialHex origin = battle.Selected.Coord;
        AxialHex near = default, far = default;
        bool foundLane = false;
        foreach (AxialHex direction in BattleRangeResolver.SixNeighborOffsets)
        {
            AxialHex first = new(origin.Q + direction.Q, origin.R + direction.R);
            AxialHex second = new(origin.Q + direction.Q * 2, origin.R + direction.R * 2);
            if (battle.Board.IsWalkable(first) && battle.Board.IsWalkable(second)
                && battle.Occupancy.At(first) == null && battle.Occupancy.At(second) == null)
            {
                near = first; far = second; foundLane = true; break;
            }
        }

        Check(foundLane, "空间敌我过滤需要一段两格空直线通道");
        BattleUnitPlacement enemy = battle.Occupancy.Placements.Values
            .First(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active);
        BattleUnitPlacement ally = battle.Occupancy.Placements[battle.PlayerIds[0]];
        battle.Occupancy.CommitMove(enemy, near);
        battle.Occupancy.CommitMove(ally, far);
        int enemyBefore = enemy.Unit.HP + enemy.Unit.Shield;
        int allyBefore = ally.Unit.HP + ally.Unit.Shield;

        Check(battle.TryCastCard(arrowCardId, near, out string castError), "穿林箭出牌：" + castError);
        Check(enemy.Unit.HP + enemy.Unit.Shield < enemyBefore, "直线上的敌人仍被命中");
        Check(ally.Unit.HP + ally.Unit.Shield == allyBefore, "直线上的友军不再被误伤（空间集合只留敌方）");

        GD.Print("BATTLEFIELD_SPATIAL_FACTION_PASS: 空间受影响集合按阵营过滤 —— 直线 / 半径内的友军不掉血，敌人照常命中");
    }

    /// <summary>T6（D3）：新增「消耗选定手牌」效果 —— 林间抚慰打出后进入选牌，被选中的牌进消耗堆，手牌净 −2。</summary>
    private static void VerifyHandCostConsume()
    {
        const int sootheCardId = 21003002;   // 精灵 林间抚慰：ConsumeSelectedHandCard|Heal|ClearFirstNormalDebuff
        var decks = new List<List<RunDeckEntry>>
        {
            new(),
            new()
            {
                new() { CardId = sootheCardId }, new() { CardId = sootheCardId },
                new() { CardId = sootheCardId }, new() { CardId = sootheCardId },
            },
            new(),
        };
        var battle = BuildCardBatchBattle(out _, decks);
        battle.Select(battle.PlayerIds[1]);   // 精灵

        int handBefore = battle.GetHand(battle.SelectedId).Count;
        Check(handBefore >= 2, $"消耗成本需要至少 2 张手牌（实际 {handBefore}）");
        Card played = battle.GetHand(battle.SelectedId).First(x => x.CardId == sootheCardId);
        Check(battle.TryCastCard(sootheCardId, battle.Selected.Coord, out string castError), "林间抚慰出牌：" + castError);
        Check(battle.HasPendingHandChoice, "林间抚慰打出后进入选牌（消耗成本）");

        Card victim = battle.GetHand(battle.SelectedId).First(x => x != played);
        int exhaustBefore = battle.Selected.Unit.ExhaustPile.Count;
        Check(battle.TryChooseHandCard(victim, out string chooseMessage), "选择被消耗的手牌：" + chooseMessage);
        Check(!battle.HasPendingHandChoice, "选牌完成，回合不被卡住");
        Check(battle.Selected.Unit.ExhaustPile.Count == exhaustBefore + 1, "被选中的牌进入消耗堆");
        Check(battle.GetHand(battle.SelectedId).Count == handBefore - 2,
            $"手牌净 −2（打出 1 + 消耗 1：{handBefore} → {battle.GetHand(battle.SelectedId).Count}）");

        GD.Print("BATTLEFIELD_HAND_COST_PASS: 「消耗选定手牌」成本生效（选牌 → 进消耗堆 → 手牌净 −2）");
    }

    /// <summary>T9（D8）：结算候选只含该角色 B–S 级专属牌 —— 等级列就位的三张表零可见变化；
    /// 勇士表缺等级数据源（拍板项），按「不过滤 + 告警」处理。</summary>
    private static void VerifyRewardTierFilter()
    {
        Card[] elfCards = LoadCardCsv.LoadCardsFromCSV("res://DataBase/Card/精灵Card.csv");
        Card[] mageCards = LoadCardCsv.LoadCardsFromCSV("res://DataBase/Card/法师Card.csv");
        Card[] swordCards = LoadCardCsv.LoadCardsFromCSV("res://DataBase/Card/重剑手Card.csv");
        Card[] warriorCards = LoadCardCsv.LoadCardsFromCSV("res://DataBase/Card/勇士Card.csv");

        bool tiersReady = elfCards.All(c => c.Tier != CardTier.None) && mageCards.All(c => c.Tier != CardTier.None)
            && swordCards.All(c => c.Tier != CardTier.None);
        Check(tiersReady && elfCards.All(c => CardRewardOwnership.IsTierEligibleForReward(c.Tier)),
            $"精灵 / 法师 / 重剑手表落了 CardTier 且全部 ∈ B–S（{elfCards.Length} / {mageCards.Length} / {swordCards.Length} 张）");
        Check(warriorCards.All(c => c.Tier == CardTier.None),
            $"勇士表没有等级数据源（{warriorCards.Length} 张全为 None → 按「不过滤」处理，待设计侧补）");
        Check(!CardRewardOwnership.IsTierEligibleForReward(CardTier.C) && !CardRewardOwnership.IsTierEligibleForReward(CardTier.D)
            && CardRewardOwnership.IsTierEligibleForReward(CardTier.B) && CardRewardOwnership.IsTierEligibleForReward(CardTier.A)
            && CardRewardOwnership.IsTierEligibleForReward(CardTier.S),
            "等级判据：C / D 拦截，B / A / S 放行，None 不拦截");

        List<int> elfRewards = LoadingSystem.GetCharacterRewardCardIds(1003);
        Check(elfRewards.Count == elfCards.Length && elfCards.All(c => elfRewards.Contains(c.CardId)),
            $"精灵掉落候选 = 表内全部 {elfCards.Length} 张（本来就只有 B–S → 零可见变化）");
        Check(LoadingSystem.GetCharacterRewardCardIds(1001).Count == warriorCards.Length,
            "勇士候选张数不变（缺等级源 → 不拦截，已打告警）");

        GD.Print("BATTLEFIELD_REWARD_TIER_PASS: 掉落候选按 CardTier 过滤（B–S 放行、C / D 拦截、None 不拦截并告警）；精灵 / 法师 / 重剑手表等级就位，勇士表待补等级源");
    }
}
