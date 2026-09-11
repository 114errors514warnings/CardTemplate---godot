// BattlefieldSession.Debug.cs
// 六边形战斗调试指令集合：仅供 HexBattleDebugPanel 调用，不改动正式结算；正式版删除本文件即可。
using System;
using System.Linq;
using CardSimulator;

namespace CardSimulator.Battlefield;

public sealed partial class BattlefieldSession
{
    // ---- 牌堆 / 手牌 ----
    public void DebugDraw(int playerId, int count) => DrawCards(playerId, count);

    public int DebugClearHand(int playerId)
    {
        if (!hands.TryGetValue(playerId, out var hand) || !discardPiles.TryGetValue(playerId, out var discard)) return 0;
        int n = hand.Count;
        if (n > 0) { discard.AddRange(hand); hand.Clear(); Notify(); }
        return n;
    }

    // targetPile: 0=手牌 1=抽牌堆 2=弃牌堆 3=消耗牌堆
    public bool DebugAddCard(int playerId, int cardId, int targetPile, int count, out string error)
    {
        error = "";
        if (count <= 0) { error = "数量需大于 0。"; return false; }
        if (!LoadingSystem.CardDictionary.TryGetValue(cardId, out Card template)) { error = $"卡牌 {cardId} 不存在。"; return false; }
        if (!hands.TryGetValue(playerId, out var hand)) { error = "角色手牌未初始化。"; return false; }
        for (int i = 0; i < count; i++)
        {
            Card card = template.CreateRuntimeInstance();
            switch (targetPile)
            {
                case 0: hand.Add(card); break;
                case 1: drawPiles[playerId].Add(card); break;
                case 2: discardPiles[playerId].Add(card); break;
                case 3: Occupancy.Placements[playerId].Unit.ExhaustPile.Add(card); break;
                default: error = "目标牌堆无效。"; return false;
            }
        }
        Notify();
        return true;
    }

    // ---- 资源（当前角色）----
    public void DebugAddEnergy(int playerId, int amount)
    {
        var unit = Occupancy.Placements[playerId].Unit;
        unit.Energy = Math.Max(0, unit.Energy + amount);
        Notify();
    }

    public void DebugAddMoves(int playerId, int amount)
    {
        var p = Occupancy.Placements[playerId];
        p.MovesUsedThisTurn = Math.Max(0, p.MovesUsedThisTurn - amount);
        Notify();
    }

    public void DebugResetRound() => NextTestRound();
    // ---- 单位效果 ----
    public bool DebugDamage(int unitId, int amount, out string error)
    {
        error = "";
        if (!Occupancy.Placements.TryGetValue(unitId, out var p)) { error = "目标单位不存在。"; return false; }
        p.Unit.HP = Math.Max(0, p.Unit.HP - amount);
        Occupancy.SyncDeaths(); EvaluateOutcome(); Notify();
        return true;
    }

    public bool DebugHeal(int unitId, int amount, out string error)
    {
        error = "";
        if (!TryGetActiveUnit(unitId, out var unit)) { error = "目标单位不存在或已离场。"; return false; }
        unit.HP = Math.Min(unit.Max_HP, unit.HP + amount);
        Notify();
        return true;
    }

    public bool DebugSetHp(int unitId, int hp, out string error)
    {
        error = "";
        if (!Occupancy.Placements.TryGetValue(unitId, out var p)) { error = "目标单位不存在。"; return false; }
        p.Unit.HP = Math.Clamp(hp, 0, p.Unit.Max_HP);
        Occupancy.SyncDeaths(); EvaluateOutcome(); Notify();
        return true;
    }

    public bool DebugAddShield(int unitId, int amount, out string error)
    {
        error = "";
        if (!TryGetActiveUnit(unitId, out var unit)) { error = "目标单位不存在或已离场。"; return false; }
        unit.Shield = Math.Max(0, unit.Shield + amount);
        Notify();
        return true;
    }

    public bool DebugAddState(int unitId, int stateType, int stacks, out string error)
    {
        error = "";
        if (!TryGetActiveUnit(unitId, out var unit)) { error = "目标单位不存在或已离场。"; return false; }
        StateSystem.AddOrUpdateState(unit, (StateType)stateType, Math.Max(1, stacks));
        Notify();
        return true;
    }

