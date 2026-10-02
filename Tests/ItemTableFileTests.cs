// ItemTableFileTests.cs
// 物品四表的**文件级**校验（2026-10-02 批 A / 代码需求清单 P1-4）。
// 直接在纯 .NET 测试里读真实 CSV（Tests.csproj 已把它们拷进输出目录），
// 用 ItemEffectSpecParser / ItemCsvSchema 的纯逻辑方法 + 手写拆列校验：表头、ID、效果文本、寿命轴与配方口径。
// 说明：**不**触碰 LoadingSystem / LoadCsv / LoadFoodCsv（Godot 依赖，测试工程没有 GodotSharp 引用），
// 因此运行期「表 → 内存字典」的接线由 `--battlefield-smoke` 的 `BATTLEFIELD_ITEM_TABLES_PASS` 覆盖。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CardSimulator;
using Xunit;

[Collection("ItemNameResolverState")]
public class ItemTableFileTests
{
	private static string[] ReadTable(string name)
	{
		string path = Path.Combine(AppContext.BaseDirectory, name);
		Assert.True(File.Exists(path), $"缺表：{path}（检查 Tests.csproj 的 None Include 是否拷贝）");
		return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
	}

	private static string[] Cells(string line) => line.Split(',');

	private static string Cell(string[] cells, int index) => index >= 0 && index < cells.Length ? cells[index].Trim() : string.Empty;

	private static void AssertHeader(string[] header, params string[] expected) =>
		Assert.Equal(expected, header.Select(x => x.Trim()).ToArray());

	// ── 材料表 ──

	[Fact]
	public void MaterialTable_HeaderIsStable_AndIdsAreUnique()
	{
		string[] rows = ReadTable(Path.Combine("Item", "Material.csv"));
		AssertHeader(Cells(rows[0]), "MaterialId", "DefinitionId", "Category", "SubType", "Rarity", "Load", "Description");

		List<int> ids = new List<int>();
		foreach (string row in rows.Skip(1))
		{
			string[] cells = Cells(row);
			int id = ItemCsvSchema.ParseId(Cell(cells, 0), row);
			ItemCsvSchema.ParseName(Cell(cells, 1), row);
			ItemCsvSchema.ParseMaterialCategory(Cell(cells, 2), row);
			ItemCsvSchema.ParseRarity(Cell(cells, 4), row);
			ItemCsvSchema.ParseNonNegativeFloat(Cell(cells, 5), row);
			Assert.DoesNotContain(id, ids);
			ids.Add(id);
		}

		Assert.Equal(16, ids.Count);
		Assert.Contains(101, ids); // DropTable.csv 的 `Material,101` 引用锚点（P1-4 的悬空引用修复）
	}

	// ── 掉落表 → 物品表引用 ──

	[Fact]
	public void DropTable_References_ResolveAgainstItemTables()
	{
		HashSet<int> materials = ReadTable(Path.Combine("Item", "Material.csv")).Skip(1)
			.Select(row => int.Parse(Cell(Cells(row), 0), CultureInfo.InvariantCulture)).ToHashSet();
		HashSet<int> items = ReadTable(Path.Combine("Item", "Item.csv")).Skip(1)
			.Select(row => int.Parse(Cell(Cells(row), 0), CultureInfo.InvariantCulture)).ToHashSet();
		HashSet<int> foods = ReadTable(Path.Combine("Item", "Food.csv")).Skip(1)
			.Select(row => int.Parse(Cell(Cells(row), 0), CultureInfo.InvariantCulture)).ToHashSet();

		foreach (string row in ReadTable("DropTable.csv").Skip(1))
		{
			string[] cells = Cells(row);
			int id = int.Parse(Cell(cells, 2), CultureInfo.InvariantCulture);
			switch (Cell(cells, 1))
			{
				case "Material":
					Assert.True(materials.Contains(id), $"掉落表引用了未定义材料：{row}");
					break;
				case "Item":
					Assert.True(items.Contains(id), $"掉落表引用了未定义道具：{row}");
					break;
				case "Food":
					Assert.True(foods.Contains(id), $"掉落表引用了未定义食物：{row}");
					break;
			}
		}
	}

	// ── 道具表 ──

