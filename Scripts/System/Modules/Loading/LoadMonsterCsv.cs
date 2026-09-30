using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 怪物CSV专用加载器，处理怪物数据的解析
/// </summary>
[GlobalClass]
public partial class LoadMonsterCsv : Node
{
	private const int BaseFieldCount = 5;
	private const int MaxIntentionColumnCount = 10;

	/// <summary>
	/// 从CSV文件加载所有怪物
	/// CSV格式: id,Name,MAX_HP,Ini_Attack,Ini_Defend,Intention1...Intention10[,IsMinion]
	/// </summary>
	/// <param name="filePath">CSV文件路径</param>
	/// <returns>怪物数组</returns>
	public static Monster[] LoadMonstersFromCSV(string filePath)
	{
		string[] allLines = LoadCsv.LoadCSVLines(filePath);

		if (allLines.Length == 0)
		{
			GD.Print($"No monster data found in {filePath}");
			return Array.Empty<Monster>();
		}

		Monster[] monsters = ParseMonstersFromLines(allLines);
		GD.Print($"Successfully loaded {monsters.Length} monsters from {filePath}");
		return monsters;
	}

	/// <summary>
	/// 解析整份怪物表（含表头）：跳过第一行，逐行解析为 `Monster`。
	/// **`IsMinion`（是否为爪牙）列按表头列名定位**（列结构见 `MonsterCsvSchema`；位置无关，缺列时全部按非爪牙）；
	/// 意图列仍按固定偏移解析（`Intention1..10` = 前五列之后的 10 列），因此新列请追加在意图列之后。
	/// 除 CSV 拆列与失败告警外不做别的处理；纯 .NET 单测覆盖的是 `MonsterCsvSchema`（本类派生自 `Node`，测试工程未引用 GodotSharp），
	/// 本类的整表解析由 Godot 侧 `--battlefield-smoke` 的 `VerifyMonsterTableColumns()` 覆盖。
	/// </summary>
	/// <param name="lines">CSV 全部行（第 0 行为表头）</param>
	/// <returns>怪物数组；没有数据行时为空数组</returns>
	public static Monster[] ParseMonstersFromLines(string[] lines)
	{
		if (lines == null || lines.Length <= 1)
		{
			return Array.Empty<Monster>();
		}

		int isMinionColumnIndex = MonsterCsvSchema.ResolveIsMinionColumnIndex(LoadCsv.ParseCSVFields(lines[0]));

		List<Monster> monsterList = new List<Monster>();
		for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
		{
			string line = lines[lineIndex];
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			Monster monster = ParseMonsterFromCSVLine(line, isMinionColumnIndex);
			if (monster != null)
			{
				monsterList.Add(monster);
			}
		}

		return monsterList.ToArray();
	}

	/// <summary>
	/// 解析单个CSV行为怪物对象
	/// </summary>
	/// <param name="line">CSV行</param>
	/// <param name="isMinionColumnIndex">`IsMinion` 列下标（-1 = 该表无此列，按非爪牙）</param>
	/// <returns>解析后的怪物对象，失败返回null</returns>
	public static Monster ParseMonsterFromCSVLine(string line, int isMinionColumnIndex = -1)
	{
		try
		{
			string[] fields = LoadCsv.ParseCSVFields(line);

			if (fields.Length < BaseFieldCount)
			{
				GD.PrintErr($"Invalid monster CSV format. Expected at least {BaseFieldCount} fields, got {fields.Length}");
				return null;
			}

			// 解析字段
			int id = int.Parse(fields[0]);
			string name = fields[1];
			int maxHp = int.Parse(fields[2]);
			int iniAttack = int.Parse(fields[3]);
			int iniDefend = int.Parse(fields[4]);
			int[][][] table = ParseIntentions(fields, line);
			if (table == null)
			{
				return null;
			}

			// 是否为爪牙：不属于必杀目标、不计入战利品价值比例（玩法 §7.4 / §7.5）。
			if (!MonsterCsvSchema.TryParseIsMinion(fields, isMinionColumnIndex, out bool isMinion, out string minionError))
			{
				GD.PrintErr($"Monster CSV: {minionError}. Source: {line}");
			}

			return new Monster(id, name, maxHp, iniAttack, iniDefend, table, isMinion);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error parsing monster CSV line: {line}\nException: {ex.Message}");
			return null;
		}
	}

	private static int[][][] ParseIntentions(string[] fields, string sourceLine)
	{
		List<int[][]> intentions = new List<int[][]>();

		for (int columnOffset = 0; columnOffset < MaxIntentionColumnCount; columnOffset++)
		{
			int fieldIndex = BaseFieldCount + columnOffset;
			if (fieldIndex >= fields.Length)
			{
				break;
			}

			string rawIntention = fields[fieldIndex]?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(rawIntention))
			{
				break;
			}

			int[][] parsedIntention = ParseSingleIntention(rawIntention, sourceLine, columnOffset + 1);
			if (parsedIntention == null)
			{
				return null;
			}

			intentions.Add(parsedIntention);
		}

		return intentions.ToArray();
	}

	private static int[][] ParseSingleIntention(string rawIntention, string sourceLine, int intentionIndex)
	{
		string[] effectSegments = rawIntention.Split('|', StringSplitOptions.RemoveEmptyEntries);
		if (effectSegments.Length == 0)
		{
			GD.PrintErr($"Monster intention parse failed at Intention{intentionIndex}: empty intention. Source: {sourceLine}");
			return null;
		}

		List<int[]> effects = new List<int[]>();
		foreach (string effectSegment in effectSegments)
		{
			string trimmedSegment = effectSegment.Trim();
			if (string.IsNullOrWhiteSpace(trimmedSegment))
			{
				continue;
			}

			string[] rawParams = trimmedSegment.Split(';', StringSplitOptions.RemoveEmptyEntries);
			if (rawParams.Length == 0)
			{
				GD.PrintErr($"Monster intention parse failed at Intention{intentionIndex}: effect has no params. Source: {sourceLine}");
				return null;
			}

			List<int> parsedParams = new List<int>();
			foreach (string rawParam in rawParams)
			{
				string trimmedParam = rawParam.Trim();
				if (string.IsNullOrWhiteSpace(trimmedParam))
				{
					continue;
				}

				if (!int.TryParse(trimmedParam, out int parsedValue))
				{
					GD.PrintErr($"Monster intention parse failed at Intention{intentionIndex}: '{trimmedParam}' is not an integer. Source: {sourceLine}");
					return null;
				}

				parsedParams.Add(parsedValue);
			}

			if (parsedParams.Count == 0)
			{
				GD.PrintErr($"Monster intention parse failed at Intention{intentionIndex}: effect has no valid integer params. Source: {sourceLine}");
				return null;
			}

			effects.Add(parsedParams.ToArray());
		}

		return effects.ToArray();
	}
}