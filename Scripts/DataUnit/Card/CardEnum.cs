// CardEnums.cs
namespace CardSimulator
{
	// 卡牌种类枚举
	public enum CardCategory
	{
		Attack = 0,    // 攻击卡牌
		Skill,     // 技能卡牌
		State     // 状态卡牌
	}

	[System.Flags]
	public enum CardKeyWord
	{
		None = 0,
		Retain = 1 << 0, // 保留：回合结束时不弃置
		Exhaust = 1 << 1, // 消耗：打出后进入消耗牌堆，不再参与常规循环
		InfiniteUpgrade = 1 << 2, // 无限升级：可持续记录升级级数
	}

	public enum CardConditionType
	{
		None = 0,
		NoBattleCardPlayedThisTurn = 1, // 本回合内，该牌所属角色尚未打出过战斗牌
	}

	// 效果类型枚举（示例，你可根据需求扩展）
	public enum EffectType
	{
		None = 0,       // 无效果
		Damage = 1,        // 造成伤害
		Shield,        // 增加护盾
		AddState,		//添加状态
		ClearState,
		Heal,          // 治疗
		DrawCard,      // 抽卡
		AddCost,       // 增加能量
		ClearAllStates,// 清除所有状态
		ClearFirstNormalDebuff, // 清除状态栏中的第一个普通弱化状态
		ShieldSlam,    // 护盾冲撞：额外造成等同自身护盾值的伤害
		UpgradeBattleCard, // 升级战斗中的具体卡牌
		UpgradePermanentCard, // 升级角色默认卡组中的具体卡牌
		DamageByBattleLostHp, // 额外造成自身本局已失去生命值的伤害
	}

	public enum CardOperationTargetType
	{
		None = 0,
		SelectHandCards = 1,
		RandomHandCards = 2,
		RandomDefaultDeckCards = 3,
	}

	// 状态类型枚举（可挂载到 UnitInstance）
	public enum StateType
	{
		None = 0,
		Vulnerable = 1, // 易伤：受到的伤害增加50%（向下取整）
		Weak = 2,       // 虚弱：造成的攻击伤害降低25%
		Ignite = 3,
		CounterAttack = 4, // 反击：回合外受到攻击时，对来源进行一次反击（反击不会触发反击）
		WhirlwindSlash = 5, // 旋风斩：回合外的攻击作用于所有敌人
		AddAttack = 6, // 增加攻击力
		ExtraEnergy = 7, // 下回合额外获得能量
		Void = 8, // 虚无
		CourageArmor = 9, // 勇气铠甲：打出攻击牌时触发一次防御
	}

	// 每个效果独立的目标选择方式
	public enum EffectTargetType
	{
		Auto = 0,
		Self = 1,
		SelectedTarget = 2,
		AllEnemies = 3,
		AllUnits = 4,
	}

	// 怪物 Damage 意图的目标模式。
	// 注意：枚举值从 1 开始，便于在怪物意图参数中显式书写。
	public enum MonsterDamageTargetMode
	{
		RandomPerHit = 1,
		RandomSameTargetWithinIntention = 2,
	}
}
