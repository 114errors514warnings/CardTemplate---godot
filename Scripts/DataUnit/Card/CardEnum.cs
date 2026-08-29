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
		Crit = 1 << 3, // 暴击
	}

	/// <summary>单个运行时关键词的生命周期标记</summary>
	[System.Flags]
	public enum KeywordFlag
	{
		None = 0,
		RemoveAfterActivate = 1 << 0, // 生效后移除
		RemoveAtTurnEnd = 1 << 1,     // 回合结束时移除
	}

	/// <summary>运行时附加的单个关键词条目（每条目描述一个关键词及其独立标志）</summary>
	public struct AppliedKeywordEntry
	{
		public CardKeyWord Keyword;
		public KeywordFlag Flags;
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
		AddKeyword = 14,            // 为卡牌添加运行时关键词
		MirrorShieldToAllies = 15,  // 将前续Shield效果的累积护盾复制到全体友方
		RearrangeMonsterTargets = 16, // 到我身后：将所有怪物单攻意图重定向到施法者，每改一个目标获得 1 点护盾
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
		ExtraEnergy = 7, // 下回合额外获得能量（已废弃，合并到 TurnStartEffect）
		Void = 8, // 虚无
		CourageArmor = 9, // 勇气铠甲：打出攻击牌时触发一次防御
		TurnStartEffect = 10,     // 统一回合开始效果
		GainAttackOnHpLoss = 11,  // 燃血：失去生命时+1攻击
		CounterAttackWhenAttacking = 12, // 紧咬不放：攻击施加者时触发其反击
		ShieldGuard = 13,         // 持盾防守：下回合防御+3/攻击-1/禁战斗牌
		ShieldCapEqualsHP = 14,   // 阵地：回合开始时护盾上限=HP
		ForcedTaunt = 15,         // 城墙：有护盾时强制嘲讽
		RetainAllBattleCards = 16,// 蓄势待发：保留全部战斗牌，反击时替代打出
		HpLossPerCardPlayed = 17,  // 强撑：每打出一张牌，失去1点生命
		BattleCardBlocked = 18,    // 禁止打出战斗牌（回合始移除）
		DrawLock = 19,             // 战术支援：本回合不能再抽牌
		NextBattleCardFree = 20,   // 统一战线：下一张战斗牌免费
	}

	/// <summary>TurnStartEffect 状态下，回合开始时获得的资源类型</summary>
	public enum TurnStartResourceType
	{
		None = 0,
		ExtraEnergy = 1,   // 下回合额外获得能量
		Shield = 2,        // 下回合防御（额外护盾）
	}

	// 每个效果独立的目标选择方式
	public enum EffectTargetType
	{
		Auto = 0,
		Self = 1,
		SelectedTarget = 2,
		AllEnemies = 3,
		AllUnits = 4,
		AllAllies = 5,   // 全体友方单位（不含自身）
	}

	// 怪物 Damage 意图的目标模式。
	// 注意：枚举值从 1 开始，便于在怪物意图参数中显式书写。
	public enum MonsterDamageTargetMode
	{
		RandomPerHit = 1,
		RandomSameTargetWithinIntention = 2,
	}
}
