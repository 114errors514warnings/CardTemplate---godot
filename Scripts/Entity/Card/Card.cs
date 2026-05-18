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

	[Export]
	public CardKeyWord CardKeyWord { get; set; } = CardKeyWord.None; // 卡牌关键词

	// 各效果对应的参数，Params[i][0] 固定表示目标类型，后续参数为该效果自身参数
	public int[][] Params { get; set; } = Array.Empty<int[]>();

	// 构造函数（Godot Resource需保留无参构造）
	public Card() { }

	// 带参数的构造函数，NeedTarget 自动从 cardParams 推导
	public Card(int cardId, string uniqueInGameId, int energyCost, CardCategory category, EffectType[] effectTypes, string effectDesc, int[][] cardParams = null, string cardName = "", CardKeyWord cardKeyWord = CardKeyWord.None)
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
		CardKeyWord = cardKeyWord;
	}

	public bool HasKeyWord(CardKeyWord keyWord)
	{
		return (CardKeyWord & keyWord) == keyWord;
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
		Card card = new Card(CardId, string.Empty, EnergyCost, Category, EffectTypes, EffectDescription, Params, CardName, CardKeyWord);
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
				case EffectType.Heal:
					result = ApplyHealEffect(source, resolvedTargets, effectArgs);
					break;
				case EffectType.DrawCard:
					result = ApplyDrawCardEffect(source, resolvedTargets, effectArgs);
					break;
				case EffectType.AddCost:
					result = ApplyAddCostEffect(source, resolvedTargets, effectArgs);
					break;
				case EffectType.ClearAllStates:
					result = ApplyClearAllStatesEffect(source, resolvedTargets);
					break;
				case EffectType.ClearFirstNormalDebuff:
					result = ApplyClearFirstNormalDebuffEffect(source, resolvedTargets, effectArgs);
					break;
				case EffectType.ShieldSlam:
					result = ApplyShieldSlamEffect(source, resolvedTargets, effectArgs);
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

	private CardApplyResult ApplyHealEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		int healAmount = effectArgs.Length > 0 ? effectArgs[0] : 0;
		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			int hpBefore = resolvedTarget.HP;
			resolvedTarget.HP = Math.Min(resolvedTarget.Max_HP, resolvedTarget.HP + healAmount);
			AppendConsoleInfo($"{GetUnitLabel(resolvedTarget)} 治疗 {healAmount}，HP {hpBefore}->{resolvedTarget.HP}");
		}

		return new CardApplyResult(true, this, source, lastTarget);
	}

	private CardApplyResult ApplyDrawCardEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		int drawCount = effectArgs.Length > 0 ? effectArgs[0] : 0;
		IUnitInstance lastTarget = null;

		// DrawCard 效果作用于 source（抽牌者），而不是 resolvedTargets
		if (source is CharacterInstance character)
		{
			lastTarget = source;
			int drawn = 0;
			for (int i = 0; i < drawCount; i++)
			{
				if (character.drawpile.Count == 0)
				{
					if (character.discardpile.Count == 0)
					{
						break;
					}

					// 将弃牌堆洗入抽牌堆
					character.drawpile.AddRange(character.discardpile);
					character.discardpile.Clear();
					ShuffleList(character.drawpile);
					AppendConsoleInfo("抽牌堆为空：已将弃牌堆随机洗牌后放回抽牌堆。");
				}

				character.handcards.Add(character.drawpile[0]);
				character.drawpile.RemoveAt(0);
				drawn++;
			}

			AppendConsoleInfo($"{GetUnitLabel(source)} 抽取 {drawn} 张牌");
		}
		else
		{
			AppendConsoleInfo($"抽牌效果仅对角色有效");
		}

		return new CardApplyResult(true, this, source, lastTarget);
	}

	private static void ShuffleList(List<Card> cards)
	{
		if (cards == null || cards.Count <= 1)
		{
			return;
		}

		System.Random rng = new System.Random();
		for (int index = cards.Count - 1; index > 0; index--)
		{
			int swapIndex = rng.Next(index + 1);
			(cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
		}
	}

	private CardApplyResult ApplyAddCostEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		int energyAmount = effectArgs.Length > 0 ? effectArgs[0] : 0;
		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			int energyBefore = resolvedTarget.Energy;
			EffectResult effectResult = EffectSystem.ApplyAddCost(resolvedTarget, new int[] { energyAmount });
			if (effectResult != null)
			{
				AppendConsoleInfo(effectResult.BuildSummary());
			}
		}

		return new CardApplyResult(true, this, source, lastTarget);
	}

	private CardApplyResult ApplyShieldSlamEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		EffectResult lastEffectResult = null;
		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			lastEffectResult = EffectSystem.ApplyShieldSlam(source, resolvedTarget, effectArgs);
		}

		return new CardApplyResult(true, this, source, lastTarget, lastEffectResult);
	}

	private CardApplyResult ApplyClearAllStatesEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets)
	{
		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			
			// 移动所有状态牌到弃牌堆
			if (resolvedTarget.StatePile.Count > 0)
			{
				resolvedTarget.DiscardPile.AddRange(resolvedTarget.StatePile);
				resolvedTarget.StatePile.Clear();
			}

			// 清除所有状态
			var statesToRemove = new System.Collections.Generic.List<StateType>(resolvedTarget.States.Keys);
			foreach (StateType stateType in statesToRemove)
			{
				StateSystem.RemoveState(resolvedTarget, stateType);
			}

			AppendConsoleInfo($"{GetUnitLabel(resolvedTarget)} 移除所有状态");
		}

		return new CardApplyResult(true, this, source, lastTarget);
	}

	private CardApplyResult ApplyClearFirstNormalDebuffEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		IUnitInstance lastTarget = null;
		int removeCount = (effectArgs != null && effectArgs.Length > 0 && effectArgs[0] > 0) ? effectArgs[0] : 1;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			if (StateSystem.TryRemoveFirstNormalDebuffs(resolvedTarget, removeCount, out List<StateType> removedStateTypes))
			{
				AppendConsoleInfo($"{GetUnitLabel(resolvedTarget)} 移除前 {removedStateTypes.Count} 个普通弱化状态：{string.Join(", ", removedStateTypes)}");
			}
			else
			{
				AppendConsoleInfo($"{GetUnitLabel(resolvedTarget)} 没有可移除的普通弱化状态");
			}
		}

		return new CardApplyResult(true, this, source, lastTarget);
	}

	private static string GetUnitLabel(IUnitInstance unit)
	{
		if (unit == null)
		{
			return "未知单位";
		}

		Unit typedUnit = unit as Unit;
		string name = typedUnit?.Name ?? unit.GetType().Name;
		return $"{name}(ID={unit.UniqueInGameId})";
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
