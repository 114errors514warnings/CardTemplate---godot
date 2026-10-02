// RunFoodSystemTests.cs
// 篝火食物系统（2026-10-02 批 C / P1-19）：纯逻辑单测。
// 覆盖：效果饱食度上限 10 与「首个越限者及其后不给效果」、草稿撤销重算、过期食物不可入篝火、
// 食物效果寿命轴（BattleCount / TimePoint / DayCount）扣减、以及「材料不参与烹饪」的 2026-10-02 口径。
using System.Collections.Generic;
using System.Linq;
using CardSimulator;
using Xunit;

[Collection("ItemNameResolverState")]
public class RunFoodSystemTests
{
	/// <summary>注册假食物表（饱食度 / 有效期 / 效果）与配方所需的食物定义。</summary>
	private static void RegisterFoods(params (int Id, string Name, int Satiety, int ExpireDays, string Effect, string Duration)[] foods)
	{
		ItemNameResolver.Clear();
		Dictionary<int, FoodDefinition> table = new Dictionary<int, FoodDefinition>();
		foreach ((int id, string name, int satiety, int expireDays, string effect, string duration) in foods)
		{
			FoodDefinition definition = new FoodDefinition
			{
				FoodId = id,
				DefinitionId = name,
				Satiety = satiety,
				ExpireDays = expireDays,
			};
			ItemEffectSpec spec = ItemEffectSpecParser.ParseEffect(effect, name);
			if (spec != null)
			{
				ItemEffectSpecParser.ApplyDuration(spec, duration, name);
				definition.Effects.Add(spec);
			}

			table[id] = definition;
		}

		ItemNameResolver.RegisterFoods(table);
	}

	private static (int Id, string Name, int Satiety, int ExpireDays, string Effect, string Duration) Food(
		int id, string name, int satiety, int expireDays = 3, string effect = null, string duration = null) =>
		(id, name, satiety, expireDays, effect, duration);

	[Fact]
	public void CampFirePlan_CapsEffectSatietyAtTen_FirstOverflowAndBeyondGrantNoEffect()
	{
		RegisterFoods(Food(401, "粗麦饼", 4), Food(402, "香草炖菜", 4), Food(403, "烤蟾蜍", 4));
		RunSaveData run = new RunSaveData();
		string a = RunBagSystem.Add(run, BagCategory.Food, 401, 1).InstanceId;
		string b = RunBagSystem.Add(run, BagCategory.Food, 402, 1).InstanceId;
		string c = RunBagSystem.Add(run, BagCategory.Food, 403, 1).InstanceId;

		Assert.True(RunFoodSystem.TryBuildPlan(run, new[] { a, b, c }, out RunFoodSystem.CampFirePlan plan, out string error), error);

		Assert.Equal(12, plan.TotalSatiety);            // 全部饱食度都算进回复（公式按上限 10 夹取）
		Assert.Equal(8, plan.EffectiveSatiety);         // 计入效果的只有前两条
		Assert.True(plan.Entries[0].GrantsEffect);
		Assert.True(plan.Entries[1].GrantsEffect);
		Assert.False(plan.Entries[2].GrantsEffect);     // 首个使累计超过 10 的食物
		Assert.Equal(12, plan.Entries[2].RunningSatiety);
	}

	[Fact]
	public void CampFirePlan_ExactlyTen_StillGrantsEffect_AndRemoveRecalculates()
	{
		RegisterFoods(Food(401, "粗麦饼", 5), Food(402, "香草炖菜", 5), Food(403, "烤蟾蜍", 5));
		RunSaveData run = new RunSaveData();
		string a = RunBagSystem.Add(run, BagCategory.Food, 401, 1).InstanceId;
		string b = RunBagSystem.Add(run, BagCategory.Food, 402, 1).InstanceId;
		string c = RunBagSystem.Add(run, BagCategory.Food, 403, 1).InstanceId;

		RunFoodSystem.TryBuildPlan(run, new[] { a, b, c }, out RunFoodSystem.CampFirePlan plan, out _);
		Assert.True(plan.Entries[1].GrantsEffect);  // 恰好到 10 → 仍给效果
		Assert.False(plan.Entries[2].GrantsEffect);

		Assert.True(plan.Remove(a));                // 拖回背包 = 撤销预览 → 重算：5 / 10
		Assert.Equal(10, plan.TotalSatiety);
		Assert.True(plan.Entries[0].GrantsEffect);
		Assert.True(plan.Entries[1].GrantsEffect);
	}

	[Fact]
	public void CampFirePlan_RejectsExpiredFood()
	{
		RegisterFoods(Food(401, "粗麦饼", 2, 1));
		RunSaveData run = new RunSaveData();
		string id = RunBagSystem.Add(run, BagCategory.Food, 401, 1).InstanceId;
		RunBagSystem.DecayFoodExpiry(run); // 1 → 0 腐坏

		Assert.Null(RunBagSystem.Find(run, id));
		Assert.False(RunFoodSystem.TryBuildPlan(run, new[] { id }, out _, out string error));
		Assert.Contains("背包里没有", error);
	}

