// VillageMerchantPrepTests.cs
// 村庄 / 商人功能的「阻断项清理」文件级 + 纯逻辑校验（2026-10-05）。
// 覆盖四类前置：① 装备两表的 `Rarity` 列（商人装备货架分档 / 锻铁铺费用档）；② 锻铁铺配方表
// `EquipmentRecipe.csv` + 纯逻辑 `EquipmentRecipeCatalog`；③ 状态表 `BlocksGuesthouse` 列（民宿封门口径）；
// ④ 卡牌操作规则与计价 `DeckOps`（商人卡组操作四项共用）。
// 说明：**不**触碰 LoadingSystem / LoadCsv（Godot 依赖，测试工程没有 GodotSharp 引用）——
// 「表 → 内存字典」的运行期接线仍由烟测覆盖；这里只锁「表结构 + 纯逻辑」，把静默退化变成红灯。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardSimulator;
using Xunit;

public class VillageMerchantPrepTests
{
	private static string[] ReadTable(string relativePath)
	{
		string path = Path.Combine(AppContext.BaseDirectory, relativePath);
		Assert.True(File.Exists(path), $"缺表：{path}（检查 Tests.csproj 的 None Include 是否拷贝）");
		return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
	}

	private static string[] Cells(string line) => line.Split(',');

	private static string Cell(string[] cells, int index) => index >= 0 && index < cells.Length ? cells[index].Trim() : string.Empty;

	// ── ① 装备表稀有度列（阻断项 B3）──

	[Fact]
	public void WeaponTable_CarriesRarityColumn_AndEveryRowParses()
	{
		string[] rows = ReadTable("Weapon.csv");
		string[] header = Cells(rows[0]).Select(x => x.Trim()).ToArray();
		Assert.Equal("Rarity", header[^1]);   // 尾部追加列：战斗侧（BattleWeaponCatalog）按前 11 列读数，不受影响

		int rarityColumn = Array.IndexOf(header, "Rarity");
		Assert.Equal(8, rows.Length - 1);
		foreach (string row in rows.Skip(1))
		{
			string[] cells = Cells(row);
			string raw = Cell(cells, rarityColumn);
			// 留空 = 普通；填了就必须是「普通 / 罕见 / 稀有」（与物品四表同一解析口径）。
			Assert.Equal(ItemRarity.Common, raw.Length == 0 ? ItemRarity.Common : ItemCsvSchema.ParseRarity(raw, row));
		}
	}

	// ── ② 锻铁铺 / 商人锻造炉配方表（阻断项 B3）──

	[Fact]
	public void EquipmentRecipeTable_CoversAllCurrentEquipment_WithResolvableMaterials()
	{
		string[] rows = ReadTable(Path.Combine("Equipment", "EquipmentRecipe.csv"));
		Assert.Equal(EquipmentRecipeCatalog.Header, Cells(rows[0]).Select(x => x.Trim()).ToArray());

		HashSet<int> materialIds = ReadTable(Path.Combine("Item", "Material.csv")).Skip(1)
			.Select(row => int.Parse(Cell(Cells(row), 0))).ToHashSet();
		HashSet<string> equipmentNames = ReadTable("Weapon.csv").Skip(1).Select(row => Cell(Cells(row), 1))
			.Concat(ReadTable(Path.Combine("Equipment", "Armor.csv")).Skip(1).Select(row => Cell(Cells(row), 1)))
			.ToHashSet(StringComparer.Ordinal);

		List<EquipmentRecipe> recipes = EquipmentRecipeCatalog.ParseLines(rows);
		Assert.Equal(14, recipes.Count);   // 现役装备 14 件（Weapon 8 + Armor 6）
		Assert.Equal(recipes.Count, recipes.Select(x => x.RecipeId).Distinct().Count());
		Assert.Equal(recipes.Count, recipes.Select(x => x.ResultDefinitionId).Distinct().Count());
		Assert.All(recipes, recipe =>
		{
			Assert.True(recipe.Enabled, $"{recipe.ResultDefinitionId} 的配方应处于放行态（锻铁铺以现役 14 件为准）");
			Assert.True(equipmentNames.Contains(recipe.ResultDefinitionId), $"{recipe.ResultDefinitionId} 不在装备两表里");
			Assert.True(materialIds.Contains(recipe.Material1Id), $"{recipe.ResultDefinitionId} 的第一材料未定义：{recipe.Material1Id}");
			if (recipe.HasSecondMaterial)
			{
				Assert.True(materialIds.Contains(recipe.Material2Id), $"{recipe.ResultDefinitionId} 的第二材料未定义：{recipe.Material2Id}");
			}

			Assert.Contains(recipe.Gold, new[] { 20, 30, 40, 60, 80 });   // 锻铁铺交互案 §三 的金币档
		});
	}

