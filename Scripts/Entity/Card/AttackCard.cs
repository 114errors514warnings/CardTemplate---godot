// AttackCard.cs
using Godot;
using CardSimulator;

[GlobalClass]
public partial class AttackCard : Card
{
	// 攻击卡牌专属属性
	[Export]
	public int ExtraAttack { get; set; } = 0; // 额外攻击力
	
	[Export]
	public int ExtraShield { get; set; } = 0; // 额外护盾

	// 无参构造
	public AttackCard() { }

	// 带参数构造（包含基类属性+子类专属属性）
	public AttackCard(int cardId, string uniqueInGameId, int energyCost, EffectType effectType, string effectDesc, bool needTarget, int extraAttack, int extraShield, string cardName = "")
		: base(cardId, uniqueInGameId, energyCost, CardCategory.Attack, effectType, effectDesc, needTarget, cardName)
	{
		ExtraAttack = extraAttack;
		ExtraShield = extraShield;
	}

	// 重写基类方法，补充攻击卡牌的信息
	public override string GetCardInfo()
	{
		return base.GetCardInfo() + $", Attack: {ExtraAttack}, Shield: {ExtraShield}";
	}

	public override Card CreateRuntimeInstance()
	{
		AttackCard card = new AttackCard(CardId, string.Empty, EnergyCost, EffectType, EffectDescription, NeedTarget, ExtraAttack, ExtraShield, CardName);
		card.GenerateUniqueInGameId();
		return card;
	}

	protected override CardApplyResult ApplyEffect(IUnitInstance source, IUnitInstance target)
	{
		switch (EffectType)
		{
			case EffectType.Damage:
				return new CardApplyResult(true, this, source, target, EffectSystem.ApplyAttack(source, target, ExtraAttack));
			case EffectType.Shield:
				return new CardApplyResult(true, this, source, target, EffectSystem.ApplyShield(source, ExtraShield));
			default:
				string errorMessage = $"攻击牌 CardId={CardId} 的效果类型 {EffectType} 暂未实现。";
				AppendConsoleError(errorMessage, true);
				return new CardApplyResult(false, this, source, target, errorMessage: errorMessage);
		}
	}

	// 攻击卡牌专属方法：执行攻击效果（示例）
	public void ExecuteAttackEffect(Node target)
	{
		if (NeedTarget && target == null)
		{
			GD.Print("攻击卡牌需要目标！");
			return;
		}
		
		GD.Print($"执行攻击效果：对{target?.Name ?? "无目标"}造成{ExtraAttack}点伤害，附加{ExtraShield}点护盾");
		// 此处可编写具体的攻击逻辑（如扣血、加护盾）
	}
}
