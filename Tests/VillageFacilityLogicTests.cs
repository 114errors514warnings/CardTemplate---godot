// VillageFacilityLogicTests.cs
// 村庄 5 个设施与商人货架的**纯逻辑**断言（村庄地图交互案 / 旅馆案 / 民宿案 / 树林案 / 锻铁铺案 / 餐厅案 / 商人案）。
// 只锁「规则 + 数值 + 文案」：界面接线由烟测覆盖，这里把静默退化（比例改了、文案改了、价格算错）变成红灯。
// 与 `RunFacilityCostsTests` 同集合：后者会临时改写设施代价的全局运行期取值，同集合 = 串行，不互相踩。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardSimulator;
using Xunit;

[Collection("RunFacilityCosts")]
public class VillageFacilityLogicTests
{
	private static string[] ReadTable(string relativePath)
	{
		string path = Path.Combine(AppContext.BaseDirectory, relativePath);
		Assert.True(File.Exists(path), $"缺表：{path}（检查 Tests.csproj 的 None Include 是否拷贝）");
		return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
	}

	/// <summary>三个槽位、每人 48 最大生命（当前 20）、给定金币与当天已用时间点。</summary>
	private static RunSaveData NewRun(int gold = 10, float usedToday = 0f, int maxHp = 48, int currentHp = 20)
	{
		RunSaveData run = new RunSaveData { Gold = gold, MapState = new RunMapStateSave { TimePoints = usedToday } };
		for (int i = 0; i < 3; i++)
		{
			run.CharacterSlots.Add(new RunCharacterSlotSave { CharacterId = 9000 + i, MaxHp = maxHp, CurrentHp = currentHp });
		}

		return run;
	}

	// ── 旅馆 / 民宿（VillageLodging）──

	[Fact]
	public void Lodging_HealRatios_FollowDayAndNightTable()
	{
		Assert.Equal(0.50f, VillageLodging.HealRatio(true, false));      // 旅馆 · 白天
		Assert.Equal(1.00f, VillageLodging.HealRatio(true, true));       // 旅馆 · 晚上
		Assert.Equal(0.25f, VillageLodging.HealRatio(false, false));     // 民宿 · 白天
		Assert.Equal(0.40f, VillageLodging.HealRatio(false, true));      // 民宿 · 晚上
		Assert.Equal(1, VillageLodging.InnGold);
	}

	[Fact]
	public void Inn_ChargesOneGold_HealsHalf_AndAdvancesToNextDay()
	{
		RunSaveData run = NewRun(gold: 10, usedToday: 0.5f);          // 当天剩余 3.5 → 白天
		Assert.Equal(string.Empty, VillageLodging.ValidateInn(run));
		Assert.Equal("过夜：回复 50% 生命", VillageLodging.DescribeEffect(run, true));
		Assert.Equal("代价：1 金币", VillageLodging.DescribeCost(true));

		List<int> healed = VillageLodging.Apply(run, true, out string error);

		Assert.Equal(string.Empty, error);
		Assert.Equal(9, run.Gold);
		Assert.Equal(new[] { 24, 24, 24 }, healed.ToArray());          // 48 的 50% = 24，三人都回
		Assert.Equal(new[] { 44, 44, 44 }, run.CharacterSlots.Select(s => s.CurrentHp).ToArray());
		Assert.True(run.VillageState.InnUsed);
		Assert.Equal(RunLodgingChoice.Inn, run.VillageState.ChosenLodging);
		Assert.Equal(2, run.MapState.CurrentDay);                      // 推进到新一天（当天剩余作废）
		Assert.Equal(4f, run.MapState.RemainingToday);
	}