	[Fact]
	public void ConsumeCampFire_RemovesFood_AndWritesEffectsWithLifetime()
	{
		RegisterFoods(
			Food(402, "香草炖菜", 3, 2, "Shield:2", "BattleCount:1"),
			Food(407, "龙血藤药膳", 5, 3, "Shield:5", "DayCount:2"));
		RunSaveData run = new RunSaveData();
		string id = RunBagSystem.Add(run, BagCategory.Food, 402, 1).InstanceId;
		RunBagSystem.Add(run, BagCategory.Food, 402, 1); // 同类第二件：不入篝火，用于验证实例制不被连带消耗
		RunBagSystem.Add(run, BagCategory.Food, 407, 1);

		RunFoodSystem.TryBuildPlan(run, new[] { id }, out RunFoodSystem.CampFirePlan plan, out _);
		List<RunFoodEffectSave> added = RunFoodSystem.ConsumeCampFire(run, plan);

		Assert.Single(added);                                   // 只消耗了放入篝火的那一件
		Assert.True(plan.IsEmpty);
		Assert.Equal(1, RunBagSystem.CountOf(run, BagCategory.Food, 402)); // 未放入的同类食物仍在（实例制）
		Assert.Equal(1, RunBagSystem.CountOf(run, BagCategory.Food, 407));

		RunFoodEffectSave effect = added[0];
		Assert.Equal((int)EffectType.Shield, effect.EffectType);
		Assert.Equal(2, effect.Params[0]);
		Assert.Equal((int)FoodEffectDurationKind.BattleCount, effect.DurationKind);
		Assert.Equal(1f, effect.Remaining);
		Assert.Equal("香草炖菜", effect.SourceFoodId);
		Assert.Contains("接下来 1 场战斗", RunFoodSystem.DescribeEffect(effect));
	}

	[Fact]
	public void EffectLifetime_TicksPerAxis_AndRemovesWhenExhausted()
	{
		RegisterFoods(
			Food(402, "香草炖菜", 3, 2, "Shield:2", "BattleCount:2"),
			Food(406, "毒蛇杂烩", 3, 1, "AddState:AddAttack:2", "TimePoint:2"),
			Food(404, "蜂蜜烤肉", 3, 3, "AddState:AddAttack:1", "DayCount:2")); // 3+3+3=9 ≤ 10 → 三条都给效果
		RunSaveData run = new RunSaveData();
		string a = RunBagSystem.Add(run, BagCategory.Food, 402, 1).InstanceId;
		string b = RunBagSystem.Add(run, BagCategory.Food, 406, 1).InstanceId;
		string c = RunBagSystem.Add(run, BagCategory.Food, 404, 1).InstanceId;
		RunFoodSystem.TryBuildPlan(run, new[] { a, b, c }, out RunFoodSystem.CampFirePlan plan, out _);
		RunFoodSystem.ConsumeCampFire(run, plan);

		Assert.Equal(3, RunFoodSystem.ActiveForBattle(run).Count);

		Assert.Empty(RunFoodSystem.TickBattleEnd(run));         // 2 → 1
		Assert.Single(RunFoodSystem.TickBattleEnd(run));        // 1 → 0 → 移除（BattleCount 轴）
		Assert.Equal(2, RunFoodSystem.ActiveForBattle(run).Count);

		Assert.Empty(RunFoodSystem.TickTimePoints(run, 0.3f));  // 2.0 → 1.7
		Assert.Single(RunFoodSystem.TickTimePoints(run, 1.8f)); // 1.7 → -0.1 → 移除（TimePoint 轴）

		Assert.Empty(RunFoodSystem.TickRest(run));              // 2 → 1
		Assert.Single(RunFoodSystem.TickRest(run));             // 1 → 0 → 移除（DayCount 轴）
		Assert.Empty(RunFoodSystem.ActiveForBattle(run));
	}

	[Fact]
	public void Cook_ConsumesFoodInputs_AndCapsAtTwoPerRest()
	{
		RegisterFoods(Food(403, "烤蟾蜍", 3, 2), Food(404, "蜂蜜烤肉", 4, 3, "AddState:AddAttack:1", "BattleCount:1"));
		RunSaveData run = new RunSaveData();
		for (int i = 0; i < 6; i++)
		{
			RunBagSystem.Add(run, BagCategory.Food, 403, 1); // 6 只烤蟾蜍
		}

		FoodRecipeDefinition recipe = new FoodRecipeDefinition { RecipeId = 101, ResultFoodId = 404, Enabled = true };
		recipe.Inputs.Add(new RecipeInputSpec { Kind = RecipeInputKind.Food, Id = 403, Count = 2 });

		Assert.True(RunFoodSystem.CanCook(run, recipe, out string beforeError), beforeError); // 配方需求 2 只
		int cookedBefore = RunBagSystem.CountOf(run, BagCategory.Food, 404);
		Assert.True(RunFoodSystem.TryCook(run, recipe, out RunBagEntrySave cooked, out string error), error);
		Assert.Equal("蜂蜜烤肉", cooked.DefinitionId);
		Assert.Equal(cookedBefore + 1, RunBagSystem.CountOf(run, BagCategory.Food, 404));

		Assert.True(RunFoodSystem.TryCook(run, recipe, out _, out _));   // 第 2 次
		Assert.Equal(RunFoodSystem.MaxCookPerRest, run.CookedThisRest);
		Assert.False(RunFoodSystem.TryCook(run, recipe, out _, out string thirdError)); // 上限 2 次
		Assert.Contains("最多合成", thirdError);
	}

