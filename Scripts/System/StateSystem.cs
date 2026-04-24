using System;
using System.Collections.Generic;
using CardSimulator;

public sealed class StateRuntimeData
{
	public StateType Type { get; }
	public int Stacks { get; set; }

	public StateRuntimeData(StateType type, int stacks)
	{
		Type = type;
		Stacks = Math.Max(0, stacks);
	}
}

public static class StateSystem
{
	public static bool IsStackable(StateType type)
	{
		switch (type)
		{
			case StateType.None:
				return false;
			case StateType.Vulnerable:
				return true;
			default:
				return false;
		}
	}

	public static bool IsPermanent(StateType type)
	{
		return !IsStackable(type);
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
				existing.Stacks += stacks;
			}
			else
			{
				existing.Stacks = 1;
			}
			return;
		}

		int initialStacks = IsStackable(type) ? stacks : 1;
		states[type] = new StateRuntimeData(type, initialStacks);
	}

	public static void RemoveState(IUnitInstance unit, StateType type)
	{
		if (unit == null)
		{
			throw new ArgumentNullException(nameof(unit));
		}

		unit.States.Remove(type);
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
			// 易伤：受到伤害 +50%，按向上取整处理
			damage = (damage * 3 + 1) / 2;
		}

		return damage;
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
		foreach (KeyValuePair<StateType, StateRuntimeData> pair in unit.States)
		{
			if (IsPermanent(pair.Key))
			{
				continue;
			}

			pair.Value.Stacks = Math.Max(0, pair.Value.Stacks - 1);
			if (pair.Value.Stacks <= 0)
			{
				toRemove.Add(pair.Key);
			}
		}

		foreach (StateType stateType in toRemove)
		{
			unit.States.Remove(stateType);
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
}
