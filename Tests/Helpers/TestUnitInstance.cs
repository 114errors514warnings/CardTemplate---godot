// Helpers/TestUnitInstance.cs
// 测试用 IUnitInstance 桩：实现接口全部成员（BattleStatsTracker 等模块要用到）。

using System;
using System.Collections.Generic;
using CardSimulator;

internal sealed class TestUnitInstance : IUnitInstance
{
    public int UniqueInGameId { get; set; }
    public int Max_HP { get; set; }
    public int HP { get; set; }
    public string Name { get; set; } = "TestUnit";

    public Dictionary<StateType, StateRuntimeData> States { get; } = new Dictionary<StateType, StateRuntimeData>();
    public List<Card> StatePile { get; } = new List<Card>();
    public List<Card> DiscardPile { get; } = new List<Card>();
    public List<Card> ExhaustPile { get; } = new List<Card>();
    public int Shield { get; set; }
    public int Energy { get; set; }
    public int Attack { get; set; }
    public int Defend { get; set; }
    public float posx { get; set; }
    public float posy { get; set; }
    public Action<StateEndedContext> OnStateEnded { get; set; }
    public Action OnDead { get; set; }
}
