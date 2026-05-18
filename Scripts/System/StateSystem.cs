using System;
using System.Collections.Generic;
using CardSimulator;

public sealed class StateRuntimeData
{
	public StateType Type { get; }
	public int AppliedOrder { get; }
	public int Stacks { get; set; }
	public List<StateStackSegment> StackSegments { get; } = new List<StateStackSegment>();

	public StateRuntimeData(StateType type, int stacks, int appliedOrder)
	{
		Type = type;
		AppliedOrder = appliedOrder;
		Stacks = Math.Max(0, stacks);
		if (Stacks > 0)
		{
			StackSegments.Add(new StateStackSegment(Stacks));
		}
	}

	public void AddAnonymousStacks(int stacks)
	{
		int normalizedStacks = Math.Max(0, stacks);
		if (normalizedStacks <= 0)
		{
			return;
		}

		Stacks += normalizedStacks;
		StackSegments.Add(new StateStackSegment(normalizedStacks));
	}

	public bool RegisterEndCallbackForLatestStacks(int stacks, string stateCardUniqueInGameId, IUnitInstance ownerUnit)
	{
		int remaining = Math.Max(0, stacks);
		if (remaining <= 0)
		{
			return false;
		}

		for (int index = StackSegments.Count - 1; index >= 0 && remaining > 0; index--)
		{
			StateStackSegment segment = StackSegments[index];
			if (segment == null || segment.RemainingStacks <= 0 || segment.NeedCallback)
			{
				continue;
			}

			if (segment.RemainingStacks > remaining)
			{
				int leftoverStacks = segment.RemainingStacks - remaining;
				segment.RemainingStacks = remaining;
				StackSegments.Insert(index + 1, new StateStackSegment(leftoverStacks));
			}

			segment.NeedCallback = true;
			segment.StateCardUniqueInGameId = stateCardUniqueInGameId ?? string.Empty;
			segment.OwnerUnit = ownerUnit;
			remaining -= segment.RemainingStacks;
		}

		return remaining == 0;
	}

	public void AddCallbackOnlySegment(string stateCardUniqueInGameId, IUnitInstance ownerUnit)
	{
		StackSegments.Add(new StateStackSegment(0, true, stateCardUniqueInGameId, ownerUnit));
	}

	public StateEndedContext ConsumeOneStack(IUnitInstance targetUnit, StateEndReason endReason)
	{
		if (Stacks <= 0 || StackSegments.Count == 0)
		{
			Stacks = 0;
			StackSegments.Clear();
			return null;
		}

		StateStackSegment segment = StackSegments[0];
		segment.RemainingStacks = Math.Max(0, segment.RemainingStacks - 1);
		Stacks = Math.Max(0, Stacks - 1);

		if (segment.RemainingStacks > 0)
		{
			return null;
		}

		StackSegments.RemoveAt(0);
		if (!segment.NeedCallback)
		{
			return null;
		}

		return new StateEndedContext(targetUnit, Type, segment.StateCardUniqueInGameId, segment.OwnerUnit, endReason, segment.NeedCallback);
	}

	public List<StateEndedContext> ConsumeAllCallbacks(IUnitInstance targetUnit, StateEndReason endReason)
	{
		List<StateEndedContext> callbacks = new List<StateEndedContext>();
		for (int index = 0; index < StackSegments.Count; index++)
		{
			StateStackSegment segment = StackSegments[index];
			if (segment == null || !segment.NeedCallback)
			{
				continue;
			}

			callbacks.Add(new StateEndedContext(targetUnit, Type, segment.StateCardUniqueInGameId, segment.OwnerUnit, endReason, true));
		}

		StackSegments.Clear();
		Stacks = 0;
		return callbacks;
	}
}

public sealed class StateStackSegment
{
	public int RemainingStacks { get; set; }
	public bool NeedCallback { get; set; }
	public string StateCardUniqueInGameId { get; set; }
	public IUnitInstance OwnerUnit { get; set; }

	public StateStackSegment(int remainingStacks, bool needCallback = false, string stateCardUniqueInGameId = "", IUnitInstance ownerUnit = null)
	{
		RemainingStacks = Math.Max(0, remainingStacks);
		NeedCallback = needCallback;
		StateCardUniqueInGameId = stateCardUniqueInGameId ?? string.Empty;
		OwnerUnit = ownerUnit;
	}
}

public static class StateSystem
{
	private static int nextAppliedOrder = 0;

	public static bool IsStackable(StateType type)
	{
		StateDefinition definition = GetStateDefinition(type);
		return definition != null && definition.IsStackable;
	}

	public static bool IsPermanent(StateType type)
	{
		StateDefinition definition = GetStateDefinition(type);
		return definition == null || definition.IsPermanent;
	}

