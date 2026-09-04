using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 解析 DataBase/Card/CharacterRewardPool.csv。
/// 列：CharacterId,CardSource（CardSource 为 DataBase/Card/ 下相对路径）
/// </summary>
public static class LoadCharacterRewardPoolCsv
{
	public static List<CharacterRewardSource> LoadSourcesFromCSV(string filePath)
	{
		List<CharacterRewardSource> result = new List<CharacterRewardSource>();
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

			CharacterRewardSource source = ParseLine(line);
			if (source != null)
			{
				result.Add(source);
			}
		}

		return result;
	}

	public static CharacterRewardSource ParseLine(string line)
	{
		try
		{
			string[] fields = LoadCsv.ParseCSVFields(line);
			if (fields.Length < 2)
			{
				GD.PrintErr($"[CharacterRewardPool] 行格式错误：期望至少 2 列，实际 {fields.Length}：{line}");
				return null;
			}

			return new CharacterRewardSource
			{
				CharacterId = int.Parse(fields[0]),
				CardSource = fields[1].Trim(),
			};
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[CharacterRewardPool] 行解析异常：{line}\nException: {ex.Message}");
			return null;
		}
	}
}
