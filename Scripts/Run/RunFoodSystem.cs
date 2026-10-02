// RunFoodSystem.cs
// 篝火食物系统（[食物系统](../../README/玩法说明文档/系统规则/物品系统/食物系统.md) §二–§四、
// [篝火休息与食物](../../README/玩法说明文档/系统规则/营地系统/篝火休息与食物.md) §三）：纯逻辑、无 Godot 依赖，可单测。
//   ① 篝火草稿：按添加顺序累加，**效果饱食度上限 10**；首个越限者及其后只给饱食度、不给效果；
//   ② 食物效果带**寿命轴**（2026-10-02 口径 ②）：BattleCount / TimePoint / DayCount，数量不填默认 1；
//   ③ 烹饪：每次休息 ≤ 2 次、只消耗**食物**（2026-10-02 口径 ①：材料暂不参与烹饪）；
//   ④ 跨天腐坏与「过期食物不可入篝火」在 RunBagSystem / 本文件校核。
// 静态数值（饱食度 / 效果 / 有效期）来自 ItemNameResolver（LoadingSystem 在配表加载时注册）。
using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;

public static class RunFoodSystem
{
	/// <summary>每次休息最多合成 2 次食物（食物系统 §四）。</summary>
	public const int MaxCookPerRest = 2;

	/// <summary>篝火的**效果**饱食度上限（篝火休息与食物 §三）。</summary>
	public const int MaxEffectSatiety = 10;

	/// <summary>篝火草稿里的一条食物（`GrantsEffect` = 是否仍提供食物效果）。</summary>
	public sealed class CampFireEntry
	{
		public string InstanceId = string.Empty;
		public int FoodKey;
		public string Name = string.Empty;
		public int Satiety;
		public int ExpireDaysRemaining;

		/// <summary>首个使累计饱食度超过上限的食物及其后为 false（只给饱食度）。</summary>
		public bool GrantsEffect;

		/// <summary>含本条在内的累计饱食度（按添加顺序）。</summary>
		public int RunningSatiety;
	}

	/// <summary>
	/// 本次篝火的草稿（点击「休息」前的预览态）：未点击休息不消耗任何食物，
	/// 从草稿拖回背包即撤销（篝火休息交互案「添加食物」）。
	/// </summary>
	public sealed class CampFirePlan
	{
		public List<CampFireEntry> Entries { get; } = new List<CampFireEntry>();

		/// <summary>全部饱食度之和（回复公式按上限 10 夹取，见 RunTimePoints.RestHealRatio）。</summary>
		public int TotalSatiety { get; private set; }

		/// <summary>计入效果的饱食度（≤ 10）。</summary>
		public int EffectiveSatiety { get; private set; }

		public int Count => Entries.Count;
		public bool IsEmpty => Entries.Count == 0;

		/// <summary>按添加顺序追加一条并重算「仍提供效果」标记。</summary>
		public void Append(CampFireEntry entry)
		{
			if (entry == null)
			{
				return;
			}

			int running = TotalSatiety + Math.Max(0, entry.Satiety);
			entry.RunningSatiety = running;
			entry.GrantsEffect = running <= MaxEffectSatiety;
			Entries.Add(entry);
			TotalSatiety = running;
			if (entry.GrantsEffect)
			{
				EffectiveSatiety += Math.Max(0, entry.Satiety);
			}
		}

		/// <summary>把一条从草稿移除（撤销预览）并重算累计。</summary>
		public bool Remove(string instanceId)
		{
			CampFireEntry entry = Entries.FirstOrDefault(x => string.Equals(x.InstanceId, instanceId, StringComparison.Ordinal));
			if (entry == null)
			{
				return false;
			}

			Entries.Remove(entry);
			Recalculate();
			return true;
		}

		public void Clear()
		{
			Entries.Clear();
			TotalSatiety = 0;
			EffectiveSatiety = 0;
		}

		public IReadOnlyList<string> InstanceIds => Entries.Select(x => x.InstanceId).ToList();

