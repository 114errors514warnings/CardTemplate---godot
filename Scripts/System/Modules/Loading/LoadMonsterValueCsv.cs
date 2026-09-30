using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 解析 DataBase/Balance/MonsterValue.csv（奖励折损 P2-10.3 的数值来源）。
/// 列：MonsterId,IntentValues(,Note)
/// IntentValues：该怪各条意图的「意图价值」按 `|` 分隔，顺序与 `Monster.csv` 的 `IntentionN` 一致；
/// 面板 HP 仍取 `Monster.csv` 的 `MAX_HP`（综合价值点数 = 面板 HP + K × 意图加权价值，K = 1.0）。
/// </summary>
public static class LoadMonsterValueCsv
{
	private const int FieldCount = 2;

	public static List<MonsterValueEntry> LoadEntriesFromCSV(string filePath)
	{
		List<MonsterValueEntry> result = new List<MonsterValueEntry>();
		string[] dataLines = LoadCsv.LoadCSVDataLines(filePath);
		if (dataLines.Length == 0)
		{
			return result;
		}

		foreach (string line in dataLines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			MonsterValueEntry entry = ParseLine(line);
			if (entry != null)
			{
				result.Add(entry);
			}
		}

		return result;
	}

	public static MonsterValueEntry ParseLine(string line)
	{
		try
		{
			string[] fields = LoadCsv.ParseCSVFields(line);
			if (fields.Length < FieldCount)
			{
				GD.PrintErr($"[MonsterValue] 行格式错误：期望至少 {FieldCount} 列，实际 {fields.Length}：{line}");
				return null;
			}

			if (!int.TryParse(fields[0].Trim(), out int monsterId) || monsterId <= 0)
			{
				GD.PrintErr($"[MonsterValue] 怪物 Id 非法：{line}");
				return null;
			}

			return new MonsterValueEntry
			{
				MonsterId = monsterId,
				IntentValues = ParseIntentValues(fields[1]),
				Note = fields.Length > FieldCount ? fields[FieldCount].Trim() : string.Empty,
			};
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MonsterValue] 行解析异常：{line}\nException: {ex.Message}");
			return null;
		}
	}

	/// <summary>解析 `4|6|0` 形态的意图价值列；空串 = 无意图价值（返回空数组）。</summary>
	public static int[] ParseIntentValues(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return Array.Empty<int>();
		}

		string[] parts = raw.Split('|');
		List<int> values = new List<int>();
		foreach (string part in parts)
		{
			string trimmed = part.Trim();
			if (trimmed.Length == 0)
			{
				continue;
			}

			if (!int.TryParse(trimmed, out int value))
			{
				GD.PrintErr($"[MonsterValue] 意图价值非法：{raw}");
				return Array.Empty<int>();
			}

			values.Add(value);
		}

		return values.ToArray();
	}
}
