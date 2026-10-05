// MerchantCatalog.cs
// 商人货架 / 卡包配表的**纯逻辑**读取与查询（无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/商人交互案.md
//   §4.4–§4.7（货架格数与来源）· §五（货架价）· §5.1（卡价按等级）· §4.2（5 个卡包的结构与等级权重）。
// 三张表：MerchantPrice.csv（货架价）· MerchantCardPrice.csv（卡价）· MerchantCardPack.csv（卡包结构）。
// ⚠️ **卡牌操作的计价不住这里** —— 它落在 `DeckOps` 常量（删 75 / 变 100 / 移 60 / 升 80×(级+1)），
//    避免「表里一份、代码里一份」两处改漏（商人交互案 §5.2 与 §十 第 15 条回改方式都指向那里）。
// ⚠️ 货架**抽样权重**（每类第几档概率）案里未定 → 本批只落「格数」，权重登记为待拍板项，不在代码里瞎定。
using System;
using System.Collections.Generic;
using System.Globalization;
using CardSimulator;

/// <summary>商人货架类目（商人交互案 §4.4–§4.7）。</summary>
public enum MerchantCategory
{
	Material = 0,
	Food = 1,
	Equipment = 2,
	Item = 3,
	Key = 4,
}

/// <summary>卡包类型（§4.2 五个卡包的分工）。</summary>
public enum MerchantPackKind
{
	/// <summary>该槽位角色的专属卡池（包 1–3）。</summary>
	Character = 0,
	/// <summary>三个槽位角色卡池混合（包 4）。</summary>
	Mixed = 1,
	/// <summary>通用卡池（包 5）。</summary>
	Generic = 2,
}

/// <summary>买下后的入组槽位策略（§4.2 / §4.3）。</summary>
public enum MerchantOwnerSlotPolicy
{
	/// <summary>固定写入本包绑定的槽位（包 1–3）。</summary>
	Fixed = 0,
	/// <summary>买时由玩家点选槽位（包 4 / 5）。</summary>
	Chosen = 1,
}

/// <summary>MerchantPrice.csv 的一行（货架价）。</summary>
public sealed class MerchantPriceRow
{
	public MerchantCategory Category;
	public ItemRarity Rarity;
	public int Price;
}

/// <summary>MerchantCardPack.csv 的一行（一个卡包的结构）。</summary>
public sealed class MerchantPackRow
{
	public int PackIndex;
	public MerchantPackKind Kind;
	public MerchantOwnerSlotPolicy Policy;
	public int CardCount;
	/// <summary>等级抽样权重（缺等级的卡按「不过滤等级」均等处理，见 §十 第 9 / 10 条）。</summary>
	public Dictionary<CardTier, int> TierWeights = new Dictionary<CardTier, int>();
}

public static class MerchantCatalog
{
	public static readonly string[] PriceHeader = { "Category", "Rarity", "Price" };
	public static readonly string[] CardPriceHeader = { "Tier", "Price" };
	public static readonly string[] CardPackHeader =
	{
		"PackIndex", "PackKind", "OwnerSlotPolicy", "CardCount", "TierWeightD", "TierWeightC", "TierWeightB", "TierWeightA", "TierWeightS",
	};

	/// <summary>货架格数（§4.4–§4.7：材料 / 食物 / 装备 / 道具 各 3 格，钥匙 1 格）。</summary>
	public static int StockSlots(MerchantCategory category) => category switch
	{
		MerchantCategory.Material => 3,
		MerchantCategory.Food => 3,
		MerchantCategory.Equipment => 3,
		MerchantCategory.Item => 3,
		MerchantCategory.Key => 1,
		_ => 0,
	};

	/// <summary>卡包个数（§4.2：上部一行固定 5 个卡位）。</summary>
	public const int CardPackCount = 5;