	[Fact]
	public void Inn_AtNight_HealsToFull_AndSecondVisitIsRejected()
	{
		RunSaveData run = NewRun(gold: 5, usedToday: 3.1f);            // 当天剩余 0.9 → 晚上
		Assert.True(VillageLodging.IsNight(run));
		Assert.Equal("过夜：回复到满生命（100%）", VillageLodging.DescribeEffect(run, true));

		VillageLodging.Apply(run, true, out string error);
		Assert.Equal(string.Empty, error);
		Assert.Equal(new[] { 48, 48, 48 }, run.CharacterSlots.Select(s => s.CurrentHp).ToArray());
		Assert.Equal(4, run.Gold);

		Assert.Equal(VillageLodging.InnUsedText, VillageLodging.ValidateInn(run));      // 本局只能进一次
		List<int> again = VillageLodging.Apply(run, true, out string second);
		Assert.Equal(VillageLodging.InnUsedText, second);
		Assert.Empty(again);
		Assert.Equal(4, run.Gold);                                                      // 被拒时不扣钱
	}

	[Fact]
	public void Inn_RejectsWhenGuesthouseChosen_OrGoldShort()
	{
		RunSaveData choseGuesthouse = NewRun(gold: 10);
		choseGuesthouse.VillageState.ChosenLodging = RunLodgingChoice.Guesthouse;
		Assert.Equal(VillageLodging.ChoseGuesthouseText, VillageLodging.ValidateInn(choseGuesthouse));

		RunSaveData broke = NewRun(gold: 0);
		Assert.Equal("金币不足：需要 1，当前 0。", VillageLodging.ValidateInn(broke));
		VillageLodging.Apply(broke, true, out string error);
		Assert.Equal("金币不足：需要 1，当前 0。", error);
		Assert.False(broke.VillageState.InnUsed);
		Assert.Equal(1, broke.MapState.CurrentDay);                                     // 失败不推天
	}

	[Fact]
	public void Guesthouse_IsFree_OncePerDay_AndMutuallyExclusiveWithInn()
	{
		RunSaveData run = NewRun(gold: 7, usedToday: 3.1f);           // 晚上 → 40%
		Assert.Equal(string.Empty, VillageLodging.ValidateGuesthouse(run));
		Assert.Equal("过夜：回复 40% 生命", VillageLodging.DescribeEffect(run, false));
		Assert.Equal("代价：无", VillageLodging.DescribeCost(false));

		List<int> healed = VillageLodging.Apply(run, false, out string error);

		Assert.Equal(string.Empty, error);
		Assert.Equal(7, run.Gold);                                     // 免费：金币不动
		Assert.Equal(new[] { 20, 20, 20 }, healed.ToArray());          // 48 × 40% = 19.2 → 20
		Assert.Equal(1, run.VillageState.GuesthouseUsedDay);            // 记「住的那一天」
		Assert.Equal(RunLodgingChoice.Guesthouse, run.VillageState.ChosenLodging);
		Assert.Equal(2, run.MapState.CurrentDay);

		// 同一天住第二次 → 拒（把记日改回当天来模拟同一天内重复进入）
		run.VillageState.GuesthouseUsedDay = run.MapState.CurrentDay;
		Assert.Equal(VillageLodging.GuesthouseUsedTodayText, VillageLodging.ValidateGuesthouse(run));

		// 本局已选旅馆 → 民宿拒
		RunSaveData choseInn = NewRun();
		choseInn.VillageState.ChosenLodging = RunLodgingChoice.Inn;
		Assert.Equal(VillageLodging.ChoseInnText, VillageLodging.ValidateGuesthouse(choseInn));
	}

	[Fact]
	public void Guesthouse_BlockedByEventFlag_OrDebuffName()
	{
		RunSaveData byEvent = NewRun();
		byEvent.VillageState.GuesthouseLockedByEvent = true;
		Assert.Equal(VillageLodging.GuesthouseLockedByEventText, VillageLodging.ValidateGuesthouse(byEvent));

		RunSaveData byDebuff = NewRun();
		byDebuff.VillageState.GuesthouseDebuffBlockName = "通缉";
		Assert.Equal("民宿因你们的恶名（通缉）拒绝收留。", VillageLodging.ValidateGuesthouse(byDebuff));

		// 封门时不改状态：金币 / 天数都不动
		VillageLodging.Apply(byEvent, false, out string error);
		Assert.Equal(VillageLodging.GuesthouseLockedByEventText, error);
		Assert.Equal(1, byEvent.MapState.CurrentDay);
		Assert.Equal(-1, byEvent.VillageState.GuesthouseUsedDay);
	}

