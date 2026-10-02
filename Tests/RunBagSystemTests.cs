// RunBagSystemTests.cs
// 背包实例载体（2026-10-02 批 B / P0-17 / P1-19）：纯逻辑单测。
// 覆盖：材料合并 vs 食物实例化、跨天腐坏、负荷汇总、随身 3 格、旧档（SchemaVersion 3 → 4）迁移不丢数、
// 以及结算入账同时写"汇总字典 + 实例条目"。
using System.Collections.Generic;
using System.Linq;
using Xunit;

[Collection("ItemNameResolverState")]
public class RunBagSystemTests
{
	/// <summary>注册假物品表（名字 / 负荷 / 稀有度 / 有效期）：模拟 LoadingSystem 配表加载后的注册状态。</summary>
	private static void RegisterStubTables()
	{
		ItemNameResolver.Clear();
		ItemNameResolver.RegisterMaterials(new Dictionary<int, MaterialDefinition>
		{
			[101] = new MaterialDefinition { MaterialId = 101, DefinitionId = "药草", Load = 0.2f, Rarity = ItemRarity.Common },
			[102] = new MaterialDefinition { MaterialId = 102, DefinitionId = "苔藓", Load = 0.2f, Rarity = ItemRarity.Common },
		});
		ItemNameResolver.RegisterItems(new Dictionary<int, ItemDefinition>
		{
			[301] = new ItemDefinition { ItemId = 301, DefinitionId = "治疗药水", Load = 0.3f, Rarity = ItemRarity.Common },
		});
		ItemNameResolver.RegisterFoods(new Dictionary<int, FoodDefinition>
		{
			[401] = new FoodDefinition { FoodId = 401, DefinitionId = "粗麦饼", Satiety = 2, ExpireDays = 3, Load = 0.5f, Rarity = ItemRarity.Common },
			[403] = new FoodDefinition { FoodId = 403, DefinitionId = "烤蟾蜍", Satiety = 3, ExpireDays = 2, Load = 0.6f, Rarity = ItemRarity.Common },
		});
	}

	[Fact]
	public void Add_MergesMaterials_ButKeepsFoodAsSeparateInstances()
	{
		RegisterStubTables();
		RunSaveData run = new RunSaveData();

		RunBagEntrySave first = RunBagSystem.Add(run, BagCategory.Material, 101, 2);
		RunBagEntrySave second = RunBagSystem.Add(run, BagCategory.Material, 101, 3);

		Assert.Same(first, second); // 材料按定义合并到同一格
		Assert.Equal(5, RunBagSystem.CountOf(run, BagCategory.Material, 101));
		Assert.Equal("药草", first.DefinitionId);
		Assert.Single(run.BagEntries);

		RunBagEntrySave foodA = RunBagSystem.Add(run, BagCategory.Food, 401, 1);
		RunBagEntrySave foodB = RunBagSystem.Add(run, BagCategory.Food, 401, 1);

		Assert.NotSame(foodA, foodB); // 食物恒为独立实例（各自有效期）
		Assert.NotEqual(foodA.InstanceId, foodB.InstanceId);
		Assert.Equal(3, foodA.ExpireDaysRemaining); // 有效期来自食物表
		Assert.Equal(2, RunBagSystem.CountOf(run, BagCategory.Food, 401));
	}

	[Fact]
	public void DecayFoodExpiry_DecrementsAndRemovesSpoiledFood()
	{
		RegisterStubTables();
		RunSaveData run = new RunSaveData();
		RunBagEntrySave bread = RunBagSystem.Add(run, BagCategory.Food, 401, 1); // 3 天
		RunBagEntrySave toad = RunBagSystem.Add(run, BagCategory.Food, 403, 1);  // 2 天
		RunBagSystem.Add(run, BagCategory.Material, 101, 1);

		Assert.Empty(RunBagSystem.DecayFoodExpiry(run)); // 第 1 次：3→2 / 2→1
		Assert.Equal(2, bread.ExpireDaysRemaining);
		Assert.Equal(1, toad.ExpireDaysRemaining);

		List<RunBagEntrySave> spoiled = RunBagSystem.DecayFoodExpiry(run); // 第 2 次：2→1 / 1→0 腐坏
		Assert.Single(spoiled);
		Assert.Same(toad, spoiled[0]);
		Assert.DoesNotContain(toad, run.BagEntries);
		Assert.Contains(bread, run.BagEntries);
		Assert.Equal(1, RunBagSystem.CountOf(run, BagCategory.Material, 101)); // 材料不受影响
		Assert.False(RunBagSystem.CanEnterCampFire(toad));
		Assert.True(RunBagSystem.CanEnterCampFire(bread));
	}

	[Fact]
	public void TotalLoad_SumsPerItemLoadTimesCount()
	{
		RegisterStubTables();
		RunSaveData run = new RunSaveData();
		RunBagSystem.Add(run, BagCategory.Material, 101, 5); // 0.2 × 5 = 1.0
		RunBagSystem.Add(run, BagCategory.Item, 301, 2);    // 0.3 × 2 = 0.6
		RunBagSystem.Add(run, BagCategory.Food, 401, 1);    // 0.5 × 1 = 0.5

		Assert.Equal(2.1f, RunBagSystem.TotalLoad(run), 3);
	}

	[Fact]
	public void CarrySlots_SwapAndClearAndOutOfRange()
	{
		RegisterStubTables();
		RunSaveData run = new RunSaveData();
		RunBagEntrySave potion = RunBagSystem.Add(run, BagCategory.Item, 301, 1);
		RunBagEntrySave herb = RunBagSystem.Add(run, BagCategory.Material, 101, 1);

		Assert.True(RunBagSystem.TrySetCarrySlot(run, 0, potion.InstanceId, out _));
		Assert.Equal("治疗药水", RunBagSystem.CarrySlotText(run, 0));
		Assert.Equal("空", RunBagSystem.CarrySlotText(run, 1));

		// 放入已有物品的格子 = 交换（原物回背包，不做满栏替换面板）
		Assert.True(RunBagSystem.TrySetCarrySlot(run, 0, herb.InstanceId, out _));
		Assert.Equal("药草", RunBagSystem.CarrySlotText(run, 0));

		Assert.True(RunBagSystem.TryClearCarrySlot(run, 0, out _));
		Assert.Equal("空", RunBagSystem.CarrySlotText(run, 0));

		Assert.False(RunBagSystem.TrySetCarrySlot(run, 3, herb.InstanceId, out string rangeError));
		Assert.NotEmpty(rangeError);

		Assert.False(RunBagSystem.TrySetCarrySlot(run, 0, "bag-9999999-999", out string missingError));
		Assert.NotEmpty(missingError);
	}

	[Fact]
	public void Migrate_LegacyDictionaries_ExpandIntoBagEntries_WithoutLosingCounts()
	{
		RegisterStubTables();
		RunSaveData legacy = new RunSaveData
		{
			SchemaVersion = 3,
			Materials = new Dictionary<int, int> { [101] = 7, [102] = 2 },
			Items = new Dictionary<int, int> { [301] = 1 },
			Equipment = new Dictionary<int, int> { [10001] = 1 },
		};

		legacy.MigrateToCurrentSchema();

		Assert.Equal(RunSaveData.CurrentSchemaVersion, legacy.SchemaVersion);
		Assert.Equal(4, legacy.BagEntries.Count); // 2 材料 + 1 道具 + 1 装备
		Assert.Equal(7, RunBagSystem.CountOf(legacy, BagCategory.Material, 101));
		Assert.Equal(2, RunBagSystem.CountOf(legacy, BagCategory.Material, 102));
		Assert.Equal(1, RunBagSystem.CountOf(legacy, BagCategory.Item, 301));
		Assert.Equal(1, RunBagSystem.CountOf(legacy, BagCategory.Equipment, 10001));
		Assert.Equal(RunBagSystem.CarryItemSlotCount, legacy.CarryItemSlots.Count);

		legacy.MigrateToCurrentSchema(); // 幂等：再迁移一次不重复展开
		Assert.Equal(4, legacy.BagEntries.Count);
	}

	[Fact]
	public void ApplyRewardEntry_WritesBothSummaryDictionaryAndBagEntry()
	{
		RegisterStubTables();
		RunSaveData run = new RunSaveData();

		BattleRewardPresenter.ApplyRewardEntryToRun(new DropTableEntry { Category = DropCategory.Material, RewardParam = 101, Amount = 3 }, run);
		BattleRewardPresenter.ApplyRewardEntryToRun(new DropTableEntry { Category = DropCategory.Food, RewardParam = 403, Amount = 1 }, run);
		BattleRewardPresenter.ApplyRewardEntryToRun(new DropTableEntry { Category = DropCategory.Gold, Amount = 50 }, run);

		Assert.Equal(3, run.Materials[101]);                                   // 汇总视图（既有口径不变）
		Assert.Equal(3, RunBagSystem.CountOf(run, BagCategory.Material, 101)); // 实例条目
		Assert.Equal(1, RunBagSystem.CountOf(run, BagCategory.Food, 403));
		Assert.Equal(50, run.Gold);
		foreach (RunBagEntrySave entry in RunBagSystem.EntriesOf(run, BagCategory.Food))
		{
			Assert.Equal(403, entry.DefinitionKey);
			Assert.Equal(2, entry.ExpireDaysRemaining); // 食物实例带有效期（食物系统 §二）
		}
	}

	[Fact]
	public void Add_RejectsInvalidInput_AndKeepsInstanceIdsUnique()
	{
		RegisterStubTables();
		RunSaveData run = new RunSaveData();

		Assert.Null(RunBagSystem.Add(run, BagCategory.Material, 0, 1));
		Assert.Null(RunBagSystem.Add(run, BagCategory.Material, 101, 0));
		Assert.Empty(run.BagEntries);

		List<string> ids = Enumerable.Range(0, 4)
			.Select(_ => RunBagSystem.Add(run, BagCategory.Food, 401, 1).InstanceId)
			.ToList();
		Assert.Equal(ids.Count, ids.Distinct().Count());
	}
}
