// Card.cs
using Godot;
using CardSimulator;
using System;
using System.Collections.Generic;
using System.Text;

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
		public IReadOnlyList<EffectResult> IndividualEffectResults { get; }
		public string ErrorMessage { get; }

		public CardApplyResult(bool success, Card card, IUnitInstance source, IUnitInstance target, EffectResult effectResult = null, string errorMessage = "")
			: this(success, card, source, target, effectResult, null, errorMessage) { }

		public CardApplyResult(bool success, Card card, IUnitInstance source, IUnitInstance target, EffectResult effectResult, List<EffectResult> individualEffectResults, string errorMessage = "")
		{
			Success = success;
			Card = card;
			Source = source;
			Target = target;
			EffectResult = effectResult;
			IndividualEffectResults = individualEffectResults?.AsReadOnly();
			ErrorMessage = errorMessage ?? string.Empty;
		}
	}

	private sealed class DamageTargetSummary
	{
		public IUnitInstance Target { get; }
		public int HitCount { get; set; }
		public int TotalDamage { get; set; }
		public int TotalShieldAbsorbed { get; set; }
		public int TotalHpDamage { get; set; }
		public int TargetShieldBefore { get; set; }
		public int TargetShieldAfter { get; set; }
		public int TargetHpBefore { get; set; }
		public int TargetHpAfter { get; set; }

		public DamageTargetSummary(IUnitInstance target, EffectResult effectResult)
		{
			Target = target;
			HitCount = 1;
			TotalDamage = effectResult?.TotalValue ?? 0;
			TotalShieldAbsorbed = effectResult?.ShieldAbsorbed ?? 0;
			TotalHpDamage = effectResult?.HpDamage ?? 0;
			TargetShieldBefore = effectResult?.TargetShieldBefore ?? target?.Shield ?? 0;
			TargetShieldAfter = effectResult?.TargetShieldAfter ?? target?.Shield ?? 0;
			TargetHpBefore = effectResult?.TargetHpBefore ?? target?.HP ?? 0;
			TargetHpAfter = effectResult?.TargetHpAfter ?? target?.HP ?? 0;
		}
	}

	private sealed class ShieldTargetSummary
	{
		public IUnitInstance Target { get; }
		public int HitCount { get; set; }
		public int TotalShieldGained { get; set; }
		public int TargetShieldBefore { get; set; }
		public int TargetShieldAfter { get; set; }

		public ShieldTargetSummary(IUnitInstance target, EffectResult effectResult)
		{
			Target = target;
			HitCount = 1;
			TotalShieldGained = effectResult?.ShieldGained ?? 0;
			TargetShieldBefore = effectResult?.SourceShieldBefore ?? target?.Shield ?? 0;
			TargetShieldAfter = effectResult?.SourceShieldAfter ?? target?.Shield ?? 0;
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
	public string SourceDeckCardUniqueInGameId { get; set; } = string.Empty; // 默认卡组中的来源卡牌实例ID
	
	[Export]
	public int EnergyCost { get; set; } = 0; // 消耗能量

	// 运行时动态费用委托（实例级）：用于实现"按本局失去生命次数降费"等机制。
	// 返回 null/异常时回退到 EnergyCost + 静态工厂。
	// 签名取 IUnitInstance（而非 CharacterInstance）以便单测可传入 TestUnitInstance 桩。
	public System.Func<IUnitInstance, int> EnergyCostOverride { get; set; } = null;

	// 静态工厂：按 CardId 注册的动态费用计算器，签名为 (player) => cost。
	// 死亡之舞 (11002010) 用此机制按本局失去生命次数降费。
	private static readonly System.Collections.Generic.Dictionary<int, System.Func<IUnitInstance, int>> CostOverrideFactories
		= new System.Collections.Generic.Dictionary<int, System.Func<IUnitInstance, int>>();

	public static void RegisterCostOverrideFactory(int cardId, System.Func<IUnitInstance, int> factory)
	{
		if (factory == null) { CostOverrideFactories.Remove(cardId); return; }
		CostOverrideFactories[cardId] = factory;
	}

	// 计算当前实际消耗费用：实例 override → 静态工厂 → EnergyCost。
	public int GetCurrentEnergyCost(IUnitInstance currentPlayer = null)
	{
		try
		{
			if (EnergyCostOverride != null && currentPlayer != null)
			{
				return EnergyCostOverride.Invoke(currentPlayer);
			}
		}
		catch { /* fall through */ }

		try
		{
			if (currentPlayer != null && CostOverrideFactories.TryGetValue(CardId, out var factory))
			{
				return factory.Invoke(currentPlayer);
			}
		}
		catch { /* fall through */ }

		return EnergyCost;
	}

	[Export]
	public CardCategory Category { get; set; } // 卡牌种类（通过枚举限定）

	// 效果类型列表，支持多效果（CSV中用"|"分隔）
	public EffectType[] EffectTypes { get; set; } = Array.Empty<EffectType>();
	
	[Export(PropertyHint.MultilineText)]
	public string EffectDescription { get; set; } = string.Empty; // 效果描述
	
	[Export]
	public bool NeedTarget { get; set; } = false; // 是否需要目标

	[Export]
	public CardKeyWord CardKeyWord { get; set; } = CardKeyWord.None; // 卡牌关键词（CSV固有）

	public List<AppliedKeywordEntry> AppliedKeywords { get; set; } = new List<AppliedKeywordEntry>(); // 运行时附加关键词

	public CardConditionType[] ConditionParams { get; set; } = Array.Empty<CardConditionType>();

	[Export]
	public int PermanentUpgradeLevel { get; set; } = 0; // 默认卡组中的永久升级级数

	[Export]
	public int BattleUpgradeLevel { get; set; } = 0; // 当前战斗中的升级级数

	public int TotalUpgradeLevel => PermanentUpgradeLevel + BattleUpgradeLevel;

	// 各效果对应的参数，Params[i][0] 固定表示目标类型，后续参数为该效果自身参数
	public int[][] Params { get; set; } = Array.Empty<int[]>();

	// 构造函数（Godot Resource需保留无参构造）
	public Card() { }

	// 带参数的构造函数，NeedTarget 自动从 cardParams 推导
	public Card(int cardId, string uniqueInGameId, int energyCost, CardCategory category, EffectType[] effectTypes, string effectDesc, int[][] cardParams = null, string cardName = "", CardKeyWord cardKeyWord = CardKeyWord.None, CardConditionType[] conditionParams = null)
	{
		CardId = cardId;
		CardName = cardName;
		UniqueInGameId = uniqueInGameId;
		SourceDeckCardUniqueInGameId = string.Empty;
		EnergyCost = energyCost;
		Category = category;
		EffectTypes = effectTypes ?? Array.Empty<EffectType>();
		EffectDescription = effectDesc;
		Params = cardParams ?? Array.Empty<int[]>();
		NeedTarget = DeriveNeedTarget(Params);
		CardKeyWord = cardKeyWord;
		ConditionParams = conditionParams ?? Array.Empty<CardConditionType>();
		PermanentUpgradeLevel = 0;
		BattleUpgradeLevel = 0;
	}

	public bool HasKeyWord(CardKeyWord keyWord)
	{
		if ((CardKeyWord & keyWord) == keyWord)
			return true;
		foreach (AppliedKeywordEntry entry in AppliedKeywords)
		{
			if (entry.Keyword == keyWord)
				return true;
		}
		return false;
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
		Card card = new Card(CardId, string.Empty, EnergyCost, Category, EffectTypes, EffectDescription, Params, CardName, CardKeyWord, ConditionParams);
		card.GenerateUniqueInGameId();
		return card;
	}

	public virtual Card CreateDeckInstance()
	{
		Card card = new Card(CardId, string.Empty, EnergyCost, Category, EffectTypes, EffectDescription, Params, CardName, CardKeyWord, ConditionParams);
		card.GenerateUniqueInGameId();
		card.SourceDeckCardUniqueInGameId = card.UniqueInGameId;
		return card;
	}

	public virtual Card CreateBattleInstanceFromDeckCard()
	{
		Card card = new Card(CardId, string.Empty, EnergyCost, Category, EffectTypes, EffectDescription, Params, CardName, CardKeyWord, ConditionParams);
		card.GenerateUniqueInGameId();
		card.SourceDeckCardUniqueInGameId = string.IsNullOrWhiteSpace(SourceDeckCardUniqueInGameId) ? UniqueInGameId : SourceDeckCardUniqueInGameId;
		card.PermanentUpgradeLevel = PermanentUpgradeLevel;
		card.BattleUpgradeLevel = 0;
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
		int accumulatedShield = 0;

		for (int i = 0; i < EffectTypes.Length; i++)
		{
			int[] rawEffectParams = (Params != null && i < Params.Length) ? Params[i] : Array.Empty<int>();
			EffectType effectType = EffectTypes[i];
			if (IsCardOperationEffect(effectType))
			{
				// 卡牌操作效果不产生即时结算，保留上一条真实效果（如 Damage）的 EffectResult，
				// 供 DidApplyResultKillTarget（淬炼击杀检测）使用；若前面无真实效果则给空结果兜底。
				lastResult ??= new CardApplyResult(true, this, source, target);
				continue;
			}

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
					case EffectType.DamageByBattleLostHp:
						result = ApplyDamageByBattleLostHpEffect(source, resolvedTargets, effectArgs);
						break;
				case EffectType.Shield:
					result = ApplyShieldEffect(source, resolvedTargets, effectArgs);
					if (result.EffectResult != null)
						accumulatedShield += result.EffectResult.ShieldGained;
					break;
				case EffectType.AddState:
					result = ApplyAddStateEffect(source, target, resolvedTargets, effectArgs);
					break;
				case EffectType.HpLoss:
					result = ApplyHpLossEffect(source, resolvedTargets, effectArgs);
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
				case EffectType.AddKeyword:
					// AddKeyword 走 CardPlayController.TryExecuteCardOperations 选牌流程（在 PlayHandCard 中处理），
					// 避免在 Card.Apply 阶段直接调 GetCardsForCardOperation 把"选"当"随机"。
					result = new CardApplyResult(true, this, source, source);
					break;
				case EffectType.MirrorShieldToAllies:
					result = ApplyMirrorShieldToAlliesEffect(source, resolvedTargets, effectArgs, accumulatedShield);
					break;
				case EffectType.RearrangeMonsterTargets:
					result = ApplyRearrangeMonsterTargetsEffect(source, resolvedTargets);
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

	private static bool IsCardOperationEffect(EffectType effectType)
	{
		return effectType == EffectType.UpgradeBattleCard || effectType == EffectType.UpgradePermanentCard || effectType == EffectType.AddKeyword;
	}

	private CardApplyResult ApplyDamageEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		int hitCount = GetDamageHitCount(effectArgs);
		int[] finalEffectArgs = BuildDamageEffectArguments(GetDamageBaseArguments(effectArgs));
		List<EffectResult> effectResults = new List<EffectResult>();
		Dictionary<int, DamageTargetSummary> targetSummaries = new Dictionary<int, DamageTargetSummary>();
		EffectResult lastEffectResult = null;
		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
			{
			lastEffectResult = EffectSystem.ApplyAttack(source, resolvedTarget, finalEffectArgs, card: this);
				effectResults.Add(lastEffectResult);
				AccumulateDamageSummary(targetSummaries, resolvedTarget, lastEffectResult);
			}
		}

		if (effectResults.Count > 1)
		{
			lastEffectResult = BuildAggregatedDamageEffectResult(source, effectResults, targetSummaries);
		}

		return new CardApplyResult(true, this, source, lastTarget, lastEffectResult);
	}

	private CardApplyResult ApplyDamageByBattleLostHpEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		int baseExtraDamage = effectArgs != null && effectArgs.Length > 0 ? effectArgs[0] : 0;
		int battleLostHp = BattleSytem.Current?.GetBattleLostHp(source) ?? 0;
		int totalExtraDamage = baseExtraDamage + battleLostHp;
		return ApplyDamageEffect(source, resolvedTargets, new int[] { totalExtraDamage });
	}

	private int[] BuildDamageEffectArguments(int[] effectArgs)
	{
		if (effectArgs == null || effectArgs.Length == 0)
		{
			return Array.Empty<int>();
		}

		int[] result = new int[effectArgs.Length];
		Array.Copy(effectArgs, result, effectArgs.Length);

		if (!HasKeyWord(CardKeyWord.InfiniteUpgrade) || TotalUpgradeLevel <= 0)
		{
			return result;
		}

		double scaledExtraDamage = result[0] * Math.Pow(1.2d, TotalUpgradeLevel);
		result[0] = (int)Math.Floor(scaledExtraDamage);
		return result;
	}

	private static int[] GetDamageBaseArguments(int[] effectArgs)
	{
		if (effectArgs == null || effectArgs.Length == 0)
		{
			return Array.Empty<int>();
		}

		if (effectArgs.Length == 1)
		{
			return (int[])effectArgs.Clone();
		}

		int[] result = new int[effectArgs.Length - 1];
		Array.Copy(effectArgs, result, result.Length);
		return result;
	}

	private static int GetDamageHitCount(int[] effectArgs)
	{
		if (effectArgs == null || effectArgs.Length <= 1)
		{
			return 1;
		}

		return Math.Max(1, effectArgs[effectArgs.Length - 1]);
	}

	private static void AccumulateDamageSummary(Dictionary<int, DamageTargetSummary> targetSummaries, IUnitInstance target, EffectResult effectResult)
	{
		if (target == null || effectResult == null)
		{
			return;
		}

		if (!targetSummaries.TryGetValue(target.UniqueInGameId, out DamageTargetSummary summary))
		{
			targetSummaries[target.UniqueInGameId] = new DamageTargetSummary(target, effectResult);
			return;
		}

		summary.HitCount++;
		summary.TotalDamage += effectResult.TotalValue;
		summary.TotalShieldAbsorbed += effectResult.ShieldAbsorbed;
		summary.TotalHpDamage += effectResult.HpDamage;
		summary.TargetShieldAfter = effectResult.TargetShieldAfter;
		summary.TargetHpAfter = effectResult.TargetHpAfter;
	}

	private EffectResult BuildAggregatedDamageEffectResult(IUnitInstance source, List<EffectResult> effectResults, Dictionary<int, DamageTargetSummary> targetSummaries)
	{
		int totalDamage = 0;
		int totalShieldAbsorbed = 0;
		int totalHpDamage = 0;
		foreach (EffectResult effectResult in effectResults)
		{
			if (effectResult == null)
			{
				continue;
			}

			totalDamage += effectResult.TotalValue;
			totalShieldAbsorbed += effectResult.ShieldAbsorbed;
			totalHpDamage += effectResult.HpDamage;
		}

		StringBuilder summaryBuilder = new StringBuilder();
		summaryBuilder.Append($"来源={GetUnitLabel(source)}，受击单位数={targetSummaries.Count}，总命中次数={effectResults.Count}，总伤害={totalDamage}，护盾抵扣={totalShieldAbsorbed}，HP伤害={totalHpDamage}");
		if (targetSummaries.Count > 0)
		{
			summaryBuilder.Append("。目标详情：");
			bool isFirst = true;
			foreach (DamageTargetSummary summary in targetSummaries.Values)
			{
				if (!isFirst)
				{
					summaryBuilder.Append("；");
				}

				summaryBuilder.Append($"{GetUnitLabel(summary.Target)} 受击{summary.HitCount}次，护盾 {summary.TargetShieldBefore}->{summary.TargetShieldAfter}，HP {summary.TargetHpBefore}->{summary.TargetHpAfter}，护盾抵扣={summary.TotalShieldAbsorbed}，HP伤害={summary.TotalHpDamage}");
				isFirst = false;
			}
		}

		return new EffectResult(
			"Attack",
			source,
			null,
			summaryOverride: summaryBuilder.ToString(),
			totalValue: totalDamage,
			shieldAbsorbed: totalShieldAbsorbed,
			hpDamage: totalHpDamage);
	}

	private CardApplyResult ApplyShieldEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		int shieldCount = GetShieldRepeatCount(effectArgs);
		int[] finalEffectArgs = GetShieldBaseArguments(effectArgs);
		List<EffectResult> effectResults = new List<EffectResult>();
		Dictionary<int, ShieldTargetSummary> targetSummaries = new Dictionary<int, ShieldTargetSummary>();
		EffectResult lastEffectResult = null;
		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			for (int shieldIndex = 0; shieldIndex < shieldCount; shieldIndex++)
			{
				lastEffectResult = EffectSystem.ApplyShield(resolvedTarget, finalEffectArgs);
				effectResults.Add(lastEffectResult);
				AccumulateShieldSummary(targetSummaries, resolvedTarget, lastEffectResult);
			}
		}

		if (effectResults.Count > 1)
		{
			lastEffectResult = BuildAggregatedShieldEffectResult(source, effectResults, targetSummaries);
		}

		return new CardApplyResult(true, this, source, lastTarget, lastEffectResult);
	}

	private CardApplyResult ApplyHpLossEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		int[] finalEffectArgs = GetShieldBaseArguments(effectArgs);
		int hpLoss = finalEffectArgs.Length > 0 ? finalEffectArgs[0] : 0;
		List<EffectResult> effectResults = new List<EffectResult>();
		IUnitInstance lastTarget = null;
		EffectResult lastEffectResult = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			lastEffectResult = EffectSystem.ApplyHpLoss(resolvedTarget, hpLoss);
			effectResults.Add(lastEffectResult);
		}
		return new CardApplyResult(true, this, source, lastTarget, lastEffectResult);
	}

	private static int[] GetShieldBaseArguments(int[] effectArgs)
	{
		if (effectArgs == null || effectArgs.Length == 0)
		{
			return Array.Empty<int>();
		}

		if (effectArgs.Length == 1)
		{
			return (int[])effectArgs.Clone();
		}

		int[] result = new int[effectArgs.Length - 1];
		Array.Copy(effectArgs, result, result.Length);
		return result;
	}

	private static int GetShieldRepeatCount(int[] effectArgs)
	{
		if (effectArgs == null || effectArgs.Length <= 1)
		{
			return 1;
		}

		return Math.Max(1, effectArgs[effectArgs.Length - 1]);
	}

	private static void AccumulateShieldSummary(Dictionary<int, ShieldTargetSummary> targetSummaries, IUnitInstance target, EffectResult effectResult)
	{
		if (target == null || effectResult == null)
		{
			return;
		}

		if (!targetSummaries.TryGetValue(target.UniqueInGameId, out ShieldTargetSummary summary))
		{
			targetSummaries[target.UniqueInGameId] = new ShieldTargetSummary(target, effectResult);
			return;
		}

		summary.HitCount++;
		summary.TotalShieldGained += effectResult.ShieldGained;
		summary.TargetShieldAfter = effectResult.SourceShieldAfter;
	}

	private EffectResult BuildAggregatedShieldEffectResult(IUnitInstance source, List<EffectResult> effectResults, Dictionary<int, ShieldTargetSummary> targetSummaries)
	{
		int totalShieldGained = 0;
		foreach (EffectResult effectResult in effectResults)
		{
			if (effectResult == null)
			{
				continue;
			}

			totalShieldGained += effectResult.ShieldGained;
		}

		StringBuilder summaryBuilder = new StringBuilder();
		summaryBuilder.Append($"来源={GetUnitLabel(source)}，受护盾单位数={targetSummaries.Count}，总防御次数={effectResults.Count}，总获得护盾={totalShieldGained}");
		if (targetSummaries.Count > 0)
		{
			summaryBuilder.Append("。目标详情：");
			bool isFirst = true;
			foreach (ShieldTargetSummary summary in targetSummaries.Values)
			{
				if (!isFirst)
				{
					summaryBuilder.Append("；");
				}

				summaryBuilder.Append($"{GetUnitLabel(summary.Target)} 防御{summary.HitCount}次，护盾 {summary.TargetShieldBefore}->{summary.TargetShieldAfter}，总获得护盾={summary.TotalShieldGained}");
				isFirst = false;
			}
		}

		return new EffectResult(
			"Shield",
			source,
			null,
			summaryOverride: summaryBuilder.ToString(),
			totalValue: totalShieldGained,
			shieldGained: totalShieldGained);
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

		TurnStartResourceType turnStartResource = TurnStartResourceType.None;
		int turnStartAmount = 1;
		if (stateType == StateType.TurnStartEffect && effectArgs.Length > 2)
		{
			turnStartResource = (TurnStartResourceType)effectArgs[2];
			turnStartAmount = effectArgs.Length > 3 ? effectArgs[3] : 1;
		}

		IUnitInstance lastTarget = null;
		foreach (IUnitInstance resolvedTarget in resolvedTargets)
		{
			lastTarget = resolvedTarget;
			StateSystem.AddOrUpdateState(resolvedTarget, stateType, stacks, turnStartResource, turnStartAmount, source);
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

		if (resolvedTargets.Count == 0)
		{
			resolvedTargets = new List<IUnitInstance> { source };
		}

		foreach (IUnitInstance target in resolvedTargets)
		{
			if (target is CharacterInstance character)
			{
				if (StateSystem.TryGetStateStacks(character, StateType.DrawLock, out int _))
				{
					AppendConsoleInfo($"{GetUnitLabel(character)} 受 DrawLock 影响，跳过卡牌效果抽牌。");
					continue;
				}

				lastTarget = character;
				int drawn = 0;
				for (int i = 0; i < drawCount; i++)
				{
					if (character.drawpile.Count == 0)
					{
						if (character.discardpile.Count == 0)
						{
							break;
						}

						character.drawpile.AddRange(character.discardpile);
						character.discardpile.Clear();
						ShuffleList(character.drawpile);
						AppendConsoleInfo("抽牌堆为空：已将弃牌堆随机洗牌后放回抽牌堆。");
					}

					character.handcards.Add(character.drawpile[0]);
					character.drawpile.RemoveAt(0);
					drawn++;
				}

				AppendConsoleInfo($"{GetUnitLabel(character)} 抽取 {drawn} 张牌");
			}
		}

		return new CardApplyResult(true, this, source, lastTarget ?? source);
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

	private CardApplyResult ApplyAddKeywordEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs)
	{
		if (effectArgs.Length < 2)
		{
			string errorMessage = $"卡牌ID {CardId} 的 AddKeyword 参数不足，需要 CardOperationTargetType 和 keywordName。";
			AppendConsoleError(errorMessage, true);
			return new CardApplyResult(false, this, source, null, errorMessage: errorMessage);
		}

		int rawTargetType = effectArgs[0];
		CardOperationTargetType cardOpTargetType = Enum.IsDefined(typeof(CardOperationTargetType), rawTargetType)
			? (CardOperationTargetType)rawTargetType
			: CardOperationTargetType.None;

		int rawKeyword = effectArgs[1];
		if (!Enum.IsDefined(typeof(CardKeyWord), rawKeyword))
		{
			string errorMessage = $"卡牌ID {CardId} 的 AddKeyword 中 CardKeyWord 值 {rawKeyword} 无效。";
			AppendConsoleError(errorMessage, true);
			return new CardApplyResult(false, this, source, null, errorMessage: errorMessage);
		}

		CardKeyWord keyword = (CardKeyWord)rawKeyword;
		KeywordFlag keywordFlag = effectArgs.Length > 2 ? (KeywordFlag)effectArgs[2] : KeywordFlag.None;

		int count = effectArgs.Length > 3 ? effectArgs[3] : 1;

		if (source is CharacterInstance character)
		{
			List<Card> targetCards = BattleSytem.Current?.GetCardsForCardOperation(character, cardOpTargetType, count) ?? new List<Card>();
			foreach (Card targetCard in targetCards)
			{
				targetCard.AppliedKeywords.Add(new AppliedKeywordEntry { Keyword = keyword, Flags = keywordFlag });
			}

			AppendConsoleInfo($"{GetUnitLabel(source)} 为 {targetCards.Count} 张卡牌添加了关键词 {keyword} (Flags={keywordFlag})");
		}

		return new CardApplyResult(true, this, source, source);
	}

	private CardApplyResult ApplyRearrangeMonsterTargetsEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets)
	{
		// 效果：将所有怪物的单攻目标重定向到 source，每改一个目标 source 获得 1 点护盾。
		if (source is not CharacterInstance caster)
		{
			AppendConsoleError($"到我身后：source 必须是玩家角色，actual={source?.GetType().Name}", true);
			return new CardApplyResult(false, this, source, source, errorMessage: "RearrangeMonsterTargets 仅对玩家有效。");
		}

		BattleSytem battle = BattleSytem.Current;
		if (battle?.Monsters == null)
		{
			return new CardApplyResult(true, this, source, source);
		}

		int rearrangedCount = 0;
		foreach (MonsterInstance monster in battle.Monsters.Values)
		{
			if (monster == null || monster.HP <= 0) continue;
			// 只对"单攻意图"的怪物生效（SelectedIntention 里有 Damage 段）
			bool hasDamage = false;
			if (monster.SelectedIntention != null)
			{
				foreach (int[] effect in monster.SelectedIntention)
				{
					if (effect != null && effect.Length > 0 && (EffectType)effect[0] == EffectType.Damage)
					{
						hasDamage = true;
						break;
					}
				}
			}
			if (!hasDamage) continue;
			if (monster.SelectedIntentionTargetUniqueInGameId != caster.UniqueInGameId)
			{
				monster.SetSelectedIntentionTarget(caster.UniqueInGameId);
				rearrangedCount++;
			}
		}

		if (rearrangedCount > 0)
		{
			EffectSystem.ApplyShield(caster, new int[] { rearrangedCount });
		}
		AppendConsoleInfo($"{GetUnitLabel(caster)} 施展 '到我身后'：重定向 {rearrangedCount} 个怪物单攻目标，获得 {rearrangedCount} 点护盾。");

		return new CardApplyResult(true, this, source, source);
	}

	private CardApplyResult ApplyMirrorShieldToAlliesEffect(IUnitInstance source, List<IUnitInstance> resolvedTargets, int[] effectArgs, int priorShield = 0)
	{
		if (resolvedTargets.Count == 0)
		{
			return new CardApplyResult(true, this, source, source);
		}

		int totalShield = priorShield > 0 ? priorShield : (effectArgs.Length > 0 && effectArgs[0] > 0 ? effectArgs[0] : 0);

		foreach (IUnitInstance ally in resolvedTargets)
		{
			EffectSystem.ApplyDistributeShield(ally, totalShield);
		}

		AppendConsoleInfo($"{GetUnitLabel(source)} 将所有友方单位护盾增加 {totalShield}（前序累积护盾）");

		return new CardApplyResult(true, this, source, resolvedTargets[resolvedTargets.Count - 1]);
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
			case EffectTargetType.AllAllies:
				targets.AddRange(BattleSytem.Current?.GetAllyUnits(source) ?? new List<IUnitInstance>());
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
		SceneConsoleRouter.AppendRaw(message, alsoPrintError);
	}
}