	[Fact]
	public void ItemTable_HeaderIsStable_AndEffectsParseOrAreExplicitlyPending()
	{
		string[] rows = ReadTable(Path.Combine("Item", "Item.csv"));
		AssertHeader(Cells(rows[0]), "ItemId", "DefinitionId", "Rarity", "UseScope", "Effect1", "Effect2", "Load", "Description");

		Dictionary<int, string> pendingByItem = new Dictionary<int, string>();
		foreach (string row in rows.Skip(1))
		{
			string[] cells = Cells(row);
			int id = ItemCsvSchema.ParseId(Cell(cells, 0), row);
			ItemCsvSchema.ParseName(Cell(cells, 1), row);
			ItemCsvSchema.ParseRarity(Cell(cells, 2), row);
			ItemCsvSchema.ParseUseScope(Cell(cells, 3), row);

			List<string> notes = new List<string>();
			foreach (int index in new[] { 4, 5 })
			{
				ItemEffectSpec spec = ItemEffectSpecParser.ParseEffect(Cell(cells, index), row, notes);
				if (spec != null)
				{
					Assert.NotEqual(EffectType.None, spec.Type);
				}
			}

			if (notes.Count > 0)
			{
				pendingByItem[id] = string.Join("；", notes);
			}
		}

		// `Pending:` 是**显式占位**（批 D 扩 StateType / TurnStartResourceType 后应为空）：
		// 这里把「当前只能有这三条」钉成断言，任何新增占位都会让测试红，提醒同步文档与词汇。
		Assert.Equal(new[] { 303, 304, 308 }, pendingByItem.Keys.OrderBy(x => x).ToArray());
	}

	// ── 食物表：效果 + 寿命轴（2026-10-02 用户口径 ②）──

	[Fact]
	public void FoodTable_HeaderIsStable_AndEveryEffectCarriesADuration()
	{
		string[] rows = ReadTable(Path.Combine("Item", "Food.csv"));
		AssertHeader(Cells(rows[0]),
			"FoodId", "DefinitionId", "Rarity", "Satiety", "ExpireDays",
			"Effect1", "Effect1Duration", "Effect2", "Effect2Duration", "Load", "Description");

		List<string> effects = new List<string>();
		int rowCount = 0;
		foreach (string row in rows.Skip(1))
		{
			rowCount++;
			string[] cells = Cells(row);
			ItemCsvSchema.ParseId(Cell(cells, 0), row);
			string name = ItemCsvSchema.ParseName(Cell(cells, 1), row);
			ItemCsvSchema.ParseRarity(Cell(cells, 2), row);
			Assert.True(ItemCsvSchema.ParseNonNegativeInt(Cell(cells, 3), row) > 0, $"食物饱食度必须为正：{row}");
			ItemCsvSchema.ParsePositiveInt(Cell(cells, 4), row); // 有效期（天）≥ 1

			foreach ((int effectIndex, int durationIndex) in new[] { (5, 6), (7, 8) })
			{
				ItemEffectSpec spec = ItemEffectSpecParser.ParseEffect(Cell(cells, effectIndex), row);
				if (spec == null)
				{
					continue;
				}

				ItemEffectSpecParser.ApplyDuration(spec, Cell(cells, durationIndex), row);
				Assert.NotEqual(FoodEffectDurationKind.None, spec.DurationKind);
				Assert.True(spec.DurationValue >= 1, $"寿命数量必须 ≥ 1（不填默认 1）：{row}");
				effects.Add($"{name}:{spec.ToText()}");
			}
		}

		Assert.Equal(8, rowCount); // 食物系统 §三 的 8 条食物
		Assert.Contains("香草炖菜:Shield:2@BattleCount:1", effects);
		Assert.Contains("月光浓汤:ClearFirstNormalDebuff@BattleCount:1", effects);
		Assert.Contains("凤凰羽羹:SurviveFatalOnce:20@BattleCount:1", effects);
		Assert.DoesNotContain(effects, x => x.StartsWith("粗麦饼", StringComparison.Ordinal)); // 无附带效果
	}

	// ── 配方表：材料暂不参与烹饪（2026-10-02 用户口径 ①）──