	public static bool IsDebuff(StateType type)
	{
		StateDefinition definition = GetStateDefinition(type);
		return definition != null && definition.IsDebuff;
	}

	public static bool IsElite(StateType type)
	{
		StateDefinition definition = GetStateDefinition(type);
		return definition != null && definition.IsElite;
	}

	public static bool IsNormalDebuff(StateType type)
	{
		StateDefinition definition = GetStateDefinition(type);
		return definition != null && definition.IsDebuff && !definition.IsElite;
	}

	public static void AddOrUpdateState(IUnitInstance unit, StateType type, int stacks)
	{
		if (unit == null)
		{
			throw new ArgumentNullException(nameof(unit));
		}

		if (type == StateType.None || stacks <= 0)
		{
			return;
		}

		Dictionary<StateType, StateRuntimeData> states = unit.States;
		if (states.TryGetValue(type, out StateRuntimeData existing))
		{
			if (IsStackable(type))
			{
				existing.AddAnonymousStacks(stacks);
			}
			else
			{
				existing.Stacks = 1;
				// 不可叠层状态重复添加时，保留历史回调段，避免旧状态牌丢失回收机会。
				existing.StackSegments.RemoveAll(segment => segment != null && segment.RemainingStacks > 0 && !segment.NeedCallback);
				existing.StackSegments.Insert(0, new StateStackSegment(1));
			}
			return;
		}

		int initialStacks = IsStackable(type) ? stacks : 1;
		states[type] = new StateRuntimeData(type, initialStacks, System.Threading.Interlocked.Increment(ref nextAppliedOrder));
	}

	public static bool RegisterStateEndCallback(IUnitInstance unit, StateType type, int stacks, string stateCardUniqueInGameId, IUnitInstance ownerUnit)
	{
		if (unit == null)
		{
			throw new ArgumentNullException(nameof(unit));
		}

		if (!unit.States.TryGetValue(type, out StateRuntimeData stateData) || stateData == null)
		{
			return false;
		}

		if (!IsStackable(type) && HasAnyBoundCallback(stateData))
		{
			// 不可叠层状态再次打出时，补充额外回调绑定，状态结束时回收所有对应状态牌。
			stateData.AddCallbackOnlySegment(stateCardUniqueInGameId, ownerUnit);
			return true;
		}

		return stateData.RegisterEndCallbackForLatestStacks(stacks, stateCardUniqueInGameId, ownerUnit);
	}

	public static void RemoveState(IUnitInstance unit, StateType type)
	{
		if (unit == null)
		{
			throw new ArgumentNullException(nameof(unit));
		}

		if (!unit.States.TryGetValue(type, out StateRuntimeData stateData) || stateData == null)
		{
			return;
		}

		List<StateEndedContext> callbacks = stateData.ConsumeAllCallbacks(unit, StateEndReason.Cleared);
		unit.States.Remove(type);
		InvokeStateEndedCallbacks(unit, callbacks);
	}

	public static int RemoveStateStacks(IUnitInstance unit, StateType type, int stacks)
	{
		if (unit == null)
		{
			throw new ArgumentNullException(nameof(unit));
		}

		if (stacks <= 0)
		{
			return 0;
		}

		if (!unit.States.TryGetValue(type, out StateRuntimeData stateData) || stateData == null)
		{
			return 0;
		}

		int removedStacks = 0;
		List<StateEndedContext> callbacks = new List<StateEndedContext>();
		while (removedStacks < stacks && stateData.Stacks > 0)
		{
			StateEndedContext endedContext = stateData.ConsumeOneStack(unit, StateEndReason.Cleared);
			if (endedContext != null)
			{
				callbacks.Add(endedContext);
			}

			removedStacks++;
		}

		if (stateData.Stacks <= 0)
		{
			callbacks.AddRange(stateData.ConsumeAllCallbacks(unit, StateEndReason.Cleared));
			unit.States.Remove(type);
		}

		InvokeStateEndedCallbacks(unit, callbacks);
		return removedStacks;
	}

	public static bool TryRemoveFirstNormalDebuff(IUnitInstance unit, out StateType removedStateType)
	{
		removedStateType = StateType.None;
		if (!TryRemoveFirstNormalDebuffs(unit, 1, out List<StateType> removedStateTypes) || removedStateTypes.Count == 0)
		{
			return false;
		}

		removedStateType = removedStateTypes[0];
		return true;
	}

	public static bool TryRemoveFirstNormalDebuffs(IUnitInstance unit, int removeCount, out List<StateType> removedStateTypes)
	{
		removedStateTypes = new List<StateType>();
		if (removeCount <= 0 || unit == null || unit.States == null || unit.States.Count == 0)
		{
			return false;
		}

		List<StateType> orderedStateTypes = GetOrderedStateTypes(unit);
		for (int index = 0; index < orderedStateTypes.Count && removedStateTypes.Count < removeCount; index++)
		{
			StateType stateType = orderedStateTypes[index];
			if (!IsNormalDebuff(stateType))
			{
				continue;
			}

			RemoveState(unit, stateType);
			removedStateTypes.Add(stateType);
		}

		return removedStateTypes.Count > 0;
	}

