// VillagePlaceData.cs
// 村庄 / 商人场景**运行期**的候选池构造（读已加载的配表 → 纯逻辑要吃的 DTO）。
// 只在场景 / `RunSession` 侧调用；纯逻辑（`VillageForage` / `SmithyCrafting` / `MerchantStock`）仍只吃 DTO，
// 这样规则的 xUnit 直测不需要起 Godot、也不需要读表。
using Godot;
using System;
using System.Collections.Generic;
using CardSimulator;

public static class VillagePlaceData
{
	/// <summary>
	/// 树林搜寻池（树林案 §三：现役材料表全表，每层同一张池）。
	/// 顺带按 `MaterialId` 升序固定顺序，抽样结果与表行序无关（只受随机源影响）。
	/// </summary>
	public static List<ForageMaterialEntry> ForagePool()
	{
		List<ForageMaterialEntry> pool = new List<ForageMaterialEntry>();
		foreach (KeyValuePair<int, MaterialDefinition> pair in LoadingSystem.LoadMaterialsByKey())
		{
			MaterialDefinition definition = pair.Value;
			if (definition == null)
			{
				continue;
			}

			pool.Add(new ForageMaterialEntry
			{
				MaterialId = definition.MaterialId,
				DefinitionId = definition.DefinitionId,
				Rarity = definition.Rarity,
			});
		}

		pool.Sort((a, b) => a.MaterialId.CompareTo(b.MaterialId));
		return pool;
	}

	/// <summary>
	/// 材料显示名（材料表）。查不到时走 `DisplayMaterial` 的显式兜底 `未定义材料(ID)` ——
	/// 旧路径 `NameOf(BagCategory.Material, id)` 静默返回空串，会让锻铁铺出现「消耗 ×2」这种没有名字的行（P1-1，2026-10-07 改）。
	/// </summary>
	public static string MaterialName(int materialId) => ItemNameResolver.DisplayMaterial(materialId);

	/// <summary>
	/// 一次抽样用的随机源：种子绑在（本局种子 × 天数 × 用途盐）上 ——
	/// 同一局同一天走同一处设施会得到同一串结果，读档重抽可复现（与 `MerchantStock` 的确定性口径同源）。
	/// </summary>
	public static Random RandomFor(RunSaveData run, int salt)
	{
		int runSeed = run?.MapState?.Seed ?? 0;
		int day = run?.MapState?.CurrentDay ?? 1;
		return new Random(unchecked(runSeed * 397 ^ day * 31 ^ salt));
	}

	// ── 商人候选池（2026-10-07，商人交互案 §4.4–§4.7 货架 / §4.2 卡包 / §五 价格）──
	// 与上面的树林池同一分工：本文件只把**已加载的配表**翻成纯逻辑要吃的 DTO；
	// 规则（抽样、格数、计价）住 `MerchantStock` / `MerchantCardPacks` / `MerchantCatalog`。

	/// <summary>钥匙在货架上的显示名（钥匙不走背包、没有定义表，只有 `RunSaveData.Keys` 计数）。</summary>
	public const string KeyStockName = "钥匙";

	/// <summary>商人一次抽样的随机源（绑本局种子 + 天数 + 用途盐 → 读档重进得到同一份快照）。</summary>
	public static Random MerchantRandom(RunSaveData run, int salt) => RandomFor(run, salt);

	/// <summary>
	/// 货架候选：材料 / 食物 / 道具三张现役表逐行 + 装备两表（武器 + 部位装）+ 钥匙 1 条。
	/// 顺序按「类目 → 定义键」升序固定，抽样结果与表行序无关（只受随机源影响）。
	/// </summary>
	public static List<MerchantStockCandidate> MerchantStockCandidates()
	{
		List<MerchantStockCandidate> list = new List<MerchantStockCandidate>();
		foreach (KeyValuePair<int, MaterialDefinition> pair in LoadingSystem.LoadMaterialsByKey())
		{
			MaterialDefinition row = pair.Value;
			if (row != null)
			{
				list.Add(new MerchantStockCandidate
				{
					Category = MerchantCategory.Material,
					DefinitionKey = row.MaterialId,
					DefinitionId = row.DefinitionId,
					Rarity = row.Rarity,
				});
			}
		}

		foreach (KeyValuePair<int, FoodDefinition> pair in LoadingSystem.LoadFoodsByKey())
		{
			FoodDefinition row = pair.Value;
			if (row != null)
			{
				list.Add(new MerchantStockCandidate
				{
					Category = MerchantCategory.Food,
					DefinitionKey = row.FoodId,
					DefinitionId = row.DefinitionId,
					Rarity = row.Rarity,
				});
			}
		}

		foreach (KeyValuePair<int, ItemDefinition> pair in LoadingSystem.LoadItemsByKey())
		{
			ItemDefinition row = pair.Value;
			if (row != null)
			{
				list.Add(new MerchantStockCandidate
				{
					Category = MerchantCategory.Item,
					DefinitionKey = row.ItemId,
					DefinitionId = row.DefinitionId,
					Rarity = row.Rarity,
				});
			}
		}

		foreach (KeyValuePair<int, WeaponDefinition> pair in LoadingSystem.LoadWeaponsByKey())
		{
			WeaponDefinition row = pair.Value;
			if (row != null)
			{
				list.Add(new MerchantStockCandidate
				{
					Category = MerchantCategory.Equipment,
					DefinitionKey = row.WeaponId,
					DefinitionId = row.DefinitionId,
					Rarity = row.Rarity,
				});
			}
		}

		foreach (KeyValuePair<int, ArmorDefinition> pair in LoadingSystem.LoadArmorsByKey())
		{
			ArmorDefinition row = pair.Value;
			if (row != null)
			{
				list.Add(new MerchantStockCandidate
				{
					Category = MerchantCategory.Equipment,
					DefinitionKey = row.ArmorId,
					DefinitionId = row.DefinitionId,
					Rarity = row.Rarity,
				});
			}
		}

		list.Add(new MerchantStockCandidate
		{
			Category = MerchantCategory.Key,
			DefinitionKey = 0,
			DefinitionId = KeyStockName,
			Rarity = ItemRarity.Common,
		});

		list.Sort((a, b) =>
		{
			int byCategory = ((int)a.Category).CompareTo((int)b.Category);
			return byCategory != 0 ? byCategory : a.DefinitionKey.CompareTo(b.DefinitionKey);
		});
		return list;
	}