	// ── 树林（VillageForage）──

	[Fact]
	public void Forage_RollsTwoPicks_AndMergesSameMaterialIntoSingleEntry()
	{
		List<ForageMaterialEntry> pool = new List<ForageMaterialEntry>
		{
			new ForageMaterialEntry { MaterialId = 101, DefinitionId = "药草", Rarity = ItemRarity.Common },
		};

		List<(int MaterialId, int Count)> gained = VillageForage.Roll(new Random(20261005), pool);

		Assert.Single(gained);
		Assert.Equal(101, gained[0].MaterialId);
		Assert.Equal(2, gained[0].Count);        // 同材料合并成一条 ×2（不是两条 ×1）
		Assert.Equal(2, VillageForage.PicksPerSearch);
	}

	[Fact]
	public void Forage_MixedPool_ReturnsOnlyPoolEntries_AndExactlyTwoPicks()
	{
		List<ForageMaterialEntry> pool = new List<ForageMaterialEntry>
		{
			new ForageMaterialEntry { MaterialId = 101, Rarity = ItemRarity.Common },
			new ForageMaterialEntry { MaterialId = 103, Rarity = ItemRarity.Uncommon },
			new ForageMaterialEntry { MaterialId = 104, Rarity = ItemRarity.Rare },
		};

		Random random = new Random(7);
		for (int i = 0; i < 200; i++)
		{
			List<(int MaterialId, int Count)> gained = VillageForage.Roll(random, pool);
			Assert.Equal(2, gained.Sum(x => x.Count));
			Assert.All(gained, x => Assert.Contains(x.MaterialId, new[] { 101, 103, 104 }));
		}
	}

	[Fact]
	public void Forage_RarityWeights_AreSeventyTwentyFiveFive()
	{
		Assert.Equal((ItemRarity.Common, 70), VillageForage.RarityWeights[0]);
		Assert.Equal((ItemRarity.Uncommon, 25), VillageForage.RarityWeights[1]);
		Assert.Equal((ItemRarity.Rare, 5), VillageForage.RarityWeights[2]);
		Assert.Equal(100, VillageForage.TotalWeight);

		List<ForageMaterialEntry> pool = new List<ForageMaterialEntry>
		{
			new ForageMaterialEntry { MaterialId = 101, Rarity = ItemRarity.Common },
			new ForageMaterialEntry { MaterialId = 103, Rarity = ItemRarity.Uncommon },
			new ForageMaterialEntry { MaterialId = 104, Rarity = ItemRarity.Rare },
		};

		Random random = new Random(20261005);
		int common = 0, uncommon = 0, rare = 0;
		const int samples = 20000;
		for (int i = 0; i < samples; i++)
		{
			switch (VillageForage.RollRarity(random, pool))
			{
				case ItemRarity.Common: common++; break;
				case ItemRarity.Uncommon: uncommon++; break;
				default: rare++; break;
			}
		}

		Assert.InRange(common / (double)samples, 0.68, 0.72);       // 70% ± 2%
		Assert.InRange(uncommon / (double)samples, 0.23, 0.27);     // 25% ± 2%
		Assert.InRange(rare / (double)samples, 0.03, 0.07);         // 5% ± 2%
	}

	[Fact]
	public void Forage_UsesItsOwnForestCost_AndEntryIsNotAnOperation()
	{
		// 2026-10-05 第四轮口径（用户裁定）：进入树林本身不是操作；每次搜寻按**树林专属**表值收时间点
		// （`ForestForageTimePointCost`），与村庄其他操作的表值（`VillageOperationTimePointCost`）分开配。
		// 表值与代码兜底是否一致由 `GameVariablesTableTests` 对 CSV 直接校验。
		Assert.NotEqual(VillageVisit.OperationTimePointCost, VillageForage.TimePointCost);

		// 门槛边界全部从代价现算（表值调整后断言不用跟着改）：
		float cost = VillageForage.TimePointCost;
		Assert.True(VillageForage.CanSearch(cost));            // 剩余正好 = 一次搜寻 → 可搜寻
		Assert.False(VillageForage.CanSearch(cost / 2f));      // 剩余只有一半 → 不能搜寻（先去旅馆 / 民宿过夜）
		Assert.Equal($"时间点不足：需要 {RunTimePoints.Format(cost)}，当前剩余 0.4。请前往旅馆或民宿过夜，或回营地结束当天。",
			VillageForage.SearchTimePointShortText(0.4f));
	}