	public static List<StateType> GetOrderedStateTypes(IUnitInstance unit)
	{
		List<StateType> orderedStateTypes = new List<StateType>();
		if (unit == null || unit.States == null || unit.States.Count == 0)
		{
			return orderedStateTypes;
		}

		orderedStateTypes.AddRange(unit.States.Keys);
		orderedStateTypes.Sort((left, right) => CompareStateDisplayOrder(unit, left, right));
		return orderedStateTypes;
	}

	public static int ModifyIncomingDamage(IUnitInstance source, IUnitInstance target, int baseDamage)
	{
		if (target == null)
		{
			throw new ArgumentNullException(nameof(target));
		}

		int damage = Math.Max(0, baseDamage);
		if (damage == 0)
		{
			return 0;
		}

		if (TryGetStateStacks(target, StateType.Vulnerable, out int vulnerableStacks) && vulnerableStacks > 0)
		{
			// 易伤：受到伤害 +50%，按统一规则向下取整
			damage = FloorByRule(damage * 1.5d);
		}

		if (source != null && TryGetStateStacks(source, StateType.Weak, out int weakStacks) && weakStacks > 0)
		{
			// 虚弱：造成的攻击伤害 -25%，按统一规则向下取整
			damage = FloorByRule(damage * 0.75d);
		}

		if (source != null && TryGetStateStacks(source, StateType.AddAttack, out int AddAttackStacks) && AddAttackStacks > 0)
		{
			damage += AddAttackStacks;
		}

		return damage;
	}

	public static int ModifyAddedEnergy(IUnitInstance source, int baseEnergy)
	{
		if (source == null)
		{
			throw new ArgumentNullException(nameof(source));
		}

		int energy = Math.Max(0, baseEnergy);
		if (energy == 0)
		{
			return 0;
		}

		// 后续可在此添加其他能量修改状态：
		// - 加法修改（固定提升）
		// - 乘法修改（百分比提升）
		// 示例：
		// if (TryGetStateStacks(source, StateType.Charged, out int chargedStacks) && chargedStacks > 0)
		// {
		//     energy += chargedStacks * 2;  // 每层固定+2点能量
		// }
		//
		// if (TryGetStateStacks(source, StateType.EnergyAmplify, out int amplifyStacks) && amplifyStacks > 0)
		// {
		//     energy = FloorByRule(energy * (1.0 + amplifyStacks * 0.1));  // 每层+10%
		// }

		return energy;
	}

	public static void OnCardPlayed(IUnitInstance unit, Card card)
	{
		if (unit == null)
		{
			throw new ArgumentNullException(nameof(unit));
		}

		if (card == null || card.Category != CardCategory.Attack)
		{
			return;
		}

		if (!TryGetStateStacks(unit, StateType.CourageArmor, out int courageArmorStacks) || courageArmorStacks <= 0)
		{
			return;
		}

		EffectResult shieldResult = EffectSystem.ApplyShield(unit);
		if (shieldResult != null)
		{
			AppendStateCardPlayedInfo(unit, StateType.CourageArmor, card, shieldResult);
		}
	}

	private static int FloorByRule(double value)
	{
		if (value >= 0)
		{
			return (int)Math.Floor(value);
		}

		// 负数按绝对值向下取整后再恢复符号，避免 -1.2 被取整为 -2。
		return -(int)Math.Floor(Math.Abs(value));
	}

	public static void OnTurnStart(IUnitInstance unit)
	{
		if (unit == null)
		{
			throw new ArgumentNullException(nameof(unit));
		}

		if (unit.States.Count == 0)
		{
			return;
		}

		List<StateType> toRemove = new List<StateType>();
		List<StateEndedContext> callbacks = new List<StateEndedContext>();
		foreach (KeyValuePair<StateType, StateRuntimeData> pair in unit.States)
		{
			int decayAmount = GetTurnStartDecayAmount(pair.Key);
			if (decayAmount <= 0)
			{
				continue;
			}

			for (int count = 0; count < decayAmount && pair.Value.Stacks > 0; count++)
			{
				StateEndedContext endedContext = pair.Value.ConsumeOneStack(unit, StateEndReason.Expired);
				if (endedContext != null)
				{
					callbacks.Add(endedContext);
				}
			}

			if (pair.Value.Stacks <= 0)
			{
				callbacks.AddRange(pair.Value.ConsumeAllCallbacks(unit, StateEndReason.Expired));
				toRemove.Add(pair.Key);
			}
		}

		if (TryGetStateStacks(unit, StateType.ExtraEnergy, out int extraEnergyStacks) && extraEnergyStacks > 0)
		{
			unit.Energy += extraEnergyStacks;
			toRemove.Add(StateType.ExtraEnergy);
			AppendStateTurnStartInfo(unit, StateType.ExtraEnergy, extraEnergyStacks);
		}

		foreach (StateType stateType in toRemove)
		{
			unit.States.Remove(stateType);
		}

		InvokeStateEndedCallbacks(unit, callbacks);
	}

