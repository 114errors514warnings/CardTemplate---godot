// BattleCardSpatialRepository.cs
// 读取 勇士Card.csv 的扩展空间列（SpatialShape/SpatialArgs/TrapId），供战场出牌使用。
using System;
using System.Collections.Generic;

namespace CardSimulator.Battlefield;

public static class BattleCardSpatialRepository
{
	public const string WarriorCardCsvPath = "res://DataBase/Card/勇士Card.csv";

	private static Dictionary<int, CardSpatialSpec> cache;

	public static Dictionary<int, CardSpatialSpec> LoadAll(bool useCache = true)
	{
		if (useCache && cache != null)
		{
			return cache;
		}

		Dictionary<int, CardSpatialSpec> fresh = new Dictionary<int, CardSpatialSpec>();
		string[] dataLines = LoadCsv.LoadCSVDataLines(WarriorCardCsvPath);
		foreach (string line in dataLines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			CardSpatialSpec spec = CardSpatialSpec.ParseFields(LoadCsv.ParseCSVFields(line));
			if (spec.CardId > 0)
			{
				fresh[spec.CardId] = spec;
			}
		}

		cache = fresh;
		return cache;
	}

	public static CardSpatialSpec ForCard(int cardId)
	{
		if (cache == null)
		{
			LoadAll();
		}

		return cache != null && cache.TryGetValue(cardId, out CardSpatialSpec spec) ? spec : null;
	}
}
