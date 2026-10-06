// RunSession.Place.cs
// 地点**设施结算**的会话入口：设施规则层进 / 出的唯一出口（界面只调这里，不直接改存档字段）。
// 口径出处：旅馆交互案 §四、民宿交互案 §六、树林交互案 §四、锻铁铺交互案 §四、餐厅交互案 §四。
// **2026-10-05 第四轮口径**（村庄案 §四，取代第三轮）：进入设施本身**不是操作**（不看、不扣时间点；
//   旅馆的 1 金币是过夜费）；**每次操作固定消耗少量时间点** —— 设施操作 = 全局数据表
//   `VillageOperationTimePointCost`（默认 **0.1**，见 `RunFacilityCosts.OperationCost`），
//   **树林搜寻另配** `ForestForageTimePointCost`（当前 1.0）；部分操作额外收少量金币（锻造、点菜）。
//   → 四处操作结算（`TryForageMaterials` / `TryCraftEquipment` / `TryCookAtRestaurant` / `TryBuyFood`）
//     都在本文件收时间点；`TrySellFood` 是纯交易，不收。
// **2026-10-06（方案甲）**：原「地点场景会话」部分（`BeginRunPlace` / `CompletePendingPlaceToMap` /
//   `PlaceVillage` / `PlaceMerchant` / `IsInPlace` / `IsInVillage` / `IsInMerchant`）随村庄专用场景
//   （`VillageScene` / `VillageVisit` / `VillageLayout` / `run.village.*`）一并撤除；本文件只留设施结算，
//   待「统一关卡通道」批把设施挂到关卡场景上时，再接新的进入 / 离开入口（届时 `PendingContentType`
//   与 `GameMode` 的取值口径由那一批统一裁定）。
using Godot;
using System;
using System.Collections.Generic;

public partial class RunSession
{
	// ── 设施：旅馆 / 民宿（旅馆案 §四、民宿案 §六）──

	/// <summary>旅馆过夜：校验 → 扣 1 金币 → 逐槽回复 → 推进新一天 → 置 `InnUsed` / `ChosenLodging` → `Save()`。</summary>
	public bool TryRestAtInn(out List<int> healed, out string error) => TryLodge(true, out healed, out error);

	/// <summary>民宿过夜：校验（封门 / 当天已住 / 已选旅馆）→ 免费回复 → 推进新一天 → 记 `GuesthouseUsedDay` → `Save()`。</summary>
	public bool TryRestAtGuesthouse(out List<int> healed, out string error) => TryLodge(false, out healed, out error);

	private bool TryLodge(bool inn, out List<int> healed, out string error)
	{
		// 规则、数值与全部文案都住 `VillageLodging`；这里只做「调它 → Save()」。
		healed = VillageLodging.Apply(Current, inn, out error);
		if (!string.IsNullOrEmpty(error))
		{
			healed = new List<int>();
			return false;
		}

		Save();
		return true;
	}

	// ── 村庄设施：树林（树林案 §四）──

	/// <summary>
	/// 树林搜寻（= 一次「操作」，树林案 §一）：时间点门槛 → 扣**树林专属**表值的时间点 →
	/// 按池抽 2 件（同材料合并）→ 逐件入背包 → `Save()`。**进入树林本身不是操作**（踏入入口格不扣点）。
	/// 背包超载不拒绝（树林案 §四 第 2 步：入账照入、提示超载）。
	/// </summary>
	public bool TryForageMaterials(IReadOnlyList<ForageMaterialEntry> pool, out List<(int MaterialId, int Count)> gained, out string error)
	{
		gained = new List<(int, int)>();
		error = string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		// 树林的代价与村庄其他操作**分开配**，因此走 `VillageForage` 自己的门槛与文案。
		if (!VillageForage.CanSearch(Current.MapState.RemainingToday))
		{
			error = VillageForage.SearchTimePointShortText(Current.MapState.RemainingToday);
			return false;
		}

		if (!TrySpendTimePoints(VillageForage.TimePointCost, out error))
		{
			return false;
		}

		gained = VillageForage.Roll(VillagePlaceData.RandomFor(Current, 0x51F0), pool);
		foreach ((int materialId, int count) in gained)
		{
			if (!TryAddBagItem(BagCategory.Material, materialId, count, out string addError))
			{
				GD.PrintErr($"[村庄] 材料入包被拒：{addError}");
			}
		}

		Save();
		return true;
	}

	// ── 村庄设施：锻铁铺 / 商人锻造炉（锻铁铺案 §四，2026-10-05 第三轮口径）──