	/// <summary>表头校验（顺序即列序）；不符抛 <see cref="FormatException"/>。</summary>
	public static void ValidateHeader(IReadOnlyList<string> cells, string[] expected, string tableName)
	{
		if (cells == null || cells.Count != expected.Length)
		{
			throw new FormatException($"[{tableName}] 表头列数不符：期望 {expected.Length} 列，实际 {(cells?.Count ?? 0)} 列");
		}

		for (int i = 0; i < expected.Length; i++)
		{
			if (!string.Equals((cells[i] ?? string.Empty).Trim(), expected[i], StringComparison.OrdinalIgnoreCase))
			{
				throw new FormatException($"[{tableName}] 第 {i + 1} 列表头应为 `{expected[i]}`，实际 `{(cells[i] ?? string.Empty).Trim()}`");
			}
		}
	}

	/// <summary>解析货架价表。</summary>
	public static List<MerchantPriceRow> ParsePrices(IEnumerable<string> lines)
	{
		List<MerchantPriceRow> rows = new List<MerchantPriceRow>();
		bool headerSeen = false;
		int lineNumber = 0;
		foreach (string line in lines ?? Array.Empty<string>())
		{
			lineNumber++;
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] cells = line.Split(',');
			if (!headerSeen)
			{
				ValidateHeader(cells, PriceHeader, "MerchantPrice");
				headerSeen = true;
				continue;
			}

			string context = $"[MerchantPrice] 第 {lineNumber} 行（{line}）";
			if (cells.Length != PriceHeader.Length)
			{
				throw new FormatException($"{context}：列数不符（期望 {PriceHeader.Length} 列，实际 {cells.Length} 列）");
			}

			rows.Add(new MerchantPriceRow
			{
				Category = ParseCategory(cells[0], context),
				Rarity = ItemCsvSchema.ParseRarity(cells[1], context),
				Price = ParsePositiveInt(cells[2], context, "Price"),
			});
		}

		if (!headerSeen)
		{
			throw new FormatException("[MerchantPrice] 缺表头");
		}

