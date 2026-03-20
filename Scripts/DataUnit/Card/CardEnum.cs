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
		Damage = 0,        // 造成伤害
		Shield,        // 增加护盾
		AddState,		//添加状态
		ClearState,
		Heal,          // 治疗
		DrawCard,      // 抽卡
	}
}
