// Card.cs
using Godot;
using CardSimulator;
using System;
using System.Collections.Generic;

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

	// 效果类型列表，支持多效果（CSV中用"|"分隔）
	public EffectType[] EffectTypes { get; set; } = Array.Empty<EffectType>();
	
	[Export(PropertyHint.MultilineText)]
	public string EffectDescription { get; set; } = string.Empty; // 效果描述
	
	[Export]
	public bool NeedTarget { get; set; } = false; // 是否需要目标

	// 各效果对应的参数，Params[i][0] 固定表示目标类型，后续参数为该效果自身参数
	public int[][] Params { get; set; } = Array.Empty<int[]>();

	// 构造函数（Godot Resource需保留无参构造）
	public Card() { }

	// 带参数的构造函数，NeedTarget 自动从 cardParams 推导
	public Card(int cardId, string uniqueInGameId, int energyCost, CardCategory category, EffectType[] effectTypes, string effectDesc, int[][] cardParams = null, string cardName = "")
	{
		CardId = cardId;
		CardName = cardName;
		UniqueInGameId = uniqueInGameId;
		EnergyCost = energyCost;
		Category = category;
		EffectTypes = effectTypes ?? Array.Empty<EffectType>();
		EffectDescription = effectDesc;
		Params = cardParams ?? Array.Empty<int[]>();
		NeedTarget = DeriveNeedTarget(Params);
	}

	// 自动推导 NeedTarget：任意效果的 TargetType == SelectedTarget 则为 true
	private static bool DeriveNeedTarget(int[][] cardParams)
	{
		if (cardParams == null) return false;
		foreach (int[] p in cardParams)
		{
			if (p != null && p.Length > 0 && p[0] == (int)EffectTargetType.SelectedTarget)
				return true;
		}
		return false;
	}

	// 通用方法：获取卡牌基础信息
	public virtual string GetCardInfo()
	{
		string effects = string.Join("|", EffectTypes);
		return $"Card ID: {CardId}, Name: {CardName}, Energy: {EnergyCost}, Category: {Category}, Effects: {effects}";
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
		Card card = new Card(CardId, string.Empty, EnergyCost, Category, EffectTypes, EffectDescription, Params, CardName);
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
		CardApplyResult lastResult = null;

		for (int i = 0; i < EffectTypes.Length; i++)
		{
			int[] rawEffectParams = (Params != null && i < Params.Length) ? Params[i] : Array.Empty<int>();
			EffectType effectType = EffectTypes[i];
			EffectTargetType effectTargetType = ParseEffectTargetType(rawEffectParams);
			int[] effectArgs = GetEffectArguments(rawEffectParams);
			List<IUnitInstance> resolvedTargets = ResolveEffectTargets(source, target, effectTargetType);

			if (resolvedTargets.Count == 0)
			{
				string errorMessage = $"卡牌ID {CardId} 的效果类型 {effectType} 未解析出有效目标，targetType={effectTargetType}。";
				AppendConsoleError(errorMessage, true);
				return new CardApplyResult(false, this, source, target, errorMessage: errorMessage);
			}

			CardApplyResult result;
			switch (effectType)
			{
				case EffectType.Damage:
					result = ApplyDamageEffect(source, resolvedTargets, effectArgs);
					break;
				case EffectType.Shield:
					result = ApplyShieldEffect(source, resolvedTargets, effectArgs);
					break;
				case EffectType.AddState:
					result = ApplyAddStateEffect(source, target, resolvedTargets, effectArgs);
					break;
				case EffectType.ClearState:
					result = ApplyClearStateEffect(source, target, resolvedTargets, effectArgs);
					break;
				default:
					string errorMessage = $"卡牌ID {CardId} 的效果类型 {effectType} 暂未实现。";
					AppendConsoleError(errorMessage, true);
					result = new CardApplyResult(false, this, source, target, errorMessage: errorMessage);
					break;
			}

			if (!result.Success)
				return result;

			lastResult = result;
		}

		if (lastResult == null)
		{
			return new CardApplyResult(true, this, source, target);
		}

		return lastResult;
	}

	private CardApplyResult ApplyDamageEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		EffectResult lastEffectResult = null;
		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			lastEffectResult = EffectSystem.ApplyAttack(source, resolvedTarget, effectArgs);
		}

		return new CardApplyResult(true, this, source, lastTarget, lastEffectResult);
	}

	private CardApplyResult ApplyShieldEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		EffectResult lastEffectResult = null;
		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			lastEffectResult = EffectSystem.ApplyShield(resolvedTarget, effectArgs);
		}

		return new CardApplyResult(true, this, source, lastTarget, lastEffectResult);
	}

	private CardApplyResult ApplyAddStateEffect(IUnitInstance source, IUnitInstance originalTarget, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		if (effectArgs.Length <= 0)
		{
			string errorMessage = $"卡牌ID {CardId} 的 AddState 缺少参数，至少需要 stateType。";
			AppendConsoleError(errorMessage, true);
			return new CardApplyResult(false, this, source, originalTarget, errorMessage: errorMessage);
		}

		StateType stateType = (StateType)effectArgs[0];
		if (!Enum.IsDefined(typeof(StateType), stateType) || stateType == StateType.None)
		{
			string errorMessage = $"卡牌ID {CardId} 的 AddState 参数非法，stateType={effectArgs[0]}。";
			AppendConsoleError(errorMessage, true);
			return new CardApplyResult(false, this, source, originalTarget, errorMessage: errorMessage);
		}

		int stacks = effectArgs.Length > 1 ? effectArgs[1] : 1;
		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			StateSystem.AddOrUpdateState(resolvedTarget, stateType, stacks);
		}

		return new CardApplyResult(true, this, source, lastTarget);
	}

	private CardApplyResult ApplyClearStateEffect(IUnitInstance source, IUnitInstance originalTarget, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		if (effectArgs.Length <= 0)
		{
			string errorMessage = $"卡牌ID {CardId} 的 ClearState 缺少参数，至少需要 stateType。";
			AppendConsoleError(errorMessage, true);
			return new CardApplyResult(false, this, source, originalTarget, errorMessage: errorMessage);
		}

		StateType stateType = (StateType)effectArgs[0];
		if (!Enum.IsDefined(typeof(StateType), stateType) || stateType == StateType.None)
		{
			string errorMessage = $"卡牌ID {CardId} 的 ClearState 参数非法，stateType={effectArgs[0]}。";
			AppendConsoleError(errorMessage, true);
			return new CardApplyResult(false, this, source, originalTarget, errorMessage: errorMessage);
		}

		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			StateSystem.RemoveState(resolvedTarget, stateType);
		}

		return new CardApplyResult(true, this, source, lastTarget);
	}

	private static EffectTargetType ParseEffectTargetType(int[] rawEffectParams)
	{
		if (rawEffectParams == null || rawEffectParams.Length == 0)
		{
			return EffectTargetType.Auto;
		}

		EffectTargetType parsed = (EffectTargetType)rawEffectParams[0];
		return Enum.IsDefined(typeof(EffectTargetType), parsed) ? parsed : EffectTargetType.Auto;
	}

	private static int[] GetEffectArguments(int[] rawEffectParams)
	{
		if (rawEffectParams == null || rawEffectParams.Length <= 1)
		{
			return Array.Empty<int>();
		}

		int[] args = new int[rawEffectParams.Length - 1];
		Array.Copy(rawEffectParams, 1, args, 0, args.Length);
		return args;
	}

	private static List<IUnitInstance> ResolveEffectTargets(IUnitInstance source, IUnitInstance selectedTarget, EffectTargetType effectTargetType)
	{
		List<IUnitInstance> targets = new List<IUnitInstance>();
		switch (effectTargetType)
		{
			case EffectTargetType.Self:
				if (source != null)
				{
					targets.Add(source);
				}
				break;
			case EffectTargetType.SelectedTarget:
				if (selectedTarget != null)
				{
					targets.Add(selectedTarget);
				}
				break;
			case EffectTargetType.AllEnemies:
				targets.AddRange(BattleSytem.Current?.GetEnemyUnits(source) ?? new List<IUnitInstance>());
				break;
			case EffectTargetType.AllUnits:
				targets.AddRange(BattleSytem.Current?.GetAllUnits() ?? new List<IUnitInstance>());
				break;
			case EffectTargetType.Auto:
			default:
				if (selectedTarget != null)
				{
					targets.Add(selectedTarget);
				}
				else if (source != null)
				{
					targets.Add(source);
				}
				break;
		}

		return targets;
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
