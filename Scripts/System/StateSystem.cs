using System;
using System.Collections.Generic;
using CardSimulator;

public sealed class StateRuntimeData
{
	public StateType Type { get; }
	public int Stacks { get; set; }
	public List<StateStackSegment> StackSegments { get; } = new List<StateStackSegment>();

	public StateRuntimeData(StateType type, int stacks)
	{
		Type = type;
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
	private const string DefaultStateCsvPath = "res://DataBase/State/通用State.csv";

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
				existing.StackSegments.Clear();
				existing.StackSegments.Add(new StateStackSegment(1));
			}
			return;
		}

		int initialStacks = IsStackable(type) ? stacks : 1;
		states[type] = new StateRuntimeData(type, initialStacks);
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

		return damage;
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
				toRemove.Add(pair.Key);
			}
		}

		foreach (StateType stateType in toRemove)
		{
			unit.States.Remove(stateType);
		}

		InvokeStateEndedCallbacks(unit, callbacks);
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

		LoadingSystem.LoadStates(DefaultStateCsvPath, true);
	}
}
