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
	public AttackCard(int cardId, string uniqueInGameId, int energyCost, EffectType effectType, string effectDesc, bool needTarget, int extraAttack, int extraShield)
		: base(cardId, uniqueInGameId, energyCost, CardCategory.Attack, effectType, effectDesc, needTarget)
	{
		ExtraAttack = extraAttack;
		ExtraShield = extraShield;
	}

	// 重写基类方法，补充攻击卡牌的信息
	public override string GetCardInfo()
	{
		
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
