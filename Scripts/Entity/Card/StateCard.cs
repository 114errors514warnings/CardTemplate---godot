// StateCard.cs
using Godot;
using CardSimulator;

[GlobalClass]
public partial class StateCard : Card
{
	// 无参构造
	public StateCard() { }

	// 带参数构造（无专属属性）
	public StateCard(int cardId, string uniqueInGameId, int energyCost, EffectType effectType, string effectDesc, bool needTarget)
		: base(cardId, uniqueInGameId, energyCost, CardCategory.State, effectType, effectDesc, needTarget)
	{
	}

	// 重写基类方法（无额外信息）
	public override string GetCardInfo()
	{
		var baseInfo = base.GetCardInfo();
		return $"{baseInfo} | 类型：状态卡牌";
	}

	// 状态卡牌专属方法：应用状态效果（示例）
	public void ApplyStateEffect(Node target)
	{
		if (NeedTarget && target == null)
		{
			GD.Print("状态卡牌需要目标！");
			return;
		}
		
		GD.Print($"应用状态效果：{EffectDescription}");
		// 此处可编写具体的状态逻辑（如持续伤害、属性修改等）
	}
}
