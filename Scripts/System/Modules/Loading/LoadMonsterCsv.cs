using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 怪物CSV专用加载器，处理怪物数据的解析
/// </summary>
[GlobalClass]
public partial class LoadMonsterCsv : Node
{
	private const int BaseFieldCount = 6;
	private const int MaxIntentionColumnCount = 10;

	/// <summary>
	/// 从CSV文件加载所有怪物
	/// CSV格式: id,Name,MAX_HP,Ini_Attack,Ini_Defend,MaxActionTime,Intention1...Intention10
	/// </summary>
	/// <param name="filePath">CSV文件路径</param>
	/// <returns>怪物数组</returns>
	public static Monster[] LoadMonstersFromCSV(string filePath)
	{
		string[] dataLines = LoadCsv.LoadCSVDataLines(filePath);

		if (dataLines.Length == 0)
		{
			GD.Print($"No monster data found in {filePath}");
			return Array.Empty<Monster>();
		}

		List<Monster> monsterList = new List<Monster>();

		foreach (string line in dataLines)
		{
			if (string.IsNullOrWhiteSpace(line))
				continue;

			Monster monster = ParseMonsterFromCSVLine(line);
			if (monster != null)
			{
				monsterList.Add(monster);
			}
		}

		GD.Print($"Successfully loaded {monsterList.Count} monsters from {filePath}");
		return monsterList.ToArray();
	}

	/// <summary>
	/// 解析单个CSV行为怪物对象
	/// </summary>
	/// <param name="line">CSV行</param>
	/// <returns>解析后的怪物对象，失败返回null</returns>
	private static Monster ParseMonsterFromCSVLine(string line)
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
			int maxActionTime = int.Parse(fields[5]);
			int[][][] table = ParseIntentions(fields, line);
			if (table == null)
			{
				return null;
			}

			return new Monster(id, name, maxHp, iniAttack, iniDefend, maxActionTime, table);
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