	[Fact]
	public void RecipeTable_EnabledRecipesUseFoodInputsOnly_AndMaterialChannelStaysDisabled()
	{
		string[] rows = ReadTable(Path.Combine("Item", "FoodRecipe.csv"));
		AssertHeader(Cells(rows[0]),
			"RecipeId", "ResultFoodId", "ResultCount",
			"Input1Kind", "Input1Id", "Input1Count", "Input2Kind", "Input2Id", "Input2Count",
			"Enabled", "Description");

		HashSet<int> foods = ReadTable(Path.Combine("Item", "Food.csv")).Skip(1)
			.Select(row => int.Parse(Cell(Cells(row), 0), CultureInfo.InvariantCulture)).ToHashSet();

		int enabled = 0;
		int disabledMaterialChannel = 0;
		foreach (string row in rows.Skip(1))
		{
			string[] cells = Cells(row);
			ItemCsvSchema.ParseId(Cell(cells, 0), row);
			int resultFoodId = ItemCsvSchema.ParseId(Cell(cells, 1), row);
			ItemCsvSchema.ParsePositiveInt(Cell(cells, 2), row);
			bool isEnabled = ItemCsvSchema.ParseBool(Cell(cells, 9), row);

			List<KeyValuePair<RecipeInputKind, int>> inputs = new List<KeyValuePair<RecipeInputKind, int>>();
			foreach ((int kindIndex, int idIndex, int countIndex) in new[] { (3, 4, 5), (6, 7, 8) })
			{
				string kind = Cell(cells, kindIndex);
				string inputId = Cell(cells, idIndex);
				string count = Cell(cells, countIndex);
				if (kind.Length == 0 && inputId.Length == 0)
				{
					continue;
				}

				inputs.Add(new KeyValuePair<RecipeInputKind, int>(
					ItemCsvSchema.ParseRecipeInputKind(kind, row),
					ItemCsvSchema.ParseId(inputId, row)));
				ItemCsvSchema.ParsePositiveInt(count, row);
			}

			Assert.NotEmpty(inputs);

			if (!isEnabled)
			{
				// 材料通道：保留但禁用（2026-10-02 口径，回改 = 翻这一列）
				Assert.All(inputs, input => Assert.Equal(RecipeInputKind.Material, input.Key));
				disabledMaterialChannel++;
				continue;
			}

			enabled++;
			Assert.True(foods.Contains(resultFoodId), $"放行配方的结果食物未定义：{row}");
			Assert.All(inputs, input =>
			{
				Assert.Equal(RecipeInputKind.Food, input.Key); // 材料暂不参与烹饪
				Assert.True(foods.Contains(input.Value), $"放行配方的食物输入未定义：{row}");
			});
		}

		Assert.Equal(5, enabled);
		Assert.Equal(7, disabledMaterialChannel);
	}

	// ── 装备表（2026-10-02 装备界面批，P0-18 界面半）──

	[Fact]
	public void ArmorTable_HeaderIsStable_AndRowsParse()
	{
		string[] rows = ReadTable(Path.Combine("Equipment", "Armor.csv"));
		AssertHeader(Cells(rows[0]), "ArmorId", "DefinitionId", "Slot", "HandsRequired", "DefenseValue", "DamageBonus",
			"MoveBonus", "ResourceCost", "Load");

		List<int> ids = new List<int>();
		List<string> names = new List<string>();
		int accessoryRows = 0;
		foreach (string row in rows.Skip(1))
		{
			string[] cells = Cells(row);
			int id = ItemCsvSchema.ParseId(Cell(cells, 0), row);
			names.Add(ItemCsvSchema.ParseName(Cell(cells, 1), row));
			string slot = Cell(cells, 2);
			Assert.Contains(slot, new[] { "Head", "Body", "Feet", "Accessory" });
			if (slot == "Accessory")
			{
				accessoryRows++;
			}

			ItemCsvSchema.ParseNonNegativeInt(Cell(cells, 3), row);
			ItemCsvSchema.ParseNonNegativeInt(Cell(cells, 4), row);
			ItemCsvSchema.ParseNonNegativeInt(Cell(cells, 5), row);
			ItemCsvSchema.ParseNonNegativeInt(Cell(cells, 6), row);
			ItemCsvSchema.ParseNonNegativeInt(Cell(cells, 7), row);
			Assert.True(ItemCsvSchema.ParseNonNegativeFloat(Cell(cells, 8), row) > 0f, $"Armor 表的 Load 必填：{row}");

			Assert.DoesNotContain(id, ids);
			ids.Add(id);
		}

		Assert.Equal(6, ids.Count);
		Assert.Equal(3, accessoryRows); // 饰品可选件数与配置格数一致（当前各 3）
		Assert.Equal(names.Count, names.Distinct().Count());
		Assert.Contains("布头巾", names);
	}

	[Fact]
	public void ArmorTable_NamesDoNotCollideWithWeaponTable()
	{
		// 两张装备表共用一个「装备」命名空间（名字反查 EquipmentKeysByName）：重名会让反查失真。
		HashSet<string> weaponNames = ReadTable("Weapon.csv").Skip(1)
			.Select(row => Cell(Cells(row), 1)).ToHashSet(StringComparer.Ordinal);

		foreach (string row in ReadTable(Path.Combine("Equipment", "Armor.csv")).Skip(1))
		{
			Assert.DoesNotContain(Cell(Cells(row), 1), weaponNames);
		}
	}

	[Fact]
	public void EquipmentConfigTable_HasSingleGlobalRow_MatchingDefaultAccessorySlots()
	{
		string[] rows = ReadTable(Path.Combine("Equipment", "EquipmentConfig.csv"));
		AssertHeader(Cells(rows[0]), "Scope", "CharacterId", "AccessorySlots");

		List<string[]> dataRows = rows.Skip(1).Select(Cells).ToList();
		string[] global = Assert.Single(dataRows.Where(cells => Cell(cells, 0) == "Global"));
		Assert.Equal(EquipmentConfigDefinition.DefaultAccessorySlotCount,
			int.Parse(Cell(global, 2), CultureInfo.InvariantCulture));
		// 界面 6 格（3 部位 + 饰品 ×3）依赖这个值：改成别的数字必须同时改案文与验收。
		Assert.Equal(3, EquipmentConfigDefinition.DefaultAccessorySlotCount);
	}