	/// <summary>
	/// 一次打造的**全部**结算（村庄锻铁铺与商人锻造炉共用同一份 `SmithyUi` → 共用这一处）：
	/// 时间点 → 次数 → 材料 → 金币 → 背包可入账 逐项校验，通过后扣材料 → 扣金币 → 装备入背包 → `Save()`。
	/// 任一项不过 → 不改任何状态并返回原因。`goldMultiplier` 由 `SmithyContext` 注入（村庄 1.0 / 商人 1.5）。
	/// </summary>
	public bool TryCraftEquipment(EquipmentRecipe recipe, float goldMultiplier, int remainingCrafts, out string error)
	{
		error = string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		if (recipe == null)
		{
			error = "先选一件配方。";
			return false;
		}

		// 时间点不足是硬门槛（村庄案 §四）：不足一次操作的量不能打造，先说去处。
		if (!RunFacilityCosts.CanOperate(Current.MapState.TimePoints))
		{
			error = RunFacilityCosts.OperationTimePointShortText(Current.MapState.TimePoints);
			return false;
		}

		List<SmithyRequirement> requirements = VillageSmithyRequirements(recipe);
		int price = SmithyCrafting.GoldFor(recipe.Gold, goldMultiplier);
		error = SmithyCrafting.Validate(remainingCrafts, Current.Gold, price, requirements);
		if (error.Length > 0)
		{
			return false;
		}

		if (!ItemNameResolver.TryGetEquipmentKey(recipe.ResultDefinitionId, out int key))
		{
			error = $"装备表里没有 {recipe.ResultDefinitionId}。";
			return false;
		}

		if (RunBagSystem.WouldExceedLoad(Current, SmithyCrafting.EquipmentLoad(recipe.ResultDefinitionId)))
		{
			error = SmithyCrafting.BagOverloadText;
			return false;
		}

		// 时间点先付（校验已全过）：付不掉就整笔拒绝，材料与金币一点不动。
		if (!TrySpendTimePoints(SmithyCrafting.CraftTimePointCost, out error))
		{
			return false;
		}

		foreach (SmithyRequirement requirement in requirements)
		{
			for (int i = 0; i < requirement.Need; i++)
			{
				if (!RunBagSystem.TakeOneByDefinition(Current, BagCategory.Material, requirement.MaterialId, out _))
				{
					error = SmithyCrafting.MaterialShortText(requirement.DisplayName, requirement.Need, requirement.Have);
					return false;
				}
			}
		}

		Current.Gold -= price;
		if (!TryAddBagItem(BagCategory.Equipment, key, 1, out string addError))
		{
			error = addError;
			return false;
		}

		Save();
		return true;
	}

	/// <summary>配方的材料需求清单（名称走材料表、持有数走背包实例；界面与结算共用）。</summary>
	public List<SmithyRequirement> VillageSmithyRequirements(EquipmentRecipe recipe) =>
		SmithyCrafting.Requirements(recipe, VillagePlaceData.MaterialName,
			id => Current == null ? 0 : RunBagSystem.CountOf(Current, BagCategory.Material, id));

	// ── 村庄设施：餐厅（餐厅案 §三 / §四 / §五）──

	/// <summary>
	/// 买入食物 = **点菜**（餐厅案 §三 / §四，2026-10-05 第四轮口径）：这也是一次「操作」——
	/// 校验 **时间点** → 金币 → 背包负荷，通过后扣时间点 → 扣金币 → 食物实例入背包 → `Save()`。
	/// 价格由调用方按稀有度算好（`RestaurantTrade.FoodBuyPrice`）。
	/// 出售（`TrySellFood`）是纯交易，不收时间点。
	/// </summary>
	public bool TryBuyFood(int foodKey, int price, out string error)
	{
		error = string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		// 时间点不足是硬门槛（村庄案 §四）：点菜也算一次操作，先说去处，再谈金币与背包。
		if (!RestaurantTrade.CanOrder(Current.MapState.RemainingToday))
		{
			error = RestaurantTrade.OrderTimePointShortText(Current.MapState.RemainingToday);
			return false;
		}

		if (Current.Gold < price)
		{
			error = RestaurantTrade.GoldShortText(price, Current.Gold);
			return false;
		}

		if (RunBagSystem.WouldExceedLoad(Current, ItemNameResolver.LoadOf(BagCategory.Food, foodKey)))
		{
			error = RestaurantTrade.BagOverloadText;
			return false;
		}

		// 时间点先付（前面全是不改状态的校验）：付不掉就整笔拒绝，金币与背包一点不动。
		if (!TrySpendTimePoints(RestaurantTrade.OrderTimePointCost, out error))
		{
			return false;
		}

		Current.Gold -= price;
		RunBagSystem.Add(Current, BagCategory.Food, foodKey, 1);
		Save();
		return true;
	}

