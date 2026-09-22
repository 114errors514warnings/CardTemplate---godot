// RunGoldLedger.cs
// 怪物窃取金币的账本：战斗内**按怪物实例**记账，结算时按"该实例是否被击杀"返还。
// 纯逻辑（只依赖 RunSaveData 与整数/字符串参数），便于单测。
using System;
using System.Collections.Generic;

/// <summary>
/// 窃取金币规则（2026-09-22 定，按实例分开）：
/// 1) 怪物**每次攻击命中玩家**时，按其 `StateType.Steal` 层数偷取同额金币；
/// 2) **金币不足则一分不扣**（不做部分扣除，也不会扣成负数）；
/// 3) 每只怪物实例各记一条 <see cref="StolenGoldEntry"/>（`InstanceId` = 关卡对象实例 ID），
///    同一 `MonsterId` 的多只互不合并；
/// 4) 结算时**只返还被击杀实例对应的那一条**，其余（存活的）不返还并清账。
/// </summary>
public static class RunGoldLedger
{
	/// <summary>按怪物实例偷取金币；不足或参数非法时返回 0 且不改动存档。</summary>
	public static int Steal(RunSaveData run, string instanceId, int monsterId, int amount)
	{
		if (run == null || string.IsNullOrWhiteSpace(instanceId) || amount <= 0)
		{
			return 0;
		}

		if (run.Gold < amount)
		{
			return 0;
		}

		run.Gold -= amount;
		StolenGoldEntry entry = Find(run, instanceId);
		if (entry == null)
		{
			entry = new StolenGoldEntry { InstanceId = instanceId, MonsterId = monsterId };
			run.StolenGoldFromMonsters.Add(entry);
		}

		entry.MonsterId = monsterId;
		entry.Amount += amount;
		return amount;
	}

	/// <summary>结算返还：对每个"被击杀的怪物实例"返还其名下的记录并删除该条，返回实际返还总金币。</summary>
	public static int RefundDefeated(RunSaveData run, IReadOnlyCollection<string> defeatedInstanceIds)
	{
		if (run == null || defeatedInstanceIds == null)
		{
			return 0;
		}

		int refunded = 0;
		foreach (string instanceId in defeatedInstanceIds)
		{
			StolenGoldEntry entry = Find(run, instanceId);
			if (entry == null || entry.Amount <= 0)
			{
				continue;
			}

			refunded += entry.Amount;
			run.StolenGoldFromMonsters.Remove(entry);
		}

		if (refunded > 0)
		{
			run.Gold += refunded;
		}

		return refunded;
	}

	/// <summary>未被击杀实例所偷金币不再返还：结算后清账，避免带入下一场。</summary>
	public static void Clear(RunSaveData run)
	{
		run?.StolenGoldFromMonsters?.Clear();
	}

	/// <summary>取某个怪物实例的记账条目；没有则返回 null。</summary>
	public static StolenGoldEntry Find(RunSaveData run, string instanceId)
	{
		if (run?.StolenGoldFromMonsters == null || string.IsNullOrWhiteSpace(instanceId))
		{
			return null;
		}

		foreach (StolenGoldEntry entry in run.StolenGoldFromMonsters)
		{
			if (entry != null && string.Equals(entry.InstanceId, instanceId, StringComparison.Ordinal))
			{
				return entry;
			}
		}

		return null;
	}

	/// <summary>当前账本合计（日志/UI 用）。</summary>
	public static int TotalStolen(RunSaveData run)
	{
		if (run?.StolenGoldFromMonsters == null)
		{
			return 0;
		}

		int total = 0;
		foreach (StolenGoldEntry entry in run.StolenGoldFromMonsters)
		{
			if (entry != null) total += entry.Amount;
		}

		return total;
	}
}
