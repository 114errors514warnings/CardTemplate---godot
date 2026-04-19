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
	public static readonly string CSV_HEADER = "CardId,CardName,CardType,EnergyCost,EffectType,NeedTarget,EffectDesc,AttackDamage,ShieldValue,StateType";

	/// <summary>
	/// 从CSV文件加载所有卡牌
	/// CSV格式: CardId,CardName,CardType,EnergyCost,EffectType,NeedTarget,EffectDesc,AttackDamage,ShieldValue,StateType
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

			if (fields.Length < 7)
			{
				GD.PrintErr($"Invalid card CSV format. Expected at least 7 fields, got {fields.Length}");
				return null;
			}

			// 解析通用字段
			int cardId = int.Parse(fields[0]);
			string cardName = fields[1];
			string categoryStr = fields[2];
			int energyCost = int.Parse(fields[3]);
			string effectTypeStr = fields[4];
			bool needTarget = bool.Parse(fields[5]);
			string effectDescription = fields[6];

			// 解析枚举值
			CardCategory category = (CardCategory)Enum.Parse(typeof(CardCategory), categoryStr, ignoreCase: true);
			EffectType effectType = (EffectType)Enum.Parse(typeof(EffectType), effectTypeStr, ignoreCase: true);

			// 根据类型创建相应的卡牌对象
			return CreateCardByCategory(category, cardId, cardName, energyCost, effectType, effectDescription, needTarget, fields);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error parsing card CSV line: {line}\nException: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// 根据卡牌类别创建相应的卡牌对象
	/// </summary>
	private static Card CreateCardByCategory(CardCategory category, int cardId, string cardName, int energyCost, EffectType effectType, string effectDescription, bool needTarget, string[] fields)
	{
		switch (category)
		{
			case CardCategory.Attack:
				int extraAttack = fields.Length > 7 ? int.Parse(fields[7]) : 0;
				int extraShield = fields.Length > 8 ? int.Parse(fields[8]) : 0;
				return new AttackCard(cardId, string.Empty, energyCost, effectType, effectDescription, needTarget, extraAttack, extraShield, cardName);

			case CardCategory.Skill:
				int skillExtraShield = fields.Length > 8 ? int.Parse(fields[8]) : 0;
				return new SkillCard(cardId, string.Empty, energyCost, effectType, effectDescription, needTarget, skillExtraShield, cardName);

			case CardCategory.State:
				return new StateCard(cardId, string.Empty, energyCost, effectType, effectDescription, needTarget, cardName);

			default:
				GD.PrintErr($"Unknown card category: {category}");
				return null;
		}
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
		string extraAttack = "";
		string extraShield = "";

		if (card is AttackCard attackCard)
		{
			extraAttack = attackCard.ExtraAttack.ToString();
			extraShield = attackCard.ExtraShield.ToString();
		}
		else if (card is SkillCard skillCard)
		{
			extraShield = skillCard.ExtraShield.ToString();
		}

		return $"{card.CardId},{LoadCsv.EscapeCSVField(card.CardName)},{card.Category},{card.EnergyCost},{card.EffectType},{card.NeedTarget},{LoadCsv.EscapeCSVField(card.EffectDescription)},{extraAttack},{extraShield},None";
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
