using Godot;
using CardSimulator;
using System;
using System.Collections.Generic;

/// <summary>
/// 卡牌CSV专用加载器，处理卡牌数据的解析和序列化
/// </summary>
[GlobalClass]
public partial class LoadCardCsv : Node
{

	// 分隔符常量："|"分隔多个效果，";"分隔同一效果内的多个参数
	private const char EFFECT_SEPARATOR = '|';
	private const char PARAM_SEPARATOR = ';';

	/// <summary>
	/// 从CSV文件加载所有卡牌
	/// CSV格式: CardId,CardName,CardType,EnergyCost,EffectType,EffectDesc,Params,CardKeyWord,ConditionParams
	/// EffectType 支持"|"分隔多效果；Params 用"|"分隔每个效果的参数组，用";"分隔同一效果内的参数
	/// CardKeyWord 支持"|"分隔多个关键词；为兼容旧配置，true 视为 Retain
	/// ConditionParams 为可选第9列，用"|"分隔多个条件枚举值；只要该列非空，就会在出牌前执行对应条件校验
	/// Params[i][0] 固定表示 EffectTargetType，后续参数为该效果自身参数
	/// NeedTarget 自动推导：任意效果TargetType为SelectedTarget则为true
	/// </summary>
	/// <param name="filePath">CSV文件路径</param>
	/// <returns>卡牌数组</returns>
	public static Card[] LoadCardsFromCSV(string filePath)
	{
		string[] dataLines = LoadCsv.LoadCSVDataLines(filePath);
		// 等级列（`CardTier`，P1-6 最小版 / T9）与条件列（`ConditionParams`）：都**按表头定位**。
		// 2026-10-05 修：此前条件列写死下标 8 → 通用表在尾部加 `CardTier` 后，第 9 列被当条件列解析（每行报错、整表 0 张）。
		int tierColumn = ResolveColumnIndex(filePath, "CardTier");
		int conditionColumn = ResolveColumnIndex(filePath, "ConditionParams");

		if (dataLines.Length == 0)
		{
			GD.Print($"No card data found in {filePath}");
			return Array.Empty<Card>();
		}

		List<Card> cardList = new List<Card>();

		foreach (string line in dataLines)
		{
			if (string.IsNullOrWhiteSpace(line))
				continue;

			Card card = ParseCardFromCSVLine(line, tierColumn, conditionColumn);
			if (card != null)
			{
				cardList.Add(card);
			}
		}

		GD.Print($"Successfully loaded {cardList.Count} cards from {filePath}");
		return cardList.ToArray();
	}

	/// <summary>定位表头里的某一列（大小写不敏感、去 BOM）；没有表头 / 没有该列时返回 -1。</summary>
	private static int ResolveColumnIndex(string filePath, string columnName)
	{
		foreach (string line in LoadCsv.LoadCSVLines(filePath))
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] header = LoadCsv.ParseCSVFields(line);
			for (int i = 0; i < header.Length; i++)
			{
				if ((header[i] ?? string.Empty).Trim().TrimStart('\uFEFF').Equals(columnName, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}

			return -1;   // 首行不是表头 → 视为无该列
		}

		return -1;
	}

	/// <summary>解析等级列：空 / 未知值 → <see cref="CardTier.None"/>（并按「不过滤」处理）。</summary>
	private static CardTier ParseTier(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return CardTier.None;
		}

		string trimmed = raw.Trim();
		if (Enum.TryParse(trimmed, true, out CardTier tier) && Enum.IsDefined(typeof(CardTier), tier))
		{
			return tier;
		}