		return rows;
	}

	/// <summary>按（类目 + 稀有度）取货架价；没有该行返回 -1（调用方给「该商品暂未上架」）。</summary>
	public static int PriceFor(IEnumerable<MerchantPriceRow> rows, MerchantCategory category, ItemRarity rarity)
	{
		foreach (MerchantPriceRow row in rows ?? Array.Empty<MerchantPriceRow>())
		{
			if (row != null && row.Category == category && row.Rarity == rarity)
			{
				return row.Price;
			}
		}

		return -1;
	}

	/// <summary>类目：Material / Food / Equipment / Item / Key（大小写不敏感）。</summary>
	public static MerchantCategory ParseCategory(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (Enum.TryParse(text, true, out MerchantCategory category) && Enum.IsDefined(category))
		{
			return category;
		}

		throw new FormatException($"{context}：类目不认识：`{text}`（可用：Material / Food / Equipment / Item / Key）");
	}

	/// <summary>解析卡价表（按卡牌等级，§5.1）。</summary>
	public static Dictionary<CardTier, int> ParseCardPrices(IEnumerable<string> lines)
	{
		Dictionary<CardTier, int> prices = new Dictionary<CardTier, int>();
		bool headerSeen = false;
		int lineNumber = 0;
		foreach (string line in lines ?? Array.Empty<string>())
		{
			lineNumber++;
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] cells = line.Split(',');
			if (!headerSeen)
			{
				ValidateHeader(cells, CardPriceHeader, "MerchantCardPrice");
				headerSeen = true;
				continue;
			}

			string context = $"[MerchantCardPrice] 第 {lineNumber} 行（{line}）";
			if (cells.Length != CardPriceHeader.Length)
			{
				throw new FormatException($"{context}：列数不符（期望 {CardPriceHeader.Length} 列，实际 {cells.Length} 列）");
			}

			CardTier tier = ParseTier(cells[0], context);
			if (!prices.TryAdd(tier, ParsePositiveInt(cells[1], context, "Price")))
			{
				throw new FormatException($"{context}：等级重复：{tier}");
			}
		}

		if (!headerSeen)
		{
			throw new FormatException("[MerchantCardPrice] 缺表头");
		}

		return prices;
	}

	/// <summary>解析卡包结构表（§4.2 / §4.3）。</summary>
	public static List<MerchantPackRow> ParseCardPacks(IEnumerable<string> lines)
	{
		List<MerchantPackRow> packs = new List<MerchantPackRow>();
		bool headerSeen = false;
		int lineNumber = 0;
		foreach (string line in lines ?? Array.Empty<string>())
		{
			lineNumber++;
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] cells = line.Split(',');
			if (!headerSeen)
			{
				ValidateHeader(cells, CardPackHeader, "MerchantCardPack");
				headerSeen = true;
				continue;
			}

			string context = $"[MerchantCardPack] 第 {lineNumber} 行（{line}）";
			if (cells.Length != CardPackHeader.Length)
			{
				throw new FormatException($"{context}：列数不符（期望 {CardPackHeader.Length} 列，实际 {cells.Length} 列）");
			}

			MerchantPackRow pack = new MerchantPackRow
			{
				PackIndex = ParsePositiveInt(cells[0], context, "PackIndex"),
				Kind = ParsePackKind(cells[1], context),
				Policy = ParsePolicy(cells[2], context),
				CardCount = ParsePositiveInt(cells[3], context, "CardCount"),
			};
			pack.TierWeights[CardTier.D] = ParseWeight(cells[4], context, "TierWeightD");
			pack.TierWeights[CardTier.C] = ParseWeight(cells[5], context, "TierWeightC");
			pack.TierWeights[CardTier.B] = ParseWeight(cells[6], context, "TierWeightB");
			pack.TierWeights[CardTier.A] = ParseWeight(cells[7], context, "TierWeightA");
			pack.TierWeights[CardTier.S] = ParseWeight(cells[8], context, "TierWeightS");
			packs.Add(pack);
		}

		if (!headerSeen)
		{
			throw new FormatException("[MerchantCardPack] 缺表头");
		}

		packs.Sort((a, b) => a.PackIndex.CompareTo(b.PackIndex));
		return packs;
	}

	/// <summary>卡牌操作里「该包买下后写哪个槽位」的判据（包 1–3 固定、包 4 / 5 选）。</summary>
	public static bool RequiresSlotChoice(MerchantOwnerSlotPolicy policy) => policy == MerchantOwnerSlotPolicy.Chosen;

	public static MerchantPackKind ParsePackKind(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (Enum.TryParse(text, true, out MerchantPackKind kind) && Enum.IsDefined(kind))
		{
			return kind;
		}

		throw new FormatException($"{context}：PackKind 不认识：`{text}`（可用：Character / Mixed / Generic）");
	}

	private static MerchantOwnerSlotPolicy ParsePolicy(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (Enum.TryParse(text, true, out MerchantOwnerSlotPolicy policy) && Enum.IsDefined(policy))
		{
			return policy;
		}

		throw new FormatException($"{context}：OwnerSlotPolicy 不认识：`{text}`（可用：Fixed / Chosen）");
	}

	private static CardTier ParseTier(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (Enum.TryParse(text, true, out CardTier tier) && tier != CardTier.None)
		{
			return tier;
		}

		throw new FormatException($"{context}：等级不认识：`{text}`（可用：D / C / B / A / S）");
	}

	/// <summary>权重（允许 0：= 该档不出现）。</summary>
	private static int ParseWeight(string raw, string context, string column)
	{
		string text = (raw ?? string.Empty).Trim();
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
		{
			throw new FormatException($"{context}：{column} 必须是非负整数，实际 `{text}`");
		}

		return value;
	}

	private static int ParsePositiveInt(string raw, string context, string column)
	{
		string text = (raw ?? string.Empty).Trim();
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
		{
			throw new FormatException($"{context}：{column} 必须是正整数，实际 `{text}`");
		}

		return value;
	}
}
