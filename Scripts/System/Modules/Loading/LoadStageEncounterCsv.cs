using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 解析 DataBase/Stage/&lt;层&gt;/&lt;节点类型&gt;.csv。
/// 列：Name,Difficulty,MonsterIds,DropTableId,Weight,Note
/// Difficulty 取值：Low/Mid/High/Any（空视为 Any）；MonsterIds 用 | 分隔。
/// 仅表头 = 0 行数据 = 无配置。
/// </summary>
public static class LoadStageEncounterCsv
{
	private const int FieldCount = 6;

	public static List<StageEncounterRow> LoadRowsFromCSV(string filePath, string layer, MapNodeType nodeType)
	{
		List<StageEncounterRow> result = new List<StageEncounterRow>();
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

			StageEncounterRow row = ParseLine(line, layer, nodeType);
			if (row != null)
			{
				result.Add(row);
			}
		}

		return result;
	}

	public static StageEncounterRow ParseLine(string line, string layer, MapNodeType nodeType)
	{
		try
		{
			string[] fields = LoadCsv.ParseCSVFields(line);
			if (fields.Length < FieldCount)
			{
				GD.PrintErr($"[Stage] 行格式错误：期望至少 {FieldCount} 列，实际 {fields.Length}：{line}");
				return null;
			}

			StageEncounterRow row = new StageEncounterRow
			{
				Name = fields[0].Trim(),
				Layer = layer ?? string.Empty,
				NodeType = nodeType,
				Difficulty = ParseDifficulty(fields[1]),
				DropTableId = ParseInt(fields[3], 0),
				Weight = ParseInt(fields[4], 1),
				Note = fields.Length > 5 ? fields[5].Trim() : string.Empty,
			};

			row.MonsterIds = ParseMonsterIds(fields[2], line);
			if (row.Weight <= 0)
			{
				row.Weight = 1;
			}

			return row;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[Stage] 行解析异常：{line}\nException: {ex.Message}");
			return null;
		}
	}

	public static StageDifficulty ParseDifficulty(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return StageDifficulty.Any;
		}

		switch (raw.Trim().ToLowerInvariant())
		{
			case "low":
			case "低":
				return StageDifficulty.Low;
			case "mid":
			case "中":
				return StageDifficulty.Mid;
			case "high":
			case "高":
				return StageDifficulty.High;
			default:
				return StageDifficulty.Any;
		}
	}

	private static int[] ParseMonsterIds(string raw, string sourceLine)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return Array.Empty<int>();
		}

		List<int> ids = new List<int>();
		string[] parts = raw.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string part in parts)
		{
			string trimmed = part.Trim();
			if (string.IsNullOrWhiteSpace(trimmed))
			{
				continue;
			}

			if (!int.TryParse(trimmed, out int id))
			{
				GD.PrintErr($"[Stage] MonsterIds 含非数字段 '{trimmed}'：{sourceLine}");
				continue;
			}

			ids.Add(id);
		}

		return ids.ToArray();
	}

	private static int ParseInt(string raw, int fallback)
	{
		return int.TryParse(raw?.Trim(), out int value) ? value : fallback;
	}
}