	private static bool HasAnyBoundCallback(StateRuntimeData stateData)
	{
		if (stateData == null || stateData.StackSegments == null || stateData.StackSegments.Count == 0)
		{
			return false;
		}

		for (int index = 0; index < stateData.StackSegments.Count; index++)
		{
			StateStackSegment segment = stateData.StackSegments[index];
			if (segment != null && segment.NeedCallback)
			{
				return true;
			}
		}

		return false;
	}

	private static void InvokeStateEndedCallbacks(IUnitInstance unit, List<StateEndedContext> callbacks)
	{
		if (unit == null || callbacks == null || callbacks.Count == 0)
		{
			return;
		}

		for (int index = 0; index < callbacks.Count; index++)
		{
			StateEndedContext callback = callbacks[index];
			if (callback == null || !callback.NeedCallback)
			{
				continue;
			}

			unit.OnStateEnded?.Invoke(callback);
		}
	}

	public static bool TryGetStateStacks(IUnitInstance unit, StateType type, out int stacks)
	{
		stacks = 0;
		if (unit == null)
		{
			return false;
		}

		if (!unit.States.TryGetValue(type, out StateRuntimeData stateData))
		{
			return false;
		}

		stacks = stateData.Stacks;
		return stacks > 0;
	}

	private static int CompareStateDisplayOrder(IUnitInstance unit, StateType left, StateType right)
	{
		int leftOrder = GetAppliedOrder(unit, left);
		int rightOrder = GetAppliedOrder(unit, right);
		int compare = leftOrder.CompareTo(rightOrder);
		if (compare != 0)
		{
			return compare;
		}

		return left.CompareTo(right);
	}

	private static int GetAppliedOrder(IUnitInstance unit, StateType type)
	{
		if (unit == null || unit.States == null)
		{
			return int.MaxValue;
		}

		if (!unit.States.TryGetValue(type, out StateRuntimeData stateData) || stateData == null)
		{
			return int.MaxValue;
		}

		return stateData.AppliedOrder;
	}

	private static void AppendStateTurnStartInfo(IUnitInstance unit, StateType type, int stacks)
	{
		string unitLabel = unit == null ? "Unit" : $"Unit#{unit.UniqueInGameId}";
		BattleSytem.Current?.AppendPanelConsoleInfo($"{unitLabel} state {GetStateLabel(type)} triggered: gain {stacks} energy at turn start and remove this state.");
	}

	private static void AppendStateCardPlayedInfo(IUnitInstance unit, StateType type, Card card, EffectResult shieldResult)
	{
		string unitLabel = unit == null ? "Unit" : $"Unit#{unit.UniqueInGameId}";
		string cardLabel = card == null
			? "未知卡牌"
			: string.IsNullOrWhiteSpace(card.CardName) ? $"CardId={card.CardId}" : card.CardName;
		int gainedShield = shieldResult?.ShieldGained ?? 0;
		BattleSytem.Current?.AppendPanelConsoleInfo($"{unitLabel} 的 {GetStateLabel(type)} 触发：打出攻击牌 {cardLabel} 后防御一次，获得 {gainedShield} 点护盾。");
	}

	private static string GetStateLabel(StateType type)
	{
		StateDefinition definition = GetStateDefinition(type);
		if (definition != null && !string.IsNullOrWhiteSpace(definition.Name))
		{
			return definition.Name;
		}

		return type.ToString();
	}

	private static int GetTurnStartDecayAmount(StateType type)
	{
		StateDefinition definition = GetStateDefinition(type);
		if (definition == null || definition.IsPermanent)
		{
			return 0;
		}

		return definition.TurnStartDecayAmount;
	}

	private static StateDefinition GetStateDefinition(StateType type)
	{
		EnsureStateDefinitionsLoaded();
		return LoadingSystem.StateDictionary.TryGetValue(type, out StateDefinition definition)
			? definition
			: null;
	}

	private static void EnsureStateDefinitionsLoaded()
	{
		if (LoadingSystem.StateDictionary.Count > 0)
		{
			return;
		}

		LoadingSystem.LoadStatesByKey(LoadingSystem.StateCsvPathKey, true);
	}
}
