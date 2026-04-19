// Card.cs
using Godot;
using CardSimulator;
using System;

// 标记为可在编辑器中创建的资源
[GlobalClass]
public partial class Card : Resource
{
	public sealed class CardApplyResult
	{
		public bool Success { get; }
		public Card Card { get; }
		public IUnitInstance Source { get; }
		public IUnitInstance Target { get; }
		public EffectResult EffectResult { get; }
		public string ErrorMessage { get; }

		public CardApplyResult(bool success, Card card, IUnitInstance source, IUnitInstance target, EffectResult effectResult = null, string errorMessage = "")
		{
			Success = success;
			Card = card;
			Source = source;
			Target = target;
			EffectResult = effectResult;
			ErrorMessage = errorMessage ?? string.Empty;
		}
	}

	// 通用属性 - 可在编辑器中导出编辑
	[Export]
	public int CardId { get; set; } = 0; // 卡牌模板ID（相同卡牌ID一致）

	[Export]
	public string CardName { get; set; } = string.Empty; // 卡牌名称
	
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
	public Card(int cardId, string uniqueInGameId, int energyCost, CardCategory category, EffectType effectType, string effectDesc, bool needTarget, string cardName = "")
	{
		CardId = cardId;
		CardName = cardName;
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
		return $"Card ID: {CardId}, Name: {CardName}, Energy: {EnergyCost}, Category: {Category}";
	}

	public CardApplyResult Apply(IUnitInstance source, IUnitInstance target = null)
	{
		if (source == null)
		{
			throw new ArgumentNullException(nameof(source));
		}

		if (NeedTarget && target == null)
		{
			string errorMessage = $"卡牌ID {CardId} 需要目标，但本次出牌未传入目标。";
			AppendConsoleError(errorMessage, true);
			return new CardApplyResult(false, this, source, target, errorMessage: errorMessage);
		}

		return ApplyEffect(source, target);
	}

	public virtual Card CreateRuntimeInstance()
	{
		Card card = new Card(CardId, string.Empty, EnergyCost, Category, EffectType, EffectDescription, NeedTarget, CardName);
		card.GenerateUniqueInGameId();
		return card;
	}

	// 通用方法：生成局内唯一ID（可调用此方法初始化）
	public void GenerateUniqueInGameId()
	{
		// 7位局内唯一ID，卡牌实例固定以3开头
		UniqueInGameId = UniqueIdGenerator.NextCardId().ToString("D7");
	}

	protected virtual CardApplyResult ApplyEffect(IUnitInstance source, IUnitInstance target)
	{
		switch (EffectType)
		{
			case EffectType.Damage:
				return new CardApplyResult(true, this, source, target, EffectSystem.ApplyAttack(source, target));
			case EffectType.Shield:
				return new CardApplyResult(true, this, source, target, EffectSystem.ApplyShield(source));
			default:
				string errorMessage = $"卡牌ID {CardId} 的效果类型 {EffectType} 暂未实现。";
				AppendConsoleError(errorMessage, true);
				return new CardApplyResult(false, this, source, target, errorMessage: errorMessage);
		}
	}

	protected static void AppendConsoleInfo(string message)
	{
		AppendConsole("[信息] " + message, false);
	}

	protected static void AppendConsoleError(string message, bool alsoPrintError = false)
	{
		AppendConsole("[错误] " + message, alsoPrintError);
	}

	private static void AppendConsole(string message, bool alsoPrintError)
	{
		if (alsoPrintError)
		{
			GD.PrintErr(message);
		}

		SceneTree sceneTree = Engine.GetMainLoop() as SceneTree;
		Node scene = sceneTree?.CurrentScene;
		if (scene == null)
		{
			return;
		}

		RichTextLabel console = scene.GetNodeOrNull<RichTextLabel>("ConsoleContainer/Console");
		if (console == null)
		{
			console = scene.GetNodeOrNull<RichTextLabel>("UI_Main/ConsoleContainer/Console");
		}

		if (console == null)
		{
			return;
		}

		if (!string.IsNullOrEmpty(console.Text))
		{
			console.Text += "\n";
		}

		console.Text += message;
	}
}
