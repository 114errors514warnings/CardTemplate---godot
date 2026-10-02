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
	/// **只数背包内条目**：已放进随身 3 格的条目不在背包里（烹饪 / 消耗的输入口径与背包列表一致）。
	/// </summary>
	public static int CountOf(RunSaveData run, BagCategory category, int definitionKey,
		IReadOnlyCollection<string> excludedInstanceIds = null)
	{
		EnsureCollections(run);
		HashSet<string> excluded = ToSet(excludedInstanceIds);
		return run.BagEntries.Where(x => x != null && x.CategoryEnum == category && x.DefinitionKey == definitionKey
				&& x.IsInBag
				&& (excluded == null || !excluded.Contains(x.InstanceId)))
			.Sum(x => x.Count);
	}

	/// <summary>把实例键集合转成大小写敏感的 HashSet（null / 空 = null，调用方据此跳过筛选）。</summary>
	internal static HashSet<string> ToSet(IReadOnlyCollection<string> instanceIds) =>
		instanceIds == null || instanceIds.Count == 0
			? null
			: new HashSet<string>(instanceIds, StringComparer.Ordinal);

	/// <summary>某类别的**背包内**条目（按加入顺序；不含已放进随身 3 格的条目 —— 那是背包界面的列表口径）。</summary>
	public static List<RunBagEntrySave> EntriesOf(RunSaveData run, BagCategory category)
	{
		EnsureCollections(run);
		return run.BagEntries.Where(x => x != null && x.CategoryEnum == category && x.IsInBag).ToList();
	}

	/// <summary>某类别的全部条目（含随身格上的）：结算 / 消耗类逻辑要看见「已被随身格占住」的条目时用它。</summary>
	public static List<RunBagEntrySave> AllEntriesOf(RunSaveData run, BagCategory category)
	{
		EnsureCollections(run);
		return run.BagEntries.Where(x => x != null && x.CategoryEnum == category).ToList();
	}

	/// <summary>
	/// 当前背包总负荷 = `Σ(单件负荷 × 数量)`（背包系统交互案 §四）。
	/// **只统计背包内物品**：已放进随身 3 格的条目不占背包负荷（手位装备根本不在 `BagEntries` 里）。
	/// </summary>
	public static float TotalLoad(RunSaveData run)
	{
		EnsureCollections(run);
		float total = 0f;
		foreach (RunBagEntrySave entry in run.BagEntries)
		{
			if (entry == null || !entry.IsInBag)
			{
				continue;
			}

			total += ItemNameResolver.LoadOf(entry.CategoryEnum, entry.DefinitionKey) * entry.Count;
		}

		return total;
	}

	/// <summary>队伍负荷上限（背包系统交互案 §四）：来自 `InventoryConfig.csv` 的 `Global` 行（注册进 ItemNameResolver）。</summary>
	public static float LoadLimit => ItemNameResolver.InventoryCapacity;

	/// <summary>是否已超载：超载时仍允许奖励入账，只阻止新的拖入（背包系统交互案 §四）。</summary>
	public static bool IsOverloaded(RunSaveData run) => TotalLoad(run) > LoadLimit;

	/// <summary>试算：再放进 <paramref name="extraLoad"/> 负荷的物品会不会超限（拖入 / 卸下的前置校验）。</summary>
	public static bool WouldExceedLoad(RunSaveData run, float extraLoad) => TotalLoad(run) + extraLoad > LoadLimit;

	/// <summary>负荷不足的原因文案（背包系统交互案 §四 的示例格式：`负荷不足：12.4 + 2.0 &gt; 14.0`）。</summary>
	public static string DescribeLoadReject(RunSaveData run, float extraLoad) =>
		$"负荷不足：{TotalLoad(run):0.0} + {extraLoad:0.0} > {LoadLimit:0.0}";

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

	/// <summary>
	/// 把背包条目搬进随身格；该格已有条目时**交换**（原物回背包，不做满栏替换面板）。
	/// 「条目在哪个格」的唯一真相是 `RunBagEntrySave.CarrySlot`，`RunSaveData.CarryItemSlots` 是同步镜像
	/// （SchemaVersion 5；旧档由 `RunSaveData.MigrateToSchema5` 双向补齐）。
	/// </summary>
	public static bool TrySetCarrySlot(RunSaveData run, int slot, string instanceId, out string error)
	{
		EnsureCollections(run);
		error = string.Empty;
		if (slot < 0 || slot >= CarryItemSlotCount)
		{
			error = "随身格编号越界。";
			return false;
		}

		RunBagEntrySave moving = Find(run, instanceId);
		if (!string.IsNullOrWhiteSpace(instanceId) && moving == null)
		{
			error = "背包里没有该物品。";
			return false;
		}

		RunBagEntrySave occupant = CarrySlotEntry(run, slot);
		if (occupant != null && !ReferenceEquals(occupant, moving))
		{
			occupant.CarrySlot = -1; // 原物回背包
		}

		if (moving != null)
		{
			moving.CarrySlot = slot;
		}

		SyncCarrySlotMirror(run);
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

		RunBagEntrySave occupant = CarrySlotEntry(run, slot);
		if (occupant != null)
		{
			occupant.CarrySlot = -1; // 收入背包（局外不落地）
		}

		SyncCarrySlotMirror(run);
		return true;
	}

	/// <summary>随身格上的条目（无 = null）。</summary>
	public static RunBagEntrySave CarrySlotEntry(RunSaveData run, int slot)
	{
		EnsureCollections(run);
		return slot < 0 || slot >= CarryItemSlotCount
			? null
			: run.BagEntries.FirstOrDefault(x => x != null && x.CarrySlot == slot);
	}

	/// <summary>条目的随身格归属 → 兼容镜像列表（每次随身格变动后调用一次）。</summary>
	public static void SyncCarrySlotMirror(RunSaveData run)
	{
		EnsureCollections(run);
		for (int slot = 0; slot < CarryItemSlotCount; slot++)
		{
			RunBagEntrySave occupant = CarrySlotEntry(run, slot);
			run.CarryItemSlots[slot] = occupant?.InstanceId ?? string.Empty;
		}
	}

	/// <summary>
	/// 按定义取走**一件**（装备搬到手位用）：可叠加类别从合并条目里扣 1（扣完移除该条目），
	/// 食物 / 带实例的类别整条拿走。返回是否成功；<paramref name="taken"/> = 被拿走的那件。
	/// </summary>
	public static bool TakeOneByDefinition(RunSaveData run, BagCategory category, int definitionKey, out RunBagEntrySave taken)
	{
		EnsureCollections(run);
		taken = null;
		RunBagEntrySave entry = run.BagEntries.FirstOrDefault(
			x => x != null && x.CategoryEnum == category && x.DefinitionKey == definitionKey && x.IsInBag);
		if (entry == null)
		{
			return false;
		}

		taken = entry;
		if (entry.Count > 1)
		{
			entry.Count -= 1;
		}
		else
		{
			run.BagEntries.Remove(entry);
		}

		SyncCarrySlotMirror(run);
		return true;
	}

	/// <summary>随身格显示文案：空格 = `空`；条目不存在（已消耗 / 旧档）= `空`。</summary>
	public static string CarrySlotText(RunSaveData run, int slot)
	{
		EnsureCollections(run);
		if (slot < 0 || slot >= CarryItemSlotCount)
		{
			return string.Empty;
		}

		RunBagEntrySave entry = CarrySlotEntry(run, slot);
		return entry == null ? "空" : entry.DefinitionId;
	}
}