		GD.PrintErr($"[CardTier] 无法识别的等级值：{raw}（按「无等级数据」处理）");
		return CardTier.None;
	}

	/// <summary>
	/// 解析单个CSV行为卡牌对象
	/// </summary>
	/// <param name="line">CSV行</param>
	/// <returns>解析后的卡牌对象，失败返回null</returns>
	private static Card ParseCardFromCSVLine(string line, int tierColumn = -1, int conditionColumn = -1)
	{
		try
		{
			string[] fields = LoadCsv.ParseCSVFields(line);

			if (fields.Length < 6)
			{
				GD.PrintErr($"Invalid card CSV format. Expected at least 6 fields, got {fields.Length}");
				return null;
			}

			// 解析通用字段
			int cardId = int.Parse(fields[0]);
			string cardName = fields[1];
			string categoryStr = fields[2];
			int energyCost = int.Parse(fields[3]);
			string effectTypeStr = fields[4];
			string effectDescription = fields[5];
			string paramsStr = fields.Length > 6 ? fields[6] : string.Empty;

			CardKeyWord cardKeyWord = fields.Length > 7 ? ParseCardKeyWord(fields[7]) : CardKeyWord.None;
			// 条件列按**表头**定位（缺列 = 该表无条件数据）；不再写死下标 8。
			CardConditionType[] conditionParams = conditionColumn >= 0 && conditionColumn < fields.Length
				? ParseConditionParams(fields[conditionColumn])
				: Array.Empty<CardConditionType>();

			// 解析 EffectTypes（"|"分隔多个效果）
			EffectType[] effectTypes = ParseEffectTypes(effectTypeStr);

			// 解析 Params（"|"分隔每个效果的参数，";"分隔同一组内的参数）
			int[][] cardParams = ParseParams(paramsStr);

			// 解析类别枚举
			CardCategory category = (CardCategory)Enum.Parse(typeof(CardCategory), categoryStr, ignoreCase: true);

			// NeedTarget 自动从 Params 中推导，无需CSV配置
			Card parsed = new Card(cardId, string.Empty, energyCost, category, effectTypes, effectDescription, cardParams, cardName, cardKeyWord, conditionParams);
			// 等级列（CardTier，P1-6 最小版 / T9）：按表头下标取；缺列 / 越界 / 空值 = None（掉落过滤不拦截）。
			parsed.Tier = tierColumn >= 0 && tierColumn < fields.Length ? ParseTier(fields[tierColumn]) : CardTier.None;
			return parsed;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error parsing card CSV line: {line}\nException: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// 解析 EffectType 字段（支持"|"分隔的多效果）
	/// </summary>
	private static EffectType[] ParseEffectTypes(string effectTypeStr)
	{
		if (string.IsNullOrWhiteSpace(effectTypeStr))
			return Array.Empty<EffectType>();

		string[] parts = effectTypeStr.Split(EFFECT_SEPARATOR);
		List<EffectType> types = new List<EffectType>(parts.Length);
		foreach (string part in parts)
		{
			string trimmed = part.Trim();
			if (!string.IsNullOrEmpty(trimmed))
				types.Add((EffectType)Enum.Parse(typeof(EffectType), trimmed, ignoreCase: true));
		}
		return types.ToArray();
	}

	private static CardKeyWord ParseCardKeyWord(string cardKeyWordStr)
	{
		if (string.IsNullOrWhiteSpace(cardKeyWordStr))
			return CardKeyWord.None;

		string trimmed = cardKeyWordStr.Trim();
		if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
			return CardKeyWord.Retain;

		string[] parts = trimmed.Split(new char[] { EFFECT_SEPARATOR, ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
		CardKeyWord result = CardKeyWord.None;
		foreach (string part in parts)
		{
			string keyWordText = part.Trim();
			if (string.IsNullOrEmpty(keyWordText))
				continue;

			result |= (CardKeyWord)Enum.Parse(typeof(CardKeyWord), keyWordText, ignoreCase: true);
		}

		return result;
	}

	private static CardConditionType[] ParseConditionParams(string conditionParamsStr)
	{
		if (string.IsNullOrWhiteSpace(conditionParamsStr))
			return Array.Empty<CardConditionType>();

		string[] parts = conditionParamsStr.Split(new char[] { EFFECT_SEPARATOR, ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
		List<CardConditionType> result = new List<CardConditionType>(parts.Length);
		foreach (string part in parts)
		{
			string trimmed = part.Trim();
			if (string.IsNullOrEmpty(trimmed))
			{
				continue;
			}

			result.Add((CardConditionType)Enum.Parse(typeof(CardConditionType), trimmed, ignoreCase: true));
		}

		return result.ToArray();
	}

	/// <summary>
	/// 解析 Params 字段（"|"分隔效果，";"分隔同一效果内的参数）
	/// 示例："5;10|3" => [[5,10],[3]]
	/// </summary>
	private static int[][] ParseParams(string paramsStr)
	{
		if (string.IsNullOrWhiteSpace(paramsStr))
			return Array.Empty<int[]>();

		string[] effectGroups = paramsStr.Split(EFFECT_SEPARATOR);
		List<int[]> result = new List<int[]>(effectGroups.Length);
		foreach (string group in effectGroups)
		{
			string trimmed = group.Trim();
			if (string.IsNullOrEmpty(trimmed))
			{
				result.Add(Array.Empty<int>());
				continue;
			}
			string[] paramParts = trimmed.Split(PARAM_SEPARATOR);
			List<int> paramList = new List<int>(paramParts.Length);
			foreach (string p in paramParts)
			{
				if (int.TryParse(p.Trim(), out int val))
					paramList.Add(val);
			}
			result.Add(paramList.ToArray());
		}
		return result.ToArray();
	}

	/// <summary>
	/// 根据卡牌ID过滤卡牌
	/// </summary>
	public static Card[] FilterCardsByTemplateId(Card[] cards, int templateId)
	{
		List<Card> filtered = new List<Card>();

		foreach (Card card in cards)
		{
			if (card.CardId == templateId)
			{
				filtered.Add(card);
			}
		}

		return filtered.ToArray();
	}

	/// <summary>
	/// 根据卡牌类别过滤卡牌
	/// </summary>
	public static Card[] FilterCardsByCategory(Card[] cards, CardCategory category)
	{
		List<Card> filtered = new List<Card>();

		foreach (Card card in cards)
		{
			if (card.Category == category)
			{
				filtered.Add(card);
			}
		}

		return filtered.ToArray();
	}
}