	[Fact]
	public void EquipmentRecipeCatalog_RejectsMalformedRows()
	{
		// 表头不对 / 材料数缺失 / Enabled 非布尔：一律抛 FormatException（不静默降级）。
		Assert.Throws<FormatException>(() => EquipmentRecipeCatalog.ParseLines(new[]
		{
			"RecipeId,ResultDefinitionId,Material1Id,Material1Count,Material2Id,Material2Count,Gold",
			"1,行军短剑,202,2,,,20,TRUE",
		}));

		Assert.Throws<FormatException>(() => EquipmentRecipeCatalog.ParseLines(new[]
		{
			string.Join(",", EquipmentRecipeCatalog.Header),
			"1,行军短剑,,2,,,20,TRUE",
		}));

		Assert.Throws<FormatException>(() => EquipmentRecipeCatalog.ParseLines(new[]
		{
			string.Join(",", EquipmentRecipeCatalog.Header),
			"1,行军短剑,202,2,,,20,YES",
		}));

		// 第三条材料（多给一列）也拦下：表只支持两种材料。
		Assert.Throws<FormatException>(() => EquipmentRecipeCatalog.ParseFields(
			new[] { "1", "行军短剑", "202", "2", "", "", "20", "TRUE", "999" }));
	}

	// ── ③ 状态表民宿封门列（阻断项 B6）──

	[Fact]
	public void StateTable_CarriesBlocksGuesthouseColumn_FalseByDefault()
	{
		string[] rows = ReadTable(Path.Combine("State", "通用State.csv"));
		string[] header = Cells(rows[0]).Select(x => x.Trim()).ToArray();
		int column = Array.IndexOf(header, "BlocksGuesthouse");
		Assert.True(column >= 0, "通用State.csv 缺 BlocksGuesthouse 列（民宿封门判定依赖它）");

		Assert.Equal(23, rows.Length - 1);
		// 本批只落**列结构**（占位）：哪几种状态算「恶名」由内容侧点名（民宿交互案 §十 第 2 条）——
		// 因此这里断言「全部 FALSE」；内容侧点名后改这条断言即可（改列不改代码）。
		Assert.All(rows.Skip(1), row =>
		{
			string raw = Cell(Cells(row), column);
			Assert.True(raw.Length > 0 && bool.TryParse(raw, out bool value) && !value,
				$"占位态下 BlocksGuesthouse 应为 FALSE：{row}");
		});
	}

	// ── ④ 卡牌操作规则与计价（阻断项 B8：RunSession 只调 DeckOps）──

	[Fact]
	public void DeckOps_RemoveAndTransfer_RequireAtLeastTwoCards()
	{
		Assert.True(DeckOps.CanRemoveCard(2, 0, out _));
		Assert.False(DeckOps.CanRemoveCard(1, 0, out string onlyOne));
		Assert.Equal("卡组至少保留 1 张卡。", onlyOne);
		Assert.False(DeckOps.CanRemoveCard(2, 7, out string outOfRange));
		Assert.Equal("该卡不在卡组中（下标越界）。", outOfRange);

		Assert.True(DeckOps.CanTransferCard(3, 1, 3, 2, 4, out _));
		Assert.False(DeckOps.CanTransferCard(3, 1, 3, 2, 0, out string emptyTarget));
		Assert.Equal("目标卡组为空，无法接收。", emptyTarget);
		Assert.False(DeckOps.CanTransferCard(3, 1, 3, 9, 4, out string badSlot));
		Assert.Equal("该角色的卡组当前不可写入。", badSlot);
	}

	[Fact]
	public void DeckOps_Upgrade_StopsAtCap_AndPricesStep()
	{
		Assert.True(DeckOps.CanUpgradeCard(0, out _));
		Assert.True(DeckOps.CanUpgradeCard(DeckOps.MaxPermanentUpgradeLevel - 1, out _));
		Assert.False(DeckOps.CanUpgradeCard(DeckOps.MaxPermanentUpgradeLevel, out string capped));
		Assert.Equal($"该卡已达永久升级上限（+{DeckOps.MaxPermanentUpgradeLevel}）。", capped);

		Assert.Equal(80, DeckOps.UpgradeGold(0));
		Assert.Equal(160, DeckOps.UpgradeGold(1));
		Assert.Equal(240, DeckOps.UpgradeGold(2));

		Assert.Equal(75, DeckOps.RemoveGold);
		Assert.Equal(100, DeckOps.ChangeGold);
		Assert.Equal(60, DeckOps.TransferGold);
		Assert.Equal("金币不足：需要 60，当前 5。", DeckOps.GoldShortText(60, 5));
	}

	// ── ⑤ 商人配表（阻断项：卡包 / 卡价 / 货架价的表结构，商人交互案 §4.2 / §5.1 / §五）──

