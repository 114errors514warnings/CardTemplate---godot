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
	}

	// 状态类型枚举（可挂载到 UnitInstance）
	public enum StateType
	{
		None = 0,
		Vulnerable = 1, // 易伤：受到的伤害增加50%（向下取整）
		Weak = 2,       // 虚弱：造成的攻击伤害降低25%
		Ignite = 3,
		CounterAttack = 4, // 反击：回合外受到攻击时，对来源进行一次反击（反击不会触发反击）
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
}