	// ── 锻铁铺 / 商人锻造炉（SmithyCrafting）──

	[Fact]
	public void Smithy_GoldMultiplier_RoundsUpToFive()
	{
		Assert.Equal(20, SmithyCrafting.GoldFor(20, SmithyCrafting.VillageGoldMultiplier));
		Assert.Equal(30, SmithyCrafting.GoldFor(20, SmithyCrafting.MerchantGoldMultiplier));   // 20 × 1.5
		Assert.Equal(60, SmithyCrafting.GoldFor(40, SmithyCrafting.MerchantGoldMultiplier));   // 40 × 1.5 = 60（容差不把它抬到 65）
		Assert.Equal(90, SmithyCrafting.GoldFor(60, SmithyCrafting.MerchantGoldMultiplier));
		Assert.Equal(120, SmithyCrafting.GoldFor(80, SmithyCrafting.MerchantGoldMultiplier));
		Assert.Equal(45, SmithyCrafting.GoldFor(30, SmithyCrafting.MerchantGoldMultiplier));
		Assert.Equal(0, SmithyCrafting.GoldFor(0, SmithyCrafting.MerchantGoldMultiplier));
		Assert.Equal(2, SmithyCrafting.VillageMaxCrafts);
		Assert.Equal(1, SmithyCrafting.MerchantMaxCrafts);

		// 2026-10-05 第四轮口径：每次打造 = 一次「操作」= 村庄操作表值的时间点（默认 0.1）+ 配方金币。
		float cost = SmithyCrafting.CraftTimePointCost;
		Assert.Equal(VillageVisit.OperationTimePointCost, cost);
		Assert.True(SmithyCrafting.CanCraft(cost));            // 剩余正好 = 一次操作 → 可打造
		Assert.False(SmithyCrafting.CanCraft(cost / 2f));      // 剩余不足一次操作 → 不能打造（先去旅馆 / 民宿过夜）
	}

	[Fact]
	public void Smithy_Requirements_ComeFromRecipeTable_AndValidateOrderIsCraftsThenMaterialThenGold()
	{
		List<EquipmentRecipe> recipes = EquipmentRecipeCatalog.ParseLines(ReadTable(Path.Combine("Equipment", "EquipmentRecipe.csv")));
		EquipmentRecipe sword = recipes.First(recipe => recipe.ResultDefinitionId == "行军短剑");
		Dictionary<int, string> names = ReadTable(Path.Combine("Item", "Material.csv")).Skip(1)
			.ToDictionary(row => int.Parse(Cell(row, 0)), row => Cell(row, 1));

		List<SmithyRequirement> short1 = SmithyCrafting.Requirements(
			sword,
			id => names.TryGetValue(id, out string name) ? name : id.ToString(),
			id => id == sword.Material1Id ? 1 : 0);

		Assert.Single(short1);
		Assert.Equal(2, short1[0].Need);
		Assert.False(short1[0].Satisfied);
		Assert.Equal(20, sword.Gold);

		// 校验顺序：次数 → 材料 → 金币
		Assert.Equal(SmithyCrafting.CraftsExhaustedText, SmithyCrafting.Validate(0, 100, 20, short1));
		Assert.Equal("材料不足：铁矿石 需要 2，持有 1。", SmithyCrafting.Validate(1, 100, 20, short1));

		List<SmithyRequirement> enough = SmithyCrafting.Requirements(
			sword,
			id => names.TryGetValue(id, out string name) ? name : id.ToString(),
			id => id == sword.Material1Id ? 2 : 0);

		Assert.True(enough[0].Satisfied);
		Assert.Equal("金币不足：需要 20，当前 5。", SmithyCrafting.Validate(1, 5, 20, enough));
		Assert.Equal(string.Empty, SmithyCrafting.Validate(1, 20, 20, enough));
		Assert.Equal("打造完成：行军短剑", SmithyCrafting.CraftedText(sword.ResultDefinitionId));

		// 扣料按需求逐条走（数量来自配方，不是「各扣 1」）
		List<(int MaterialId, int Count)> consumed = new List<(int, int)>();
		SmithyCrafting.Consume(enough, (id, count) => consumed.Add((id, count)));
		Assert.Equal(new[] { (sword.Material1Id, 2) }, consumed.ToArray());
	}

