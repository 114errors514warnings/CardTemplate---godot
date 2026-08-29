// BattleStatsTracker.cs
// 战斗内统计：初始 HP 快照、本局累计掉血点数、本局 HP 损失事件数、本回合已出战斗牌数。
// 不依赖 Godot，可直接单测。

using System.Collections.Generic;
using CardSimulator;

public sealed class BattleStatsTracker
{
    private readonly Dictionary<int, int> initialHpSnapshots = new Dictionary<int, int>();
    private readonly Dictionary<int, int> hpLossEventCounts = new Dictionary<int, int>();
    private readonly Dictionary<int, int> cardsPlayedThisTurnCounts = new Dictionary<int, int>();

    /// <summary>设置单位本局初始 HP 快照（一般在 OnInit 时调用）。</summary>
    public void SnapshotInitialHp(IEnumerable<IUnitInstance> units)
    {
        if (units == null)
        {
            return;
        }

        foreach (IUnitInstance unit in units)
        {
            if (unit != null)
            {
                initialHpSnapshots[unit.UniqueInGameId] = unit.HP;
            }
        }
    }

    /// <summary>清空所有统计（用于重开战斗）。</summary>
    public void Reset()
    {
        initialHpSnapshots.Clear();
        hpLossEventCounts.Clear();
        cardsPlayedThisTurnCounts.Clear();
    }

    public int GetBattleLostHp(IUnitInstance unit)
    {
        if (unit == null || !initialHpSnapshots.TryGetValue(unit.UniqueInGameId, out int initialHp))
        {
            return 0;
        }
        return initialHp > unit.HP ? initialHp - unit.HP : 0;
    }

    public int GetBattleHpLossEventCount(IUnitInstance unit)
    {
        if (unit == null)
        {
            return 0;
        }
        return hpLossEventCounts.TryGetValue(unit.UniqueInGameId, out int count) ? count : 0;
    }

    public void RecordHpLossEvent(IUnitInstance unit, int hpLoss)
    {
        if (!(unit is CharacterInstance) || hpLoss <= 0)
        {
            return;
        }
        int id = unit.UniqueInGameId;
        hpLossEventCounts[id] = hpLossEventCounts.TryGetValue(id, out int current) ? current + 1 : 1;
    }

    public int GetBattleCardsPlayedThisTurnCount(CharacterInstance player)
    {
        if (player == null)
        {
            return 0;
        }
        return cardsPlayedThisTurnCounts.TryGetValue(player.UniqueInGameId, out int count) ? count : 0;
    }

    public void RecordCardPlayedThisTurn(CharacterInstance player, Card card)
    {
        if (player == null || card == null || card.Category == CardCategory.State)
        {
            return;
        }
        int id = player.UniqueInGameId;
        cardsPlayedThisTurnCounts[id] = cardsPlayedThisTurnCounts.TryGetValue(id, out int current) ? current + 1 : 1;
    }

    public void ResetBattleCardsPlayedThisTurnCounts(IEnumerable<CharacterInstance> players)
    {
        if (players == null)
        {
            cardsPlayedThisTurnCounts.Clear();
            return;
        }
        foreach (CharacterInstance player in players)
        {
            if (player != null)
            {
                cardsPlayedThisTurnCounts[player.UniqueInGameId] = 0;
            }
        }
    }
}
