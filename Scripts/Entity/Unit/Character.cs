using System;
using System.Collections.Generic;

// 通过配置加载的人物
public class Character : Unit
{
    // Skill 类型在当前项目中未定义，已注释相关成员。
    // public List<Skill> skill;
    public int drawCardNum;

    public Character(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend, int drawCardNum)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend)
    {
        this.drawCardNum = drawCardNum;
    }

    public Character(Character c) : base(c)
    {
        drawCardNum = c.drawCardNum;
    }
}

public class CharacterInstance : Character, IUnitInstance
{
    public int UniqueInGameId { get; set; }
    public int Max_HP { get; set; }

    private int _hp;
    public int HP
    {
        get => _hp;
        set
        {
            bool wasAlive = _hp > 0;
            _hp = value;
            if (wasAlive && _hp <= 0)
            {
                OnDead?.Invoke();
            }
        }
    }

    public System.Action OnDead { get; set; }
    // public List<State> states { get; set; }
    public int Shield { get; set; }
    public int Max_costs;
    public int costs;
    public int Attack { get; set; }
    public int Defend { get; set; }
    public float posx { get; set; }
    public float posy { get; set; }

    public List<Card> cards;
    public List<Card> handcards;
    public List<Card> drawpile;
    public List<Card> discardpile;

    public CharacterInstance(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend, int drawCardNum)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend, drawCardNum)
    {
        // states = new List<State>();
        UniqueInGameId = UniqueIdGenerator.NextCharacterId();
        Max_HP = MAX_HP;
        _hp = MAX_HP;
        Attack = Ini_Attack;
        Defend = Ini_Defend;
        Shield = 0;
        Max_costs = 3;
        costs = 0;
        posx = 0;
        posy = 0;
        cards = new List<Card>();
        handcards = new List<Card>();
        drawpile = new List<Card>();
        discardpile = new List<Card>();
    }

    public CharacterInstance(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend, int drawCardNum
        , int HP, /*List<State> states,*/ int Shield, int costs, int Attack, int Defend, float posx, float posy, List<Card> cards, List<Card> handcards, List<Card> drawpile, List<Card> discardpile)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend, drawCardNum)
    {
        // this.states = new List<State>(states);
        UniqueInGameId = UniqueIdGenerator.NextCharacterId();
        _hp = HP;
        this.Attack = Attack;
        this.Defend = Defend;
        this.Shield = Shield;
        Max_costs = 3;
        this.costs = costs;
        this.posx = posx;
        this.posy = posy;
        this.cards = new List<Card>(cards);
        this.handcards = new List<Card>(handcards);
        this.drawpile = new List<Card>(drawpile);
        this.discardpile = new List<Card>(discardpile);
    }

    public CharacterInstance(Character c) : base(c)
    {
        // states = new List<State>();
        UniqueInGameId = UniqueIdGenerator.NextCharacterId();
        Max_HP = c.MAX_HP;
        _hp = c.MAX_HP;
        Attack = c.Ini_Attack;
        Defend = c.Ini_Defend;
        Shield = 0;
        Max_costs = 3;
        costs = 0;
        posx = 0;
        posy = 0;
        cards = new List<Card>();
        handcards = new List<Card>();
        drawpile = new List<Card>();
        discardpile = new List<Card>();
    }
}

public class RougeCharacter : CharacterInstance /*, RougeChaFactor */
{
    public uint Patience { get; set; }

    public RougeCharacter(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend, int drawCardNum)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend, drawCardNum)
    {
    }

    public RougeCharacter(Character c) : base(c)
    {
    }

    public RougeCharacter(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend, int drawCardNum
        , int HP, /*List<State> states,*/ int Shield, int costs, int Attack, int Defend, float posx, float posy, List<Card> cards, List<Card> handcards, List<Card> drawpile, List<Card> discardpile)
        : base(id, Name, MAX_HP, Ini_Attack, Ini_Defend, drawCardNum, HP, /*states,*/ Shield, costs, Attack, Defend, posx, posy, cards, handcards, drawpile, discardpile)
    {
    }
}