	// ── 纯逻辑：效果文本与寿命轴解析 ──

	[Theory]
	[InlineData("Shield:5", EffectType.Shield, 5)]
	[InlineData("DrawCard:1", EffectType.DrawCard, 1)]
	[InlineData("SurviveFatalOnce:20", EffectType.SurviveFatalOnce, 20)]
	[InlineData("ClearFirstNormalDebuff", EffectType.ClearFirstNormalDebuff, 0)]
	public void ParseEffect_ReadsTypeAndParams(string raw, EffectType expected, int firstParam)
	{
		ItemEffectSpec spec = ItemEffectSpecParser.ParseEffect(raw, "test");
		Assert.NotNull(spec);
		Assert.Equal(expected, spec.Type);
		Assert.Equal(firstParam, spec.ParamAt(0));
	}

	[Fact]
	public void ParseEffect_StateNameParam_MapsToEnumNumber()
	{
		ItemEffectSpec spec = ItemEffectSpecParser.ParseEffect("AddState:AddAttack:2", "test");
		Assert.Equal(EffectType.AddState, spec.Type);
		Assert.Equal((int)StateType.AddAttack, spec.ParamAt(0));
		Assert.Equal(2, spec.ParamAt(1));
	}

	[Fact]
	public void ParseEffect_Pending_IsRecordedNotSilentlyDropped()
	{
		List<string> notes = new List<string>();
		Assert.Null(ItemEffectSpecParser.ParseEffect("Pending:每回合多抽 1 张", "test", notes));
		Assert.Single(notes);
		Assert.Equal("每回合多抽 1 张", notes[0]);
	}

	[Theory]
	[InlineData("NotAnEffect:1")]
	[InlineData("None")]
	[InlineData("AddState:NotAState")]
	public void ParseEffect_InvalidText_Throws(string raw) =>
		Assert.Throws<FormatException>(() => ItemEffectSpecParser.ParseEffect(raw, "test"));

	[Theory]
	[InlineData("", FoodEffectDurationKind.BattleCount, 1)] // 不填 = 下一场战斗（用户口径 ②）
	[InlineData("BattleCount:1", FoodEffectDurationKind.BattleCount, 1)]
	[InlineData("TimePoint:2", FoodEffectDurationKind.TimePoint, 2)]
	[InlineData("DayCount:3", FoodEffectDurationKind.DayCount, 3)]
	[InlineData("DayCount", FoodEffectDurationKind.DayCount, 1)]
	public void ParseDuration_ReadsKindAndValue_DefaultingToOne(string raw, FoodEffectDurationKind kind, int value)
	{
		Assert.True(ItemEffectSpecParser.TryParseDuration(raw, out FoodEffectDurationKind actualKind, out int actualValue, "test"));
		Assert.Equal(kind, actualKind);
		Assert.Equal(value, actualValue);
	}

	[Theory]
	[InlineData("Unknown")]
	[InlineData("BattleCount:0")]
	[InlineData("BattleCount:-1")]
	[InlineData("BattleCount:1:2")]
	public void ParseDuration_InvalidText_Throws(string raw) =>
		Assert.Throws<FormatException>(() => ItemEffectSpecParser.TryParseDuration(raw, out _, out _, "test"));

	// ── 文案：名字解析与「未定义」兜底（P1-4 验收）──

	[Fact]
	public void RewardLine_UsesRegisteredNames_AndFallsBackWithCategoryLabel()
	{
		ItemNameResolver.Clear();
		try
		{
			ItemNameResolver.RegisterMaterials(new Dictionary<int, MaterialDefinition>
			{
				[101] = new MaterialDefinition { MaterialId = 101, DefinitionId = "药草" },
			});
			ItemNameResolver.RegisterFoods(new Dictionary<int, FoodDefinition>
			{
				[402] = new FoodDefinition { FoodId = 402, DefinitionId = "香草炖菜" },
			});

			Assert.Equal("材料 药草 ×2", BattleRewardPresenter.FormatRewardLine(
				new DropTableEntry { Category = DropCategory.Material, RewardParam = 101, Amount = 2 }));
			Assert.Equal("食物 香草炖菜 ×1", BattleRewardPresenter.FormatRewardLine(
				new DropTableEntry { Category = DropCategory.Food, RewardParam = 402, Amount = 1 }));
			Assert.Equal("材料 未定义材料(999) ×1", BattleRewardPresenter.FormatRewardLine(
				new DropTableEntry { Category = DropCategory.Material, RewardParam = 999, Amount = 1 }));
		}
		finally
		{
			ItemNameResolver.Clear();
		}
	}
}