	/// <summary>卖出食物实例：移除实例 → 加金币 → `Save()`（餐厅案 §五；现做加成由调用方算好价格）。</summary>
	public bool TrySellFood(string instanceId, int price, out string error)
	{
		error = string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		RunBagEntrySave entry = RunBagSystem.Find(Current, instanceId);
		if (entry == null || entry.CategoryEnum != BagCategory.Food)
		{
			error = RestaurantTrade.OnlyFoodText;
			return false;
		}

		if (!RunBagSystem.Remove(Current, instanceId, 1))
		{
			error = RestaurantTrade.SoldOutText;
			return false;
		}

		Current.Gold += price;
		Save();
		return true;
	}

	/// <summary>
	/// 餐厅现做（餐厅案 §四）：把「材料 → 食物」配方结算一次 —— 校验 **时间点** / 次数 / 材料 / 背包负荷 →
	/// 扣一次操作的**表值**时间点 → 扣材料 → 产物入背包 → `Save()`。**进入餐厅本身不是操作**；
	/// 每次烹饪才是一次「操作」（村庄案 §四，2026-10-05 第四轮口径）。烹饪不收金币
	/// （收金币的是点菜 = `TryBuyFood`）。
	/// 产物的「现做」标由界面侧的 `RestaurantTrade.FreshMarks` 打（离开餐厅即失效）。
	/// 注：营地侧 `RunFoodSystem` 仍是「食物 → 食物」口径，本方法只走材料输入，两边不互相影响（餐厅案 §六）。
	/// </summary>
	public bool TryCookAtRestaurant(FoodRecipeDefinition recipe, int remainingCooks, out RunBagEntrySave result, out string error)
	{
		result = null;
		error = string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		if (recipe == null)
		{
			error = "没有可用的配方。";
			return false;
		}

		// 时间点不足是硬门槛（村庄案 §四）：先说去处，再谈次数 / 材料。
		if (!RestaurantTrade.CanCook(Current.MapState.RemainingToday))
		{
			error = RestaurantTrade.CookTimePointShortText(Current.MapState.RemainingToday);
			return false;
		}

		List<SmithyRequirement> requirements = RestaurantRequirements(recipe, out error);
		if (error.Length > 0)
		{
			return false;
		}

		bool bagAccepts = !RunBagSystem.WouldExceedLoad(Current,
			ItemNameResolver.LoadOf(BagCategory.Food, recipe.ResultFoodId) * Math.Max(1, recipe.ResultCount));
		error = RestaurantTrade.ValidateCook(remainingCooks, requirements, bagAccepts);
		if (error.Length > 0)
		{
			return false;
		}

		// 时间点在此付（前面全是不改状态的校验）：付不掉就整笔拒绝，材料一点不动（村庄案 §四）。
		if (!TrySpendTimePoints(RestaurantTrade.CookTimePointCost, out error))
		{
			return false;
		}

		foreach (SmithyRequirement requirement in requirements)
		{
			for (int i = 0; i < requirement.Need; i++)
			{
				if (!RunBagSystem.TakeOneByDefinition(Current, BagCategory.Material, requirement.MaterialId, out _))
				{
					error = RestaurantTrade.MaterialShortText(requirement.DisplayName, requirement.Need, requirement.Have);
					return false;
				}
			}
		}

		result = RunBagSystem.Add(Current, BagCategory.Food, recipe.ResultFoodId, recipe.ResultCount);
		Save();
		return true;
	}

	/// <summary>餐厅配方的材料需求清单（餐厅只做「材料 → 食物」，食物输入一律拒绝并给原因）。</summary>
	public List<SmithyRequirement> RestaurantRequirements(FoodRecipeDefinition recipe, out string error)
	{
		error = string.Empty;
		List<SmithyRequirement> requirements = new List<SmithyRequirement>();
		if (recipe == null || Current == null)
		{
			return requirements;
		}

		foreach (RecipeInputSpec input in recipe.Inputs)
		{
			if (input == null)
			{
				continue;
			}

			if (input.Kind != RecipeInputKind.Material)
			{
				error = "餐厅只做「材料 → 食物」的配方（食物合成在营地，餐厅案 §六）。";
				return requirements;
			}

			requirements.Add(new SmithyRequirement
			{
				MaterialId = input.Id,
				DisplayName = VillagePlaceData.MaterialName(input.Id),
				Need = input.Count,
				Have = RunBagSystem.CountOf(Current, BagCategory.Material, input.Id),
			});
		}

		return requirements;
	}
}
