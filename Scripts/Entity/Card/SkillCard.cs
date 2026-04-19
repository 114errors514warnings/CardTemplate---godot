// SkillCard.cs
using Godot;
using CardSimulator;

[GlobalClass]
public partial class SkillCard : Card
{
	// 技能卡牌专属属性
	[Export]
	public int ExtraShield { get; set; } = 0; // 额外护盾

	// 无参构造
	public SkillCard() { }

	// 带参数构造
	public SkillCard(int cardId, string uniqueInGameId, int energyCost, EffectType effectType, string effectDesc, bool needTarget, int extraShield, string cardName = "")
		: base(cardId, uniqueInGameId, energyCost, CardCategory.Skill, effectType, effectDesc, needTarget, cardName)
	{
		ExtraShield = extraShield;
	}

	// 重写基类方法
	public override string GetCardInfo()
	{
		return base.GetCardInfo() + $", Shield: {ExtraShield}";
	}

	public override Card CreateRuntimeInstance()
	{
		SkillCard card = new SkillCard(CardId, string.Empty, EnergyCost, EffectType, EffectDescription, NeedTarget, ExtraShield, CardName);
		card.GenerateUniqueInGameId();
		return card;
	}

	protected override CardApplyResult ApplyEffect(IUnitInstance source, IUnitInstance target)
	{
		switch (EffectType)
		{
			case EffectType.Shield:
				return new CardApplyResult(true, this, source, target, EffectSystem.ApplyShield(source, ExtraShield));
			case EffectType.Damage:
				return new CardApplyResult(true, this, source, target, EffectSystem.ApplyAttack(source, target));
			default:
				string errorMessage = $"技能牌 CardId={CardId} 的效果类型 {EffectType} 暂未实现。";
				AppendConsoleError(errorMessage, true);
				return new CardApplyResult(false, this, source, target, errorMessage: errorMessage);
		}
	}

	// 技能卡牌专属方法：执行技能效果（示例）
	public void ExecuteSkillEffect(Node target = null)
	{
		if (NeedTarget && target == null)
		{
			GD.Print("技能卡牌需要目标！");
			return;
		}
		
		GD.Print($"执行技能效果：为{target?.Name ?? "自身"}附加{ExtraShield}点护盾");
		// 此处可编写具体的技能逻辑
	}
}
