using System.Threading;
//using System.Collections.Generic;
//using UnityEditor.Experimental.GraphView;
//using UnityEngine;

// Unit 类仅包含数据，不需要 Unity 相关引用。
//作为所有游戏中出现单位的基类，不管是从配置中加载的还是在游戏中的实体
public class Unit
{
	public int id;
	public string Name;
	public int MAX_HP;
	public int Ini_Attack;
	public int Ini_Defend;

	// 当前buff/debuff信息：键为效果名称（如"力量""易伤"），值为效果层数
	//public Dictionary<string, int> buffs = new Dictionary<string, int>();

	public Unit(int id, string Name, int MAX_HP, int Ini_Attack, int Ini_Defend)
	{
		this.id = id;
		this.Name = Name;
		this.MAX_HP = MAX_HP;
		this.Ini_Attack = Ini_Attack;
		this.Ini_Defend = Ini_Defend;
	}
	public Unit(Unit c)
	//拷贝构造
	{
		id = c.id;
		Name = c.Name;
		MAX_HP = c.MAX_HP;
		Ini_Attack = c.Ini_Attack;
		Ini_Defend = c.Ini_Defend;
	}
}

public interface IUnitInstance
{
	int UniqueInGameId { get; set; }
	int Max_HP { get; set; }
	int HP { get; set; }
	// State 类型在当前项目中未定义，已注释相关成员。
	// List<State> states { get; set; }
	int Shield { get; set; }
	int Attack { get; set; }
	int Defend { get; set; }
	float posx { get; set; }
	float posy { get; set; }

	/// <summary>
	/// HP 从正值降至 0 或以下时触发一次，由 BattleSystem 在创建实例时注入。
	/// </summary>
	System.Action OnDead { get; set; }
}

/// <summary>
/// 7位局内唯一ID生成器：
/// 人物实例以0开头，怪物实例以1开头，卡牌实例以3开头。
/// </summary>
public static class UniqueIdGenerator
{
	private const int CharacterPrefix = 0;
	private const int MonsterPrefix = 1;
	private const int CardPrefix = 3;
	private const int MaxSerial = 999999;

	private static int characterSerial = 0;
	private static int monsterSerial = 0;
	private static int cardSerial = 0;

	public static int NextCharacterId()
	{
		return BuildId(CharacterPrefix, Interlocked.Increment(ref characterSerial));
	}

	public static int NextMonsterId()
	{
		return BuildId(MonsterPrefix, Interlocked.Increment(ref monsterSerial));
	}

	public static int NextCardId()
	{
		return BuildId(CardPrefix, Interlocked.Increment(ref cardSerial));
	}

	private static int BuildId(int prefix, int serial)
	{
		int normalized = ((serial - 1) % MaxSerial) + 1;
		return (prefix * 1000000) + normalized;
	}
}
