using System;
using System.Collections.Generic;
using CardSimulator;

public class Monster : Unit
{
    public int[][][] Table { get; private set; }

    public Monster(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend, int[][][] table = null)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend)
    {
        Table = CloneTable(table);
    }

    public Monster(Monster m)
        : base(m)
    {
        Table = CloneTable(m.Table);
    }

    protected static int[][] CloneIntention(int[][] source)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<int[]>();
        }

        int[][] clone = new int[source.Length][];
        for (int effectIndex = 0; effectIndex < source.Length; effectIndex++)
        {
            int[] effectParams = source[effectIndex];
            clone[effectIndex] = effectParams == null
                ? Array.Empty<int>()
                : (int[])effectParams.Clone();
        }

        return clone;
    }

    private static int[][][] CloneTable(int[][][] source)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<int[][]>();
        }

        int[][][] clone = new int[source.Length][][];
        for (int intentionIndex = 0; intentionIndex < source.Length; intentionIndex++)
        {
            int[][] intention = source[intentionIndex];
            if (intention == null || intention.Length == 0)
            {
                clone[intentionIndex] = Array.Empty<int[]>();
                continue;
            }

            clone[intentionIndex] = CloneIntention(intention);
        }

        return clone;
    }
}

public class MonsterInstance : Monster, IUnitInstance
{
    public int UniqueInGameId { get; set; }
    public int Max_HP { get; set; }
    public int SelectedIntentionIndex { get; private set; } = -1;
    public int[][] SelectedIntention { get; private set; } = Array.Empty<int[]>();
    public int SelectedIntentionTargetUniqueInGameId { get; private set; } = -1;

    private int _hp;
    public int HP
    {
        get => _hp;
        set
        {
            int oldHp = _hp;
            bool wasAlive = _hp > 0;
            _hp = value;

            BattleSytem battle = BattleSytem.Current;
            battle?.OnUnitHpChanged(this, oldHp, _hp);

            if (wasAlive && _hp <= 0)
            {
                if (battle != null)
                {
                    battle.HandleUnitDeath(OnDead);
                    return;
                }

                OnDead?.Invoke();
            }
        }
    }

    public System.Action OnDead { get; set; }
    public System.Action<StateEndedContext> OnStateEnded { get; set; }
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

    public MonsterInstance(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend)
    {
        // states = new List<State>();
        UniqueInGameId = UniqueIdGenerator.NextMonsterId();
        Max_HP = MAX_HP;
        _hp = MAX_HP;
        Attack = Ini_Attack;
        Defend = Ini_Defend;
        Shield = 0;
        posx = 0;
        posy = 0;
    }

    public MonsterInstance(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend
        , int HP, /*List<State> states,*/ int Shield, int Attack, int Defend, float posx, float posy)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend)
    {
        // this.states = new List<State>(states);
        UniqueInGameId = UniqueIdGenerator.NextMonsterId();
        Max_HP = MAX_HP;
        _hp = HP;
        this.Attack = Attack;
        this.Defend = Defend;
        this.Shield = Shield;
        this.posx = posx;
        this.posy = posy;
    }

    public MonsterInstance(Monster m) : base(m)
    {
        // states = new List<State>();
        UniqueInGameId = UniqueIdGenerator.NextMonsterId();
        Max_HP = m.MAX_HP;
        _hp = Max_HP;
        Attack = Ini_Attack;
        Defend = Ini_Defend;
        Shield = 0;
        posx = 0;
        posy = 0;
    }

    public void SetSelectedIntention(int intentionIndex, int[][] intention)
    {
        SelectedIntentionIndex = intentionIndex;
        SelectedIntention = CloneIntention(intention);
        SelectedIntentionTargetUniqueInGameId = -1;
    }

    public void SetSelectedIntentionTarget(int targetUniqueInGameId)
    {
        SelectedIntentionTargetUniqueInGameId = targetUniqueInGameId;
    }

    public void ClearSelectedIntention()
    {
        SelectedIntentionIndex = -1;
        SelectedIntention = Array.Empty<int[]>();
        SelectedIntentionTargetUniqueInGameId = -1;
    }
}