		private void Recalculate()
		{
			int running = 0;
			int effective = 0;
			foreach (CampFireEntry entry in Entries)
			{
				running += Math.Max(0, entry.Satiety);
				entry.RunningSatiety = running;
				entry.GrantsEffect = running <= MaxEffectSatiety;
				if (entry.GrantsEffect)
				{
					effective += Math.Max(0, entry.Satiety);
				}
			}

			TotalSatiety = running;
			EffectiveSatiety = effective;
		}
	}

	/// <summary>
	/// 按添加顺序构建篝火草稿。过期食物不能放入篝火（食物系统 §二）→ 直接失败并给出原因；
	/// 空列表也合法（空篝火 = 无饱食度、无食物效果）。
	/// </summary>
	public static bool TryBuildPlan(RunSaveData run, IReadOnlyList<string> instanceIds, out CampFirePlan plan, out string error)
	{
		plan = new CampFirePlan();
		error = string.Empty;
		if (run == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		if (instanceIds == null)
		{
			return true;
		}

		foreach (string instanceId in instanceIds)
		{
			RunBagEntrySave entry = RunBagSystem.Find(run, instanceId);
			if (entry == null)
			{
				error = $"背包里没有该物品：{instanceId}";
				return false;
			}

			if (entry.CategoryEnum != BagCategory.Food)
			{
				error = $"{entry.DefinitionId} 不是食物，不能放入篝火。";
				return false;
			}

			if (!RunBagSystem.CanEnterCampFire(entry))
			{
				error = $"{entry.DefinitionId} 已过期，不能放入篝火。";
				return false;
			}

			plan.Append(new CampFireEntry
			{
				InstanceId = entry.InstanceId,
				FoodKey = entry.DefinitionKey,
				Name = entry.DefinitionId,
				Satiety = ItemNameResolver.FoodSatietyOf(entry.DefinitionKey),
				ExpireDaysRemaining = entry.ExpireDaysRemaining,
			});
		}

		return true;
	}

	// ── 消耗与效果落档（点击「休息」时执行）──

	/// <summary>
	/// 消耗篝火里的全部食物（本次休息结束即消耗，食物系统 §二）并把**仍提供效果**的食物效果
	/// 写进 `ActiveFoodEffects`（寿命轴按定义）。返回新增的效果条目（日志 / 面板用）。
	/// </summary>
	public static List<RunFoodEffectSave> ConsumeCampFire(RunSaveData run, CampFirePlan plan)
	{
		List<RunFoodEffectSave> added = new List<RunFoodEffectSave>();
		if (run == null || plan == null || plan.IsEmpty)
		{
			return added;
		}

		RunBagSystem.EnsureCollections(run);
		foreach (CampFireEntry entry in plan.Entries.ToList())
		{
			if (entry.GrantsEffect)
			{
				foreach (ItemEffectSpec spec in ItemNameResolver.FoodEffectsOf(entry.FoodKey))
				{
					RunFoodEffectSave save = BuildEffectSave(spec, entry.Name);
					run.ActiveFoodEffects.Add(save);
					added.Add(save);
				}
			}

			RunBagSystem.Remove(run, entry.InstanceId, 1);
		}

		plan.Clear();
		return added;
	}

	/// <summary>把一条效果规格落成存档条目（寿命数量 = `DurationValue`，默认 1；`None` 轴按 0 = 立即结算）。</summary>
	public static RunFoodEffectSave BuildEffectSave(ItemEffectSpec spec, string sourceFoodName)
	{
		if (spec == null)
		{
			return null;
		}

		return new RunFoodEffectSave
		{
			EffectType = (int)spec.Type,
			Params = new List<int>(spec.Params),
			DurationKind = (int)spec.DurationKind,
			Remaining = spec.DurationKind == FoodEffectDurationKind.None ? 0f : Math.Max(1, spec.DurationValue),
			SourceFoodId = sourceFoodName ?? string.Empty,
		};
	}

	// ── 寿命轴扣减（2026-10-02 口径 ②）──

	/// <summary>本场战斗开场应生效的效果（剩余寿命 &gt; 0）。</summary>
	public static List<RunFoodEffectSave> ActiveForBattle(RunSaveData run) =>
		run?.ActiveFoodEffects == null
			? new List<RunFoodEffectSave>()
			: run.ActiveFoodEffects.Where(x => x != null && x.Remaining > 0f).ToList();

	/// <summary>战斗结算结束：`BattleCount` 轴 −1，用尽移除。返回被移除的效果。</summary>
	public static List<RunFoodEffectSave> TickBattleEnd(RunSaveData run) =>
		TickByKind(run, FoodEffectDurationKind.BattleCount, 1f);

	/// <summary>时间点消耗：`TimePoint` 轴按消耗量扣减（0.1 精度），用尽移除。返回被移除的效果。</summary>
	public static List<RunFoodEffectSave> TickTimePoints(RunSaveData run, float spentPoints) =>
		TickByKind(run, FoodEffectDurationKind.TimePoint, Math.Max(0f, spentPoints));

	/// <summary>跨天休息：`DayCount` 轴 −1，用尽移除。返回被移除的效果。</summary>
	public static List<RunFoodEffectSave> TickRest(RunSaveData run) =>
		TickByKind(run, FoodEffectDurationKind.DayCount, 1f);

	private static List<RunFoodEffectSave> TickByKind(RunSaveData run, FoodEffectDurationKind kind, float amount)
	{
		List<RunFoodEffectSave> expired = new List<RunFoodEffectSave>();
		if (run?.ActiveFoodEffects == null || amount <= 0f)
		{
			return expired;
		}

		foreach (RunFoodEffectSave effect in run.ActiveFoodEffects.Where(x => x != null && x.DurationKind == (int)kind).ToList())
		{
			effect.Remaining -= amount;
			if (effect.Remaining <= 0f)
			{
				expired.Add(effect);
				run.ActiveFoodEffects.Remove(effect);
			}
		}

		return expired;
	}

	/// <summary>效果的可读文案（面板 / 日志）：`护盾 5（接下来 1 场战斗）`。</summary>
	public static string DescribeEffect(RunFoodEffectSave effect)
	{
		if (effect == null)
		{
			return string.Empty;
		}

		string type = ((EffectType)effect.EffectType).ToString();
		string param = effect.Params == null || effect.Params.Count == 0 ? string.Empty : " " + string.Join("/", effect.Params);
		return $"{type}{param}（{DescribeDuration(effect.DurationKind, effect.Remaining)}）";
	}

	/// <summary>寿命轴文案：`接下来 1 场战斗` / `剩余 2.0 时间点` / `接下来 3 天`。</summary>
	public static string DescribeDuration(int durationKind, float remaining)
	{
		switch ((FoodEffectDurationKind)durationKind)
		{
			case FoodEffectDurationKind.BattleCount:
				return $"接下来 {Math.Max(0, (int)Math.Ceiling(remaining))} 场战斗";
			case FoodEffectDurationKind.TimePoint:
				return $"剩余 {remaining:0.0} 时间点";
			case FoodEffectDurationKind.DayCount:
				return $"接下来 {Math.Max(0, (int)Math.Ceiling(remaining))} 天";
			default:
				return "立即";
		}
	}

	// ── 烹饪（食物系统 §四；2026-10-02 口径 ①：材料暂不参与烹饪）──

	/// <summary>
	/// 能否合成：配方已放行、本次休息次数未用尽、输入齐备。
	/// <paramref name="reservedInstanceIds"/> = 篝火草稿里已经规划好的食物实例（本次休息要吃的）：
	/// 它们**不能算作可用输入** —— 否则「先放进篝火再拿去烹饪」会出现同一件食物被吃两次 / 草稿引用失效。
	/// 失败时 <paramref name="error"/> 是可直接展示给玩家的原因（含"材料暂不参与烹饪"这条口径）。
	/// </summary>
	public static bool CanCook(RunSaveData run, FoodRecipeDefinition recipe, out string error,
		IReadOnlyCollection<string> reservedInstanceIds = null)
	{
		error = string.Empty;
		if (run == null || recipe == null)
		{
			error = "没有可用的配方。";
			return false;
		}

		if (!recipe.Enabled)
		{
			error = $"配方 {recipe.RecipeId} 当前未放行（{recipe.Description}）。";
			return false;
		}

		if (run.CookedThisRest >= MaxCookPerRest)
		{
			error = $"每次休息最多合成 {MaxCookPerRest} 次食物。";
			return false;
		}

		foreach (RecipeInputSpec input in recipe.Inputs)
		{
			if (input.Kind != RecipeInputKind.Food)
			{
				error = "材料暂不参与烹饪（2026-10-02 口径）：只有食物可以作为烹饪原料。";
				return false;
			}

			int have = RunBagSystem.CountOf(run, BagCategory.Food, input.Id, reservedInstanceIds);
			if (have < input.Count)
			{
				error = $"{ItemNameResolver.Food(input.Id)} 不足（需要 {input.Count}，现有 {have}）。";
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// 合成一次：扣输入（**先生效期最短的**，避免先吃掉新鲜食物）→ 产出食物实例 → 本次休息烹饪次数 +1。
	/// `reservedInstanceIds`（篝火草稿）里的实例不参与扣减（见 <see cref="CanCook"/>）。
	/// 成菜进入背包（篝火休息交互案「烹饪」待确认项，默认入背包并允许立刻拖进篝火草稿）。
	/// </summary>
	public static bool TryCook(RunSaveData run, FoodRecipeDefinition recipe, out RunBagEntrySave result, out string error,
		IReadOnlyCollection<string> reservedInstanceIds = null)
	{
		result = null;
		if (!CanCook(run, recipe, out error, reservedInstanceIds))
		{
			return false;
		}

		foreach (RecipeInputSpec input in recipe.Inputs)
		{
			if (!TryConsumeFoodInstances(run, input.Id, input.Count, out error, reservedInstanceIds))
			{
				return false;
			}
		}

		result = RunBagSystem.Add(run, BagCategory.Food, recipe.ResultFoodId, recipe.ResultCount);
		run.CookedThisRest += 1;
		return true;
	}

	/// <summary>
	/// 按**有效期最短优先**扣减同类食物的实例；数量不足时不改动任何实例并给出原因。
	/// `reservedInstanceIds` = 篝火草稿里的实例，保持不动（本次休息由 `ConsumeCampFire` 统一消耗）。
	/// </summary>
	public static bool TryConsumeFoodInstances(RunSaveData run, int foodKey, int count, out string error,
		IReadOnlyCollection<string> reservedInstanceIds = null)
	{
		error = string.Empty;
		HashSet<string> reserved = RunBagSystem.ToSet(reservedInstanceIds);
		List<RunBagEntrySave> candidates = RunBagSystem.EntriesOf(run, BagCategory.Food)
			.Where(x => x.DefinitionKey == foodKey && (reserved == null || !reserved.Contains(x.InstanceId)))
			.OrderBy(x => x.ExpireDaysRemaining)
			.ToList();
		int available = candidates.Sum(x => x.Count);
		if (available < count)
		{
			error = $"{ItemNameResolver.Food(foodKey)} 数量不足（需要 {count}，现有 {available}）。";
			return false;
		}

		int remaining = count;
		foreach (RunBagEntrySave entry in candidates)
		{
			if (remaining <= 0)
			{
				break;
			}

			int take = Math.Min(remaining, entry.Count);
			remaining -= take;
			if (entry.Count > take)
			{
				entry.Count -= take;
			}
			else
			{
				run.BagEntries.Remove(entry);
			}
		}

		return true;
	}
}
