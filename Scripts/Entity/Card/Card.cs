// Card.cs
using Godot;
using CardSimulator;

// 标记为可在编辑器中创建的资源
[GlobalClass]
public partial class Card : Resource
{
	// 通用属性 - 可在编辑器中导出编辑
	[Export]
	public int CardId { get; set; } = 0; // 卡牌模板ID（相同卡牌ID一致）
	
	[Export]
	public string UniqueInGameId { get; set; } = string.Empty; // 局内唯一ID
	
	[Export]
	public int EnergyCost { get; set; } = 0; // 消耗能量
	
	[Export]
	public CardCategory Category { get; set; } // 卡牌种类（通过枚举限定）
	
	[Export]
	public EffectType EffectType { get; set; } // 效果类型
	
	[Export(PropertyHint.MultilineText)]
	public string EffectDescription { get; set; } = string.Empty; // 效果描述
	
	[Export]
	public bool NeedTarget { get; set; } = false; // 是否需要目标

	// 构造函数（Godot Resource需保留无参构造）
	public Card() { }

	// 带参数的构造函数（便于代码中快速创建卡牌）
	public Card(int cardId, string uniqueInGameId, int energyCost, CardCategory category, EffectType effectType, string effectDesc, bool needTarget)
	{
		CardId = cardId;
		UniqueInGameId = uniqueInGameId;
		EnergyCost = energyCost;
		Category = category;
		EffectType = effectType;
		EffectDescription = effectDesc;
		NeedTarget = needTarget;
	}

	// 通用方法：获取卡牌基础信息（示例）
	public virtual string GetCardInfo()
	{
		
	}

	// 通用方法：生成局内唯一ID（可调用此方法初始化）
	public void GenerateUniqueInGameId()
	{
		// 使用GUID生成唯一ID，确保局内每张牌唯一
		UniqueInGameId = System.Guid.NewGuid().ToString();
	}
}