	[Fact]
	public void MerchantPriceTable_MatchesCaseAnchors()
	{
		List<MerchantPriceRow> prices = MerchantCatalog.ParsePrices(ReadTable(Path.Combine("WorldMarket", "MerchantPrice.csv")));

		Assert.Equal(10, MerchantCatalog.PriceFor(prices, MerchantCategory.Material, ItemRarity.Common));
		Assert.Equal(30, MerchantCatalog.PriceFor(prices, MerchantCategory.Material, ItemRarity.Uncommon));
		Assert.Equal(60, MerchantCatalog.PriceFor(prices, MerchantCategory.Material, ItemRarity.Rare));
		Assert.Equal(20, MerchantCatalog.PriceFor(prices, MerchantCategory.Food, ItemRarity.Common));
		Assert.Equal(40, MerchantCatalog.PriceFor(prices, MerchantCategory.Food, ItemRarity.Uncommon));
		Assert.Equal(80, MerchantCatalog.PriceFor(prices, MerchantCategory.Food, ItemRarity.Rare));
		Assert.Equal(40, MerchantCatalog.PriceFor(prices, MerchantCategory.Equipment, ItemRarity.Common));
		Assert.Equal(80, MerchantCatalog.PriceFor(prices, MerchantCategory.Equipment, ItemRarity.Uncommon));
		Assert.Equal(160, MerchantCatalog.PriceFor(prices, MerchantCategory.Equipment, ItemRarity.Rare));
		Assert.Equal(50, MerchantCatalog.PriceFor(prices, MerchantCategory.Item, ItemRarity.Common));
		Assert.Equal(90, MerchantCatalog.PriceFor(prices, MerchantCategory.Item, ItemRarity.Uncommon));
		Assert.Equal(160, MerchantCatalog.PriceFor(prices, MerchantCategory.Item, ItemRarity.Rare));
		Assert.Equal(80, MerchantCatalog.PriceFor(prices, MerchantCategory.Key, ItemRarity.Common));

		// 钥匙只有「普通」一档（§五 表里只有一行）；其余类目没有的档位必须查不到，不能静默取别的价。
		Assert.Equal(-1, MerchantCatalog.PriceFor(prices, MerchantCategory.Key, ItemRarity.Rare));
	}

	[Fact]
	public void MerchantCardPrices_FollowTierTable_AndPacksDescribeFiveSlots()
	{
		Dictionary<CardTier, int> prices = MerchantCatalog.ParseCardPrices(ReadTable(Path.Combine("WorldMarket", "MerchantCardPrice.csv")));
		Assert.Equal(5, prices.Count);
		Assert.Equal(60, prices[CardTier.D]);
		Assert.Equal(70, prices[CardTier.C]);
		Assert.Equal(85, prices[CardTier.B]);
		Assert.Equal(105, prices[CardTier.A]);
		Assert.Equal(125, prices[CardTier.S]);

		List<MerchantPackRow> packs = MerchantCatalog.ParseCardPacks(ReadTable(Path.Combine("WorldMarket", "MerchantCardPack.csv")));
		Assert.Equal(MerchantCatalog.CardPackCount, packs.Count);
		Assert.Equal(new[] { 1, 2, 3, 4, 5 }, packs.Select(x => x.PackIndex).ToArray());
		Assert.All(packs, pack =>
		{
			Assert.Equal(5, pack.CardCount);                     // 每包固定 5 张（§4.2）
			Assert.Equal(40, pack.TierWeights[CardTier.D]);     // 等级权重缺省 D40 / C30 / B18 / A9 / S3
			Assert.Equal(30, pack.TierWeights[CardTier.C]);
			Assert.Equal(18, pack.TierWeights[CardTier.B]);
			Assert.Equal(9, pack.TierWeights[CardTier.A]);
			Assert.Equal(3, pack.TierWeights[CardTier.S]);
		});

		Assert.All(packs.Take(3), pack =>
		{
			Assert.Equal(MerchantPackKind.Character, pack.Kind);             // 包 1–3 = 槽位专属
			Assert.False(MerchantCatalog.RequiresSlotChoice(pack.Policy));   // 固定入本槽位
		});
		Assert.Equal(MerchantPackKind.Mixed, packs[3].Kind);
		Assert.True(MerchantCatalog.RequiresSlotChoice(packs[3].Policy));    // 包 4 买时选槽位
		Assert.Equal(MerchantPackKind.Generic, packs[4].Kind);
		Assert.True(MerchantCatalog.RequiresSlotChoice(packs[4].Policy));    // 包 5 同理

		Assert.Equal(3, MerchantCatalog.StockSlots(MerchantCategory.Material));   // §4.4 / §4.5 / §4.6 / §4.7
		Assert.Equal(3, MerchantCatalog.StockSlots(MerchantCategory.Food));
		Assert.Equal(3, MerchantCatalog.StockSlots(MerchantCategory.Equipment));
		Assert.Equal(3, MerchantCatalog.StockSlots(MerchantCategory.Item));
		Assert.Equal(1, MerchantCatalog.StockSlots(MerchantCategory.Key));        // 每名商人 1 把（§十 第 6 条）
	}
}

