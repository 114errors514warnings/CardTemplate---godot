using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 怪物CSV专用加载器，处理怪物数据的解析
/// </summary>
[GlobalClass]
public partial class LoadMonsterCsv : Node
{
	/// <summary>
	/// CSV表头常量
	/// </summary>
	public static readonly string CSV_HEADER = "id,Name,MAX_HP,Ini_Attack,Ini_Defend,MaxActionTime";

	/// <summary>
	/// 从CSV文件加载所有怪物
	/// CSV格式: id,Name,MAX_HP,Ini_Attack,Ini_Defend,MaxActionTime
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

			if (fields.Length < 6)
			{
				GD.PrintErr($"Invalid monster CSV format. Expected at least 6 fields, got {fields.Length}");
				return null;
			}

			// 解析字段
			int id = int.Parse(fields[0]);
			string name = fields[1];
			int maxHp = int.Parse(fields[2]);
			int iniAttack = int.Parse(fields[3]);
			int iniDefend = int.Parse(fields[4]);
			int maxActionTime = int.Parse(fields[5]);

			return new Monster(id, name, maxHp, iniAttack, iniDefend, maxActionTime);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error parsing monster CSV line: {line}\nException: {ex.Message}");
			return null;
		}
	}
}