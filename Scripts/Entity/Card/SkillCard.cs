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
	public SkillCard(int cardId, string uniqueInGameId, int energyCost, EffectType effectType, string effectDesc, bool needTarget, int extraShield)
		: base(cardId, uniqueInGameId, energyCost, CardCategory.Skill, effectType, effectDesc, needTarget)
	{
		ExtraShield = extraShield;
	}

	// 重写基类方法
	public override string GetCardInfo()
	{
		
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