	[Fact]
	public void Cook_RejectsMaterialChannelRecipe_AndMissingInputs()
	{
		RegisterFoods(Food(402, "香草炖菜", 3, 2));
		RunSaveData run = new RunSaveData();

		FoodRecipeDefinition materialRecipe = new FoodRecipeDefinition { RecipeId = 1, ResultFoodId = 402, Enabled = true };
		materialRecipe.Inputs.Add(new RecipeInputSpec { Kind = RecipeInputKind.Material, Id = 101, Count = 2 });
		Assert.False(RunFoodSystem.CanCook(run, materialRecipe, out string materialError));
		Assert.Contains("材料暂不参与烹饪", materialError); // 2026-10-02 口径 ①

		FoodRecipeDefinition disabledRecipe = new FoodRecipeDefinition { RecipeId = 2, ResultFoodId = 402, Enabled = false };
		disabledRecipe.Inputs.Add(new RecipeInputSpec { Kind = RecipeInputKind.Food, Id = 402, Count = 1 });
		Assert.False(RunFoodSystem.CanCook(run, disabledRecipe, out string disabledError));
		Assert.Contains("未放行", disabledError);

		FoodRecipeDefinition foodRecipe = new FoodRecipeDefinition { RecipeId = 102, ResultFoodId = 402, Enabled = true };
		foodRecipe.Inputs.Add(new RecipeInputSpec { Kind = RecipeInputKind.Food, Id = 402, Count = 1 });
		Assert.False(RunFoodSystem.CanCook(run, foodRecipe, out string missingError));
		Assert.Contains("不足", missingError);
	}

	[Fact]
	public void ConsumeFoodInstances_EatsTheShortestExpiryFirst()
	{
		RegisterFoods(Food(403, "烤蟾蜍", 3, 3));
		RunSaveData run = new RunSaveData();
		RunBagEntrySave fresh = RunBagSystem.Add(run, BagCategory.Food, 403, 1);
		RunBagEntrySave old = RunBagSystem.Add(run, BagCategory.Food, 403, 1);
		old.ExpireDaysRemaining = 1; // 手动把其中一件改旧

		Assert.True(RunFoodSystem.TryConsumeFoodInstances(run, 403, 1, out string error), error);
		Assert.Null(RunBagSystem.Find(run, old.InstanceId));   // 先吃掉快过期的
		Assert.NotNull(RunBagSystem.Find(run, fresh.InstanceId));
	}

	/// <summary>
	/// 篝火草稿里的食物不能被当烹饪原料（2026-10-02 批 C 口径）：草稿只是「本次休息要吃」的引用，
	/// 点「休息」前仍在背包里 —— 若烹饪把它吃掉，玩家就能吃到一件已经不存在的食物。
	/// </summary>
	[Fact]
	public void Cook_SkipsInstancesReservedByCampFireDraft()
	{
		RegisterFoods(Food(403, "烤蟾蜍", 3, 3));
		RunSaveData run = new RunSaveData();
		RunBagEntrySave drafted = RunBagSystem.Add(run, BagCategory.Food, 403, 1);
		FoodRecipeDefinition recipe = new FoodRecipeDefinition { RecipeId = 101, ResultFoodId = 404, Enabled = true };
		recipe.Inputs.Add(new RecipeInputSpec { Kind = RecipeInputKind.Food, Id = 403, Count = 1 });

		// 唯一一件已放进篝火 → 对烹饪不可见
		Assert.False(RunFoodSystem.CanCook(run, recipe, out string reservedError, new[] { drafted.InstanceId }));
		Assert.Contains("不足", reservedError);
		Assert.False(RunFoodSystem.TryCook(run, recipe, out _, out _, new[] { drafted.InstanceId }));
		Assert.NotNull(RunBagSystem.Find(run, drafted.InstanceId));

		// 再放一件非草稿的同类食物 → 只吃非草稿那件，草稿引用保持有效
		RunBagEntrySave spare = RunBagSystem.Add(run, BagCategory.Food, 403, 1);
		Assert.True(RunFoodSystem.TryCook(run, recipe, out RunBagEntrySave cooked, out string error, new[] { drafted.InstanceId }), error);
		Assert.Equal(404, cooked.DefinitionKey);
		Assert.Null(RunBagSystem.Find(run, spare.InstanceId));
		Assert.NotNull(RunBagSystem.Find(run, drafted.InstanceId));
		Assert.Equal(1, RunBagSystem.CountOf(run, BagCategory.Food, 403));                                     // 背包里只剩草稿那件
		Assert.Equal(0, RunBagSystem.CountOf(run, BagCategory.Food, 403, new[] { drafted.InstanceId }));       // …对烹饪不可见
	}
}