	// ── 餐厅（RestaurantTrade）──

	[Fact]
	public void Restaurant_SellPrices_AreFortyPercent_WithFreshBonus_AndArbitrageFree()
	{
		Assert.Equal(8, RestaurantTrade.SellPrice(20, false));
		Assert.Equal(12, RestaurantTrade.SellPrice(20, true));
		Assert.Equal(16, RestaurantTrade.SellPrice(40, false));
		Assert.Equal(24, RestaurantTrade.SellPrice(40, true));
		Assert.Equal(32, RestaurantTrade.SellPrice(80, false));
		Assert.Equal(48, RestaurantTrade.SellPrice(80, true));
		Assert.Equal(0, RestaurantTrade.SellPrice(0, true));

		foreach (int buy in new[] { 20, 40, 80 })
		{
			Assert.True(RestaurantTrade.IsArbitrageFree(buy), $"{buy} 买进再卖出必须亏");
		}

		Assert.Equal(2, RestaurantTrade.CooksPerVisit);

		// 2026-10-05 第四轮口径：烹饪与**点菜（买入）**各算一次「操作」，各收一次村庄操作表值的时间点
		// （默认 0.1）+（点菜另按菜价）金币；出售是纯交易，不收时间点。
		float cost = RestaurantTrade.CookTimePointCost;
		Assert.Equal(VillageVisit.OperationTimePointCost, cost);
		Assert.Equal(cost, RestaurantTrade.OrderTimePointCost);
		Assert.True(RestaurantTrade.CanCook(cost));            // 剩余正好 = 一次操作 → 可烹饪
		Assert.False(RestaurantTrade.CanCook(cost / 2f));      // 剩余不足 → 不能烹饪
		Assert.True(RestaurantTrade.CanOrder(cost));
		Assert.False(RestaurantTrade.CanOrder(cost / 2f));
		Assert.Equal($"时间点不足：需要 {RunTimePoints.Format(cost)}，当前剩余 0.4。请前往旅馆或民宿过夜，或回营地结束当天。",
			RestaurantTrade.OrderTimePointShortText(0.4f));
	}

	[Fact]
	public void Restaurant_BuyPrices_SharedWithMerchantTable()
	{
		List<MerchantPriceRow> prices = MerchantCatalog.ParsePrices(ReadTable(Path.Combine("WorldMarket", "MerchantPrice.csv")));
		Assert.Equal(20, MerchantCatalog.PriceFor(prices, MerchantCategory.Food, ItemRarity.Common));
		Assert.Equal(40, MerchantCatalog.PriceFor(prices, MerchantCategory.Food, ItemRarity.Uncommon));
		Assert.Equal(80, MerchantCatalog.PriceFor(prices, MerchantCategory.Food, ItemRarity.Rare));
		Assert.Equal(12, RestaurantTrade.SellPrice(MerchantCatalog.PriceFor(prices, MerchantCategory.Food, ItemRarity.Common), true));
	}

	[Fact]
	public void Restaurant_FreshMarks_AreInstanceScoped_AndDieOnLeaving()
	{
		RestaurantTrade.FreshMarks marks = new RestaurantTrade.FreshMarks();
		marks.Mark("bag-food401-1");
		Assert.True(marks.IsFresh("bag-food401-1"));
		Assert.False(marks.IsFresh("bag-food401-2"));
		Assert.False(marks.IsFresh(string.Empty));
		Assert.Equal(1, marks.Count);

		marks.Clear();                                   // 离开餐厅即失效（待拍板第 3 条默认值）
		Assert.False(marks.IsFresh("bag-food401-1"));
	}

