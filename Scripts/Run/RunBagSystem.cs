// RunBagSystem.cs
// 背包**实例载体**（背包系统交互案 §六 / 食物系统 §二）：纯逻辑、无 Godot 依赖，可单测。
// 职责：增删改查、材料/道具/装备按定义合并、食物按实例存续与跨天腐坏、负荷汇总、局外随身 3 格。
// 静态数值（名字 / 负荷 / 稀有度 / 食物有效期）来自 `ItemNameResolver`，由 `LoadingSystem` 在配表加载时注册 ——
// 因此本文件既不读配表也不碰文件 IO，xUnit 里可直接喂假注册表。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public static class RunBagSystem
{
	/// <summary>局外随身格数（与战斗内「随身道具 3 格」同数，背包系统交互案 §二）。</summary>
	public const int CarryItemSlotCount = 3;

	/// <summary>补齐背包相关集合（旧档 / 新建局都走这里，避免各处判空）。</summary>
	public static void EnsureCollections(RunSaveData run)
	{
		if (run == null)
		{
			return;
		}

		run.BagEntries ??= new List<RunBagEntrySave>();
		run.CarryItemSlots ??= new List<string>();
		while (run.CarryItemSlots.Count < CarryItemSlotCount)
		{
			run.CarryItemSlots.Add(string.Empty);
		}
	}

	/// <summary>该类别是否"按定义合并"（材料 / 道具 / 装备合并；食物恒为独立实例）。</summary>
	public static bool StacksByDefinition(BagCategory category) => category != BagCategory.Food;

	/// <summary>创建一条实例条目（**不**加入集合）：名字 / 稀有度 / 食物有效期 / 实例键都按类别补齐。</summary>
	public static RunBagEntrySave CreateEntry(RunSaveData run, BagCategory category, int definitionKey, int count = 1, string instanceId = null)
	{
		EnsureCollections(run);
		return new RunBagEntrySave
		{
			InstanceId = string.IsNullOrWhiteSpace(instanceId) ? NewInstanceId(run, category, definitionKey) : instanceId,
			DefinitionKey = definitionKey,
			DefinitionId = ItemNameResolver.NameOf(category, definitionKey),
			Category = (int)category,
			Count = Math.Max(1, count),
			Rarity = ItemNameResolver.RarityOf(category, definitionKey),
			ExpireDaysRemaining = category == BagCategory.Food ? ItemNameResolver.FoodExpireDaysOf(definitionKey) : -1,
		};
	}

	/// <summary>实例键：`bag-{类别码}{定义键 5 位}-{序号 3 位}`，序号取该前缀下第一个空位（同类多件食物互不覆盖）。</summary>
	public static string NewInstanceId(RunSaveData run, BagCategory category, int definitionKey)
	{
		EnsureCollections(run);
		string prefix = $"bag-{(int)category}{definitionKey:D5}-";
		HashSet<string> used = new HashSet<string>(
			run.BagEntries.Where(x => x != null && x.InstanceId != null).Select(x => x.InstanceId), StringComparer.Ordinal);
		for (int i = 1; i <= 999; i++)
		{
			string candidate = prefix + i.ToString("D3", CultureInfo.InvariantCulture);
			if (!used.Contains(candidate))
			{
				return candidate;
			}
		}

		return prefix + Guid.NewGuid().ToString("N").Substring(0, 6);
	}

	/// <summary>加入物品：合并型合并到同一格；食物**恒为新实例**（各自记录有效期）。返回落库的条目。</summary>
	public static RunBagEntrySave Add(RunSaveData run, BagCategory category, int definitionKey, int count = 1, string instanceId = null)
	{
		EnsureCollections(run);
		if (run == null || definitionKey <= 0 || count <= 0)
		{
			return null;
		}

		if (StacksByDefinition(category))
		{
			RunBagEntrySave existing = run.BagEntries.FirstOrDefault(
				x => x != null && x.CategoryEnum == category && x.DefinitionKey == definitionKey);
			if (existing != null)
			{
				existing.Count += count;
				return existing;
			}
		}
		else if (count > 1)
		{
			// 食物按实例保存：一次入账 N 件 = N 条各自记录有效期的实例（食物系统 §二）
			RunBagEntrySave last = null;
			for (int i = 0; i < count; i++)
			{
				last = CreateEntry(run, category, definitionKey, 1, i == 0 ? instanceId : null);
				run.BagEntries.Add(last);
			}

			return last;
		}

		RunBagEntrySave entry = CreateEntry(run, category, definitionKey, count, instanceId);
		run.BagEntries.Add(entry);
		return entry;
	}

	/// <summary>按实例键扣减数量：扣完即移除；数量不足只扣现有部分并返回 false。</summary>
	public static bool Remove(RunSaveData run, string instanceId, int count = 1)
	{
		EnsureCollections(run);
		RunBagEntrySave entry = Find(run, instanceId);
		if (entry == null || count <= 0)
		{
			return false;
		}

		bool enough = entry.Count >= count;
		entry.Count -= Math.Min(count, entry.Count);
		if (entry.Count <= 0)
		{
			run.BagEntries.Remove(entry);
		}

		return enough;
	}

	public static RunBagEntrySave Find(RunSaveData run, string instanceId)
	{
		EnsureCollections(run);
		return string.IsNullOrWhiteSpace(instanceId)
			? null
			: run.BagEntries.FirstOrDefault(x => x != null && string.Equals(x.InstanceId, instanceId, StringComparison.Ordinal));
	}

	/// <summary>
	/// 某类别某定义的总件数（跨实例累加）。<paramref name="excludedInstanceIds"/> = 要排除的实例
	/// （篝火草稿里已规划、本次休息要吃的食物：烹饪不得把它们算作可用输入，见 `RunFoodSystem.TryCook`）。
	/// </summary>
	public static int CountOf(RunSaveData run, BagCategory category, int definitionKey,
		IReadOnlyCollection<string> excludedInstanceIds = null)
	{
		EnsureCollections(run);
		HashSet<string> excluded = ToSet(excludedInstanceIds);
		return run.BagEntries.Where(x => x != null && x.CategoryEnum == category && x.DefinitionKey == definitionKey
				&& (excluded == null || !excluded.Contains(x.InstanceId)))
			.Sum(x => x.Count);
	}

	/// <summary>把实例键集合转成大小写敏感的 HashSet（null / 空 = null，调用方据此跳过筛选）。</summary>
	internal static HashSet<string> ToSet(IReadOnlyCollection<string> instanceIds) =>
		instanceIds == null || instanceIds.Count == 0
			? null
			: new HashSet<string>(instanceIds, StringComparer.Ordinal);

	/// <summary>某类别的全部条目（按加入顺序）。</summary>
	public static List<RunBagEntrySave> EntriesOf(RunSaveData run, BagCategory category)
	{
		EnsureCollections(run);
		return run.BagEntries.Where(x => x != null && x.CategoryEnum == category).ToList();
	}

	/// <summary>当前背包总负荷 = `Σ(单件负荷 × 数量)`（背包系统交互案 §四）。</summary>
	public static float TotalLoad(RunSaveData run)
	{
		EnsureCollections(run);
		float total = 0f;
		foreach (RunBagEntrySave entry in run.BagEntries)
		{
			if (entry == null)
			{
				continue;
			}

			total += ItemNameResolver.LoadOf(entry.CategoryEnum, entry.DefinitionKey) * entry.Count;
		}

		return total;
	}

	/// <summary>给定食物实例列表的总饱食度（篝火结算与回复预览用；取值来自配表注册表）。</summary>
	public static int SatietyOf(RunSaveData run, IReadOnlyList<string> instanceIds)
	{
		if (instanceIds == null)
		{
			return 0;
		}

		int total = 0;
		foreach (string instanceId in instanceIds)
		{
			RunBagEntrySave entry = Find(run, instanceId);
			if (entry != null && entry.CategoryEnum == BagCategory.Food)
			{
				total += ItemNameResolver.FoodSatietyOf(entry.DefinitionKey);
			}
		}

		return total;
	}

	/// <summary>
	/// 跨天腐坏（食物系统 §二）：所有食物实例有效期 −1，到 0 立刻移除。
	/// 返回**被移除**的条目（调用方用于日志 / 提示：「X 腐坏了」）。
	/// </summary>
	public static List<RunBagEntrySave> DecayFoodExpiry(RunSaveData run)
	{
		EnsureCollections(run);
		List<RunBagEntrySave> spoiled = new List<RunBagEntrySave>();
		foreach (RunBagEntrySave entry in run.BagEntries.Where(x => x != null && x.CategoryEnum == BagCategory.Food).ToList())
		{
			entry.ExpireDaysRemaining -= 1;
			if (entry.ExpireDaysRemaining <= 0)
			{
				spoiled.Add(entry);
				run.BagEntries.Remove(entry);
			}
		}

		return spoiled;
	}

	/// <summary>过期的食物不能放入篝火（食物系统 §二）：剩余有效期 ≤ 0 视为已腐坏。</summary>
	public static bool CanEnterCampFire(RunBagEntrySave entry) =>
		entry != null && entry.CategoryEnum == BagCategory.Food && entry.ExpireDaysRemaining > 0;

	// ── 局外随身 3 格（背包系统交互案 §二 / §三）──

	/// <summary>把背包条目放进随身格；该格已有物品时**交换**（原物回背包，不做满栏替换面板）。</summary>
	public static bool TrySetCarrySlot(RunSaveData run, int slot, string instanceId, out string error)
	{
		EnsureCollections(run);
		error = string.Empty;
		if (slot < 0 || slot >= CarryItemSlotCount)
		{
			error = "随身格编号越界。";
			return false;
		}

		if (!string.IsNullOrWhiteSpace(instanceId) && Find(run, instanceId) == null)
		{
			error = "背包里没有该物品。";
			return false;
		}

		run.CarryItemSlots[slot] = instanceId ?? string.Empty;
		return true;
	}

	public static bool TryClearCarrySlot(RunSaveData run, int slot, out string error)
	{
		EnsureCollections(run);
		error = string.Empty;
		if (slot < 0 || slot >= CarryItemSlotCount)
		{
			error = "随身格编号越界。";
			return false;
		}

		run.CarryItemSlots[slot] = string.Empty;
		return true;
	}

	/// <summary>随身格显示文案：空格 = `空`；实例不存在（已消耗 / 旧档）= `空`。</summary>
	public static string CarrySlotText(RunSaveData run, int slot)
	{
		EnsureCollections(run);
		if (slot < 0 || slot >= CarryItemSlotCount)
		{
			return string.Empty;
		}

		RunBagEntrySave entry = Find(run, run.CarryItemSlots[slot]);
		return entry == null ? "空" : entry.DefinitionId;
	}
}