	/// <summary>商人货架价表（`Data.WorldMarket.Price` = `MerchantPrice.csv`）。</summary>
	public static List<MerchantPriceRow> MerchantPrices() =>
		MerchantCatalog.ParsePrices(LoadCsv.LoadCSVLines(LoadingSystem.GetFilePathByKey("Data.WorldMarket.Price")));

	/// <summary>商人卡价表（`Data.WorldMarket.CardPrice` = `MerchantCardPrice.csv`，按等级 D–S）。</summary>
	public static Dictionary<CardTier, int> MerchantCardPrices() =>
		MerchantCatalog.ParseCardPrices(LoadCsv.LoadCSVLines(LoadingSystem.GetFilePathByKey("Data.WorldMarket.CardPrice")));

	/// <summary>商人卡包结构表（`Data.WorldMarket.CardPack` = `MerchantCardPack.csv`）。</summary>
	public static List<MerchantPackRow> MerchantPackRows() =>
		MerchantCatalog.ParseCardPacks(LoadCsv.LoadCSVLines(LoadingSystem.GetFilePathByKey("Data.WorldMarket.CardPack")));

	/// <summary>
	/// 某槽位的角色卡池（商人案 §4.2 包 1–3：该角色在 `CharacterRewardPool.csv` 里的**全部来源表**，含通用表）。
	/// 与 `LoadingSystem.GetCharacterRewardCardIds` 的区别：那个是**掉落候选**（只放 B–S 级专属牌），
	/// 而商人卡包按 §4.2 的等级权重（D 40 / C 30 / B 18 / A 9 / S 3）抽样，因此这里取**整表原样**。
	/// </summary>
	public static List<MerchantCardCandidate> MerchantSlotCardPool(RunSaveData run, int slot)
	{
		List<MerchantCardCandidate> pool = new List<MerchantCardCandidate>();
		List<RunCharacterSlotSave> slots = run?.CharacterSlots;
		if (slots == null || slot < 0 || slot >= slots.Count)
		{
			return pool;
		}

		int characterId = slots[slot].CharacterId;
		LoadingSystem.LoadCharacterRewardPoolByKey();
		foreach (CharacterRewardSource source in LoadingSystem.CharacterRewardSources)
		{
			if (source == null || source.CharacterId != characterId || string.IsNullOrWhiteSpace(source.CardSource))
			{
				continue;
			}

			AppendCardsFromSource(pool, source.CardSource);
		}

		return pool;
	}

	/// <summary>三个槽位的角色卡池（索引 = 槽位；包 1–3 与包 4 的混合池都从这里取）。</summary>
	public static List<List<MerchantCardCandidate>> MerchantSlotCardPools(RunSaveData run)
	{
		List<List<MerchantCardCandidate>> pools = new List<List<MerchantCardCandidate>>();
		int slotCount = run?.CharacterSlots?.Count ?? 0;
		for (int slot = 0; slot < slotCount; slot++)
		{
			pools.Add(MerchantSlotCardPool(run, slot));
		}

		return pools;
	}

	/// <summary>通用卡池（商人案 §4.2 第 5 包：3 张 A 级 + 2 张 S 级，全部取自通用牌）。</summary>
	public static List<MerchantCardCandidate> MerchantGenericCardPool()
	{
		List<MerchantCardCandidate> pool = new List<MerchantCardCandidate>();
		AppendCardsFromPath(pool, LoadingSystem.GetFilePathByKey("Data.Card.Common"));
		return pool;
	}

	/// <summary>把一个角色来源表（`CharacterRewardPool.csv` 的 `CardSource`，相对 `DataBase/Card/`）读成候选卡。</summary>
	private static void AppendCardsFromSource(List<MerchantCardCandidate> pool, string cardSource)
	{
		string relative = (cardSource ?? string.Empty).Trim().TrimStart('/');
		AppendCardsFromPath(pool, "res://DataBase/Card/" + relative);
	}

	private static void AppendCardsFromPath(List<MerchantCardCandidate> pool, string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			GD.PrintErr("[商人] 卡表路径为空，该来源跳过。");
			return;
		}

		foreach (Card card in LoadCardCsv.LoadCardsFromCSV(path))
		{
			if (card == null || pool.Exists(x => x.CardId == card.CardId))
			{
				continue;
			}

			pool.Add(new MerchantCardCandidate
			{
				CardId = card.CardId,
				Tier = card.Tier,
				IsState = card.Category == CardCategory.State,
				Name = card.CardName,
			});
		}
	}
}
