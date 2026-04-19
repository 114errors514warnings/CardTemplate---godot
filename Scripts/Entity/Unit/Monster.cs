using System.Collections.Generic;

public class Monster : Unit
{
    // 用于读取怪物的意图
    // public List<Motion> intentions;
    public int MaxActionTime;

    public Monster(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend, int actionTime)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend)
    {
        this.MaxActionTime = actionTime;
    }

    public Monster(Monster m)
        : base(m)
    {
        MaxActionTime = m.MaxActionTime;
    }
}

public class MonsterInstance : Monster, IUnitInstance
{
    public int UniqueInGameId { get; set; }
    public int Max_HP { get; set; }
    public int HP { get; set; }
    // public List<State> states { get; set; }
    public int Shield { get; set; }
    public int Attack { get; set; }
    public int Defend { get; set; }
    public float posx { get; set; }
    public float posy { get; set; }

    public MonsterInstance(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend, int ActionTime)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend, ActionTime)
    {
        // states = new List<State>();
        UniqueInGameId = UniqueIdGenerator.NextMonsterId();
        Max_HP = MAX_HP;
        HP = MAX_HP;
        Attack = Ini_Attack;
        Defend = Ini_Defend;
        Shield = 0;
        posx = 0;
        posy = 0;
    }

    public MonsterInstance(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend, int ActionTime
        , int HP, /*List<State> states,*/ int Shield, int Attack, int Defend, float posx, float posy)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend, ActionTime)
    {
        // this.states = new List<State>(states);
        UniqueInGameId = UniqueIdGenerator.NextMonsterId();
        Max_HP = MAX_HP;
        this.HP = HP;
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
        HP = Max_HP;
        Attack = Ini_Attack;
        Defend = Ini_Defend;
        Shield = 0;
        posx = 0;
        posy = 0;
    }
}