	[Fact]
	public void Restaurant_ValidateCook_ReasonOrder()
	{
		List<SmithyRequirement> missing = new List<SmithyRequirement>
		{
			new SmithyRequirement { MaterialId = 202, DisplayName = "铁矿石", Need = 2, Have = 1 },
		};

		Assert.Equal(RestaurantTrade.CooksExhaustedText, RestaurantTrade.ValidateCook(0, missing, true));
		Assert.Equal("材料不足：铁矿石 需要 2，持有 1。", RestaurantTrade.ValidateCook(1, missing, true));
		Assert.Equal(RestaurantTrade.BagOverloadText, RestaurantTrade.ValidateCook(1, null, false));
		Assert.Equal(string.Empty, RestaurantTrade.ValidateCook(1, null, true));
	}

	// ── 商人货架（MerchantStock）──

	private static string Cell(string line, int index)
	{
		string[] cells = line.Split(',');
		return index >= 0 && index < cells.Length ? cells[index].Trim() : string.Empty;
	}

	/// <summary>用现役四张表 + 钥匙（§十 第 6 条：每名商人 1 把、80 金币、无表行）拼一份候选池。</summary>
	private static List<MerchantStockCandidate> BuildCatalog()
	{
		List<MerchantStockCandidate> catalog = new List<MerchantStockCandidate>();
		AddRows(catalog, Path.Combine("Item", "Material.csv"), MerchantCategory.Material, 0, 1, 4);
		AddRows(catalog, Path.Combine("Item", "Food.csv"), MerchantCategory.Food, 0, 1, 2);
		AddRows(catalog, Path.Combine("Item", "Item.csv"), MerchantCategory.Item, 0, 1, 2);
		AddRows(catalog, "Weapon.csv", MerchantCategory.Equipment, 0, 1, -1);
		AddRows(catalog, Path.Combine("Equipment", "Armor.csv"), MerchantCategory.Equipment, 0, 1, -1);
		catalog.Add(new MerchantStockCandidate { Category = MerchantCategory.Key, DefinitionKey = 0, DefinitionId = "钥匙", Rarity = ItemRarity.Common });
		return catalog;
	}

	/// <summary>`idIndex = 0` 且装备表没有数字主键 → DefinitionKey 取 0；`rarityIndex = -1` 表示稀有度在最后一列。</summary>
	private static void AddRows(List<MerchantStockCandidate> catalog, string relativePath, MerchantCategory category, int idIndex, int nameIndex, int rarityIndex)
	{
		string[] rows = ReadTable(relativePath);
		string[] header = rows[0].Split(',').Select(x => x.Trim()).ToArray();
		foreach (string row in rows.Skip(1))
		{
			string raw = Cell(row, rarityIndex < 0 ? header.Length + rarityIndex : rarityIndex);
			int id = int.TryParse(Cell(row, idIndex), out int parsed) && header[idIndex].EndsWith("Id") ? parsed : 0;
			catalog.Add(new MerchantStockCandidate
			{
				Category = category,
				DefinitionKey = id,
				DefinitionId = Cell(row, nameIndex),
				Rarity = raw.Length == 0 ? ItemRarity.Common : ItemCsvSchema.ParseRarity(raw, row),
			});
		}
	}