    // 按“第几个状态(index) 删多少层”删除状态
    public bool DebugRemoveState(int unitId, int index, int stacks, out string error)
    {
        error = "";
        if (!Occupancy.Placements.TryGetValue(unitId, out var p)) { error = "目标单位不存在。"; return false; }
        var states = p.Unit.States;
        if (index < 0 || index >= states.Count) { error = $"状态索引 {index} 越界（共 {states.Count} 个）。"; return false; }
        if (stacks <= 0) { error = "层数需大于 0。"; return false; }
        StateType type = states.ElementAt(index).Key;
        if (StateSystem.TryGetStateStacks(p.Unit, type, out int cur) && stacks >= cur)
            StateSystem.RemoveState(p.Unit, type);
        else
            StateSystem.RemoveStateStacks(p.Unit, type, stacks);
        Notify();
        return true;
    }
    // ---- 道具 / 装备 ----
    public bool DebugSpawnItemAtCell(int q, int r, string defId, bool isEquipment, out string error)
    {
        error = "";
        var coord = new AxialHex(q, r);
        if (!Board.Cells.ContainsKey(coord)) { error = "坐标不在战场上。"; return false; }
        var item = new GroundObject($"debug-item-{Guid.NewGuid():N}".Substring(0, 20), defId,
            isEquipment ? GroundObjectKind.Equipment : GroundObjectKind.Item, healAmount: isEquipment ? 0 : 3);
        if (!Board.TryAddObject(coord, item, out error))
        {
            if (error.Length == 0) error = "目标格无法放置道具。";
            return false;
        }
        Notify();
        return true;
    }

    public bool DebugSpawnItemToSlot(int slot, string defId, out string error)
    {
        error = "";
        if (slot < 0 || slot >= 3) { error = "槽位需为 0–2。"; return false; }
        var item = new GroundObject($"debug-item-{Guid.NewGuid():N}".Substring(0, 20), defId, GroundObjectKind.Item, healAmount: 3);
        SelectedLoadout.Items[slot] = item;
        Notify();
        return true;
    }

    public bool DebugClearSlot(int slot, out string error)
    {
        error = "";
        if (slot < 0 || slot >= 3) { error = "槽位需为 0–2。"; return false; }
        SelectedLoadout.Items[slot] = null;
        Notify();
        return true;
    }

    public bool DebugEquipToHand(string defId, int hand, out string error)
    {
        error = "";
        var equip = new GroundObject($"debug-equip-{Guid.NewGuid():N}".Substring(0, 20), defId, GroundObjectKind.Equipment, attackRange: 1);
        if (hand == 0) SelectedLoadout.LeftHand = equip;
        else SelectedLoadout.RightHand = equip;
        Notify();
        return true;
    }

    public bool DebugPlaceTrap(int q, int r, string trapId, out string error)
    {
        error = "";
        var coord = new AxialHex(q, r);
        if (!Board.Cells.ContainsKey(coord)) { error = "坐标不在战场上。"; return false; }
        if (!Board.Cells[coord].Walkable) { error = "目标格不可通行。"; return false; }
        if (Occupancy.At(coord) != null || Board.Cells[coord].Items.Count > 0) { error = "目标格有单位或道具，不能放陷阱。"; return false; }
        var trap = new GroundObject($"debug-trap-{Guid.NewGuid():N}".Substring(0, 20),
            string.IsNullOrEmpty(trapId) ? "test_trap" : trapId, GroundObjectKind.Trap, EntryTriggerMode.EveryEntry);
        if (!Board.TryAddObject(coord, trap, out error))
        {
            if (error.Length == 0) error = "无法放置陷阱。";
            return false;
        }
        Notify();
        return true;
    }

    // ---- 敌方 ----
    public bool DebugSpawnEnemy(int q, int r, int monsterId, out string error)
    {
        error = "";
        if (!LoadingSystem.MonsterDictionary.TryGetValue(monsterId, out Monster def)) { error = $"怪物 {monsterId} 不存在。"; return false; }
        var coord = new AxialHex(q, r);
        if (!Board.Cells.ContainsKey(coord) || !Board.Cells[coord].Walkable || Occupancy.At(coord) != null) { error = "目标格不可通行或已有单位。"; return false; }
        var monster = new MonsterInstance(def);
        Register(new BattleUnitPlacement(monster, monster.Name, BattlefieldRole.Enemy, 0), coord);
        Notify();
        return true;
    }

    public void DebugClearEnemies()
    {
        foreach (var p in Occupancy.Placements.Values.Where(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active).ToArray())
            p.Unit.HP = 0;
        Occupancy.SyncDeaths(); EvaluateOutcome(); Notify();
    }

    // ---- 阶段 ----
    public void DebugEndTurn() => EndCurrentTurn();

    private bool TryGetActiveUnit(int unitId, out IUnitInstance unit)
    {
        unit = null;
        if (Occupancy.Placements.TryGetValue(unitId, out var p) && p.Presence == BattlefieldPresence.Active)
        {
            unit = p.Unit;
            return true;
        }
        return false;
    }
}