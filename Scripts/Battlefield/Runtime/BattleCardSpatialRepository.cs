// BattleCardSpatialRepository.cs
// 读取 DataBase/Card 下**全部卡表**的扩展空间列（SpatialShape/SpatialArgs/TrapId），供战场出牌使用。
// 2026-10-04（P2-29）：不再只读 勇士Card.csv —— 递归扫描卡表文件夹、排序后合并，
// 否则精灵 / 法师表里写的 Line / Pierce / Trap / Burst 读不到，GetSpatialSpec 会静默回落到单格。
using System;
using System.Collections.Generic;
using Godot;

namespace CardSimulator.Battlefield;

public static class BattleCardSpatialRepository
{
	/// <summary>卡表文件夹（含子文件夹，如 通用/通用Card.csv）。</summary>
	public const string CardCsvFolderPath = "res://DataBase/Card";

	private static Dictionary<int, CardSpatialSpec> cache;
	private static readonly List<string[]> rowBuffer = new List<string[]>();

	public static Dictionary<int, CardSpatialSpec> LoadAll(bool useCache = true)
	{
		if (useCache && cache != null)
		{
			return cache;
		}

		Dictionary<int, CardSpatialSpec> fresh = new Dictionary<int, CardSpatialSpec>();
		List<string> csvPaths = new List<string>();
		LoadingSystem.CollectCsvFiles(CardCsvFolderPath, csvPaths);
		// DirAccess 的枚举顺序不保证稳定：先排序，保证「同 CardId 哪个文件先到」在每台机器上一致。
		csvPaths.Sort(StringComparer.Ordinal);

		foreach (string path in csvPaths)
		{
			// ⚠️ 必须用 LoadCSVLines（含表头）：LoadCSVDataLines 会**跳过表头**，拿它判断不了这是不是卡表。
			string[] allLines = LoadCsv.LoadCSVLines(path);
			if (!IsSpatialCardTable(allLines))
			{
				continue;   // 非卡表（CharacterRewardPool.csv）或未声明空间列的表（通用 / 重剑手）
			}

			rowBuffer.Clear();
			for (int i = 1; i < allLines.Length; i++)   // 第 0 行 = 表头
			{
				if (string.IsNullOrWhiteSpace(allLines[i]))
				{
					continue;
				}

				rowBuffer.Add(LoadCsv.ParseCSVFields(allLines[i]));
			}

			foreach (string conflict in CardSpatialSpecCatalog.Merge(rowBuffer, fresh, path))
			{
				GD.PrintErr($"[卡牌空间] {conflict}");
			}
		}

		if (fresh.Count == 0)
		{
			GD.PrintErr($"[卡牌空间] 未从 {CardCsvFolderPath} 读到任何空间卡表 —— 所有卡的空间列会静默回落到单格语义。");
		}

		cache = fresh;
		return cache;
	}

	/// <summary>整表是否为「声明了空间列」的卡表（传入**含表头**的全部行）。</summary>
	private static bool IsSpatialCardTable(string[] allLines)
	{
		if (allLines == null)
		{
			return false;
		}

		foreach (string line in allLines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			return CardSpatialSpecCatalog.IsSpatialCardTableHeader(LoadCsv.ParseCSVFields(line));
		}

		return false;
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

