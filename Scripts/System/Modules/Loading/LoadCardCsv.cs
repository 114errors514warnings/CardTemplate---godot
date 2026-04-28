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
	/// <summary>
	/// CSV表头常量
	/// </summary>
	public static readonly string CSV_HEADER = "CardId,CardName,CardType,EnergyCost,EffectType,EffectDesc,Params";

	// 分隔符常量："|"分隔多个效果，";"分隔同一效果内的多个参数
	private const char EFFECT_SEPARATOR = '|';
	private const char PARAM_SEPARATOR = ';';

	/// <summary>
	/// 从CSV文件加载所有卡牌
	/// CSV格式: CardId,CardName,CardType,EnergyCost,EffectType,EffectDesc,Params
	/// EffectType 支持"|"分隔多效果；Params 用"|"分隔每个效果的参数组，用";"分隔同一效果内的参数
	/// Params[i][0] 固定表示 EffectTargetType，后续参数为该效果自身参数
	/// NeedTarget 自动推导：任意效果TargetType为SelectedTarget则为true
	/// </summary>
	/// <param name="filePath">CSV文件路径</param>
	/// <returns>卡牌数组</returns>
	public static Card[] LoadCardsFromCSV(string filePath)
	{
		string[] dataLines = LoadCsv.LoadCSVDataLines(filePath);

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

			Card card = ParseCardFromCSVLine(line);
			if (card != null)
			{
				cardList.Add(card);
			}
		}

		GD.Print($"Successfully loaded {cardList.Count} cards from {filePath}");
		return cardList.ToArray();
	}

	/// <summary>
	/// 解析单个CSV行为卡牌对象
	/// </summary>
	/// <param name="line">CSV行</param>
	/// <returns>解析后的卡牌对象，失败返回null</returns>
	private static Card ParseCardFromCSVLine(string line)
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

			// 解析 EffectTypes（"|"分隔多个效果）
			EffectType[] effectTypes = ParseEffectTypes(effectTypeStr);

			// 解析 Params（"|"分隔每个效果的参数，";"分隔同一组内的参数）
			int[][] cardParams = ParseParams(paramsStr);

			// 解析类别枚举
			CardCategory category = (CardCategory)Enum.Parse(typeof(CardCategory), categoryStr, ignoreCase: true);

			// NeedTarget 自动从 Params 中推导，无需CSV配置
			return new Card(cardId, string.Empty, energyCost, category, effectTypes, effectDescription, cardParams, cardName);
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
	/// 将 Params 序列化为 CSV 字段字符串
	/// </summary>
	private static string SerializeParams(int[][] cardParams)
	{
		if (cardParams == null || cardParams.Length == 0)
			return string.Empty;

		List<string> groups = new List<string>(cardParams.Length);
		foreach (int[] group in cardParams)
		{
			groups.Add(group != null && group.Length > 0 ? string.Join(PARAM_SEPARATOR.ToString(), group) : string.Empty);
		}
		return string.Join(EFFECT_SEPARATOR.ToString(), groups);
	}

	/// <summary>
	/// 将卡牌数组保存到CSV文件
	/// </summary>
	/// <param name="cards">卡牌数组</param>
	/// <param name="filePath">保存路径</param>
	/// <returns>是否成功保存</returns>
	public static bool SaveCardsToCSV(Card[] cards, string filePath)
	{
		List<string> lines = new List<string>();

		// 添加表头
		lines.Add(CSV_HEADER);

		// 添加每张卡牌
		foreach (Card card in cards)
		{
			string line = CardToCSVLine(card);
			lines.Add(line);
		}

		bool success = LoadCsv.SaveCSVLinesStream(filePath, lines.ToArray());

		if (success)
		{
			GD.Print($"Successfully saved {cards.Length} cards to {filePath}");
		}

		return success;
	}

	/// <summary>
	/// 将单个卡牌转换为CSV行
	/// </summary>
	private static string CardToCSVLine(Card card)
	{
		string effectTypesStr = card.EffectTypes != null ? string.Join(EFFECT_SEPARATOR.ToString(), card.EffectTypes) : string.Empty;
		string paramsStr = SerializeParams(card.Params);
		return $"{card.CardId},{LoadCsv.EscapeCSVField(card.CardName)},{card.Category},{card.EnergyCost},{effectTypesStr},{LoadCsv.EscapeCSVField(card.EffectDescription)},{paramsStr}";
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
