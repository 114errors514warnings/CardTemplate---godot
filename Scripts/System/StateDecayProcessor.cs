using System.Collections.Generic;
using CardSimulator;

/// <summary>
/// 状态衰减处理触发点。决定在哪一时刻对状态做 stacks 衰减 + 自动移除。
/// </summary>
public enum DecayTrigger
{
	/// <summary>任意回合开始时（玩家+怪物都触发）</summary>
	OnTurnStart,
	/// <summary>任意回合结束时（玩家+怪物都触发）</summary>
	OnTurnEnd,
	/// <summary>打出攻击牌后（玩家+怪物都触发）</summary>
	OnAttackPlayed,
	/// <summary>受到伤害后（HP 实际减少时）</summary>
	OnDamaged,
}

/// <summary>
/// 按 StateDefinition.DecayTiming + DecayMode 在指定触发点处理状态衰减：
///   - DecayTiming=Never（永久）状态任何时机都跳过
///   - DecayMode=None 状态任何时机都跳过
///   - DecayTiming 与触发点不匹配 → 跳过
///   - 匹配时按 DecayMode 处理（None / Flat / Half / ClearAll）+ 触发 end callback + 移除
///
/// 仅处理"衰减"逻辑；TurnStartEffect 给资源 / ShieldGuard 给 3 护盾 / ShieldCapEqualsHP cap 护盾
/// 等"回合开始时状态机制效果"仍由 StateSystem.OnTurnStart 处理。
///
/// 调点顺序：先 StateSystem.OnTurnStart（给资源/护盾）→ 后 StateDecayProcessor.ProcessDecayAtTiming
/// （按 DecayMode=ClearAll 移除）—— 保证"给资源在先，移除在后"。
/// </summary>
public static class StateDecayProcessor
{
	public static void ProcessDecayAtTiming(IUnitInstance unit, DecayTrigger trigger)
	{
		if (unit == null || unit.States.Count == 0)
		{
			return;
		}

		List<StateType> toRemove = new List<StateType>();
		List<StateEndedContext> callbacks = new List<StateEndedContext>();

		foreach (KeyValuePair<StateType, StateRuntimeData> pair in unit.States)
		{
			if (!LoadingSystem.StateDictionary.TryGetValue(pair.Key, out StateDefinition def) || def == null)
			{
				continue;
			}
			if (def.DecayTiming == StateDecayTiming.Never)
			{
				continue;
			}
			if (def.DecayMode == StateDecayMode.None)
			{
				continue;
			}
			if (!ShouldDecayAtTiming(def.DecayTiming, trigger))
			{
				continue;
			}

			switch (def.DecayMode)
			{
				case StateDecayMode.ClearAll:
					for (int i = 0; i < pair.Value.Stacks; i++)
					{
						StateEndedContext endedContext = pair.Value.ConsumeOneStack(unit, StateEndReason.Expired);
						if (endedContext != null)
						{
							callbacks.Add(endedContext);
						}
					}
					callbacks.AddRange(pair.Value.ConsumeAllCallbacks(unit, StateEndReason.Expired));
					toRemove.Add(pair.Key);
					break;

				case StateDecayMode.Half:
					int halfStacks = (pair.Value.Stacks + 1) / 2;
					for (int i = 0; i < halfStacks && pair.Value.Stacks > 0; i++)
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
					break;

				case StateDecayMode.Flat:
					for (int count = 0; count < def.StacksToRemove && pair.Value.Stacks > 0; count++)
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
					break;
			}
		}

		if (toRemove.Count > 0)
		{
			foreach (StateType type in toRemove)
			{
				unit.States.Remove(type);
			}
			if (unit.OnStateEnded != null)
			{
				foreach (StateEndedContext ctx in callbacks)
				{
					unit.OnStateEnded.Invoke(ctx);
				}
			}
		}
	}

	/// <summary>
	/// 状态定义的 DecayTiming 与调点 DecayTrigger 配对：
	/// 两者是不同枚举（DecayTiming 含 Never 表示永久，无对应调点），按同名时匹配。
	/// </summary>
	private static bool ShouldDecayAtTiming(StateDecayTiming timing, DecayTrigger trigger)
	{
		return timing switch
		{
			StateDecayTiming.OnTurnStart => trigger == DecayTrigger.OnTurnStart,
			StateDecayTiming.OnTurnEnd => trigger == DecayTrigger.OnTurnEnd,
			StateDecayTiming.OnAttackPlayed => trigger == DecayTrigger.OnAttackPlayed,
			StateDecayTiming.OnDamaged => trigger == DecayTrigger.OnDamaged,
			_ => false,
		};
	}
}
