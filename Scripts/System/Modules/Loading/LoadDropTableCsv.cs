using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 解析 DataBase/Map/DropTable.csv。
/// 列：DropTableId,Category,RewardParam,Amount,Weight
/// Category：Card / Gold / Material / Item / Key（大小写不敏感，可含中文别名）。
/// </summary>
public static class LoadDropTableCsv
{
	private const int FieldCount = 5;

	public static List<DropTableEntry> LoadEntriesFromCSV(string filePath)
	{
		List<DropTableEntry> result = new List<DropTableEntry>();
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

			DropTableEntry entry = ParseLine(line);
			if (entry != null)
			{
				result.Add(entry);
			}
		}

		return result;
	}

	public static DropTableEntry ParseLine(string line)
	{
		try
		{
			string[] fields = LoadCsv.ParseCSVFields(line);
			if (fields.Length < FieldCount)
			{
				GD.PrintErr($"[DropTable] 行格式错误：期望至少 {FieldCount} 列，实际 {fields.Length}：{line}");
				return null;
			}

			return new DropTableEntry
			{
				DropTableId = int.Parse(fields[0]),
				Category = ParseCategory(fields[1]),
				RewardParam = ParseInt(fields[2]),
				Amount = ParseInt(fields[3]),
				Weight = ParseInt(fields[4]),
			};
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[DropTable] 行解析异常：{line}\nException: {ex.Message}");
			return null;
		}
	}

	private static DropCategory ParseCategory(string raw)
	{
		string trimmed = (raw ?? string.Empty).Trim();
		if (string.Equals(trimmed, "卡", StringComparison.Ordinal) || trimmed.IndexOf("卡牌", StringComparison.Ordinal) >= 0)
		{
			return DropCategory.Card;
		}
		if (string.Equals(trimmed, "金币", StringComparison.Ordinal) || trimmed.IndexOf("金币", StringComparison.Ordinal) >= 0)
		{
			return DropCategory.Gold;
		}
		if (string.Equals(trimmed, "材料", StringComparison.Ordinal) || trimmed.IndexOf("材料", StringComparison.Ordinal) >= 0)
		{
			return DropCategory.Material;
		}
		if (string.Equals(trimmed, "道具", StringComparison.Ordinal) || trimmed.IndexOf("道具", StringComparison.Ordinal) >= 0)
		{
			return DropCategory.Item;
		}
		if (string.Equals(trimmed, "钥匙", StringComparison.Ordinal) || trimmed.IndexOf("钥匙", StringComparison.Ordinal) >= 0)
		{
			return DropCategory.Key;
		}

		return Enum.TryParse(trimmed, true, out DropCategory category) ? category : DropCategory.Card;
	}

	private static int ParseInt(string raw)
	{
		return int.TryParse(raw?.Trim(), out int value) ? value : 0;
	}
}