	[Fact]
	public void MerchantStock_GeneratesThreeSlotsPerCategory_PlusOneKeySlot()
	{
		List<MerchantPriceRow> prices = MerchantCatalog.ParsePrices(ReadTable(Path.Combine("WorldMarket", "MerchantPrice.csv")));
		List<RunMerchantStockEntrySave> stock = MerchantStock.Generate(new Random(20261005), BuildCatalog(), prices);

		Assert.Equal(3, MerchantStock.CountInCategory(stock, MerchantCategory.Material));
		Assert.Equal(3, MerchantStock.CountInCategory(stock, MerchantCategory.Food));
		Assert.Equal(3, MerchantStock.CountInCategory(stock, MerchantCategory.Equipment));
		Assert.Equal(3, MerchantStock.CountInCategory(stock, MerchantCategory.Item));
		Assert.Equal(1, MerchantStock.CountInCategory(stock, MerchantCategory.Key));   // 每名商人 1 把
		Assert.Equal(13, stock.Count);

		foreach (MerchantCategory category in MerchantStock.CategoryOrder)
		{
			List<RunMerchantStockEntrySave> entries = stock.Where(x => x.Category == (int)category).ToList();
			Assert.Equal(Enumerable.Range(0, entries.Count), entries.Select(x => x.SlotIndex).OrderBy(x => x).ToArray());   // 格序号连续
			Assert.All(entries, entry => Assert.False(entry.Sold));
			Assert.All(entries, entry => Assert.True(entry.Price > 0, $"{entry.DefinitionId} 无价"));
			Assert.Equal(entries.Count, entries.Select(x => x.DefinitionId).Distinct().Count());   // 同物品不重复占两格
		}

		Assert.Equal(80, MerchantStock.FindEntry(stock, MerchantCategory.Key, 0).Price);   // §五：钥匙 80
		Assert.Null(MerchantStock.FindEntry(stock, MerchantCategory.Key, 1));               // 只有 1 格
	}

	[Fact]
	public void MerchantStock_SameSeedSameSnapshot_AndUnknownPriceRarityIsNotShelved()
	{
		List<MerchantPriceRow> prices = MerchantCatalog.ParsePrices(ReadTable(Path.Combine("WorldMarket", "MerchantPrice.csv")));
		string[] Snapshot(List<RunMerchantStockEntrySave> stock) => stock
			.Select(x => $"{x.Category}/{x.SlotIndex}/{x.DefinitionId}/{x.Price}")
			.ToArray();

		List<MerchantStockCandidate> catalog = BuildCatalog();
		catalog.Add(new MerchantStockCandidate { Category = MerchantCategory.Key, DefinitionKey = 0, DefinitionId = "稀有钥匙", Rarity = ItemRarity.Rare });

		Assert.Equal(
			Snapshot(MerchantStock.Generate(new Random(42), catalog, prices)),
			Snapshot(MerchantStock.Generate(new Random(42), catalog, prices)));

		// 价格表只有「钥匙 · 普通」一行（B17 的 PriceFor(Key, Rare) = -1）→ 稀有钥匙不上架，也不静默借用别的档价
		List<RunMerchantStockEntrySave> stock = MerchantStock.Generate(new Random(7), catalog, prices);
		Assert.DoesNotContain(stock, entry => entry.DefinitionId == "稀有钥匙");
		Assert.Equal(1, MerchantStock.CountInCategory(stock, MerchantCategory.Key));
	}

	// ── 餐厅价目（餐厅案 §五：普通 / 罕见 / 稀有 = 20 / 40 / 80，且买进来再卖一定亏）──

	[Fact]
	public void RestaurantFoodPrice_MatchesPriceTable_AndIsArbitrageFree()
	{
		Assert.Equal(20, RestaurantTrade.FoodBuyPrice((int)ItemRarity.Common));
		Assert.Equal(40, RestaurantTrade.FoodBuyPrice((int)ItemRarity.Uncommon));
		Assert.Equal(80, RestaurantTrade.FoodBuyPrice((int)ItemRarity.Rare));
		Assert.Equal(4, RestaurantTrade.BuyShelfSlots);

		foreach (ItemRarity rarity in new[] { ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare })
		{
			int buy = RestaurantTrade.FoodBuyPrice((int)rarity);
			Assert.True(RestaurantTrade.IsArbitrageFree(buy), $"{rarity} 档买进来再卖掉不应赚钱");
			Assert.Equal((int)Math.Floor(buy * 0.4 + 1e-6), RestaurantTrade.SellPrice(buy, false));
			Assert.Equal((int)Math.Floor(buy * 0.4 * 1.5 + 1e-6), RestaurantTrade.SellPrice(buy, true));
		}
	}
}
