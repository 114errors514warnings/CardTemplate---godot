using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 角色CSV专用加载器，处理角色数据的解析
/// </summary>
[GlobalClass]
public partial class LoadCharacterCsv : Node
{
	/// <summary>
	/// CSV表头常量
	/// </summary>
	public static readonly string CSV_HEADER = "id,Name,MAX_HP,Ini_Attack,Ini_Defend,drawCardNum";

	/// <summary>
	/// 从CSV文件加载所有角色
	/// CSV格式: id,Name,MAX_HP,Ini_Attack,Ini_Defend,drawCardNum
	/// </summary>
	/// <param name="filePath">CSV文件路径</param>
	/// <returns>角色数组</returns>
	public static Character[] LoadCharactersFromCSV(string filePath)
	{
		string[] dataLines = LoadCsv.LoadCSVDataLines(filePath);

		if (dataLines.Length == 0)
		{
			GD.Print($"No character data found in {filePath}");
			return Array.Empty<Character>();
		}

		List<Character> characterList = new List<Character>();

		foreach (string line in dataLines)
		{
			if (string.IsNullOrWhiteSpace(line))
				continue;

			Character character = ParseCharacterFromCSVLine(line);
			if (character != null)
			{
				characterList.Add(character);
			}
		}

		GD.Print($"Successfully loaded {characterList.Count} characters from {filePath}");
		return characterList.ToArray();
	}

	/// <summary>
	/// 解析单个CSV行为角色对象
	/// </summary>
	/// <param name="line">CSV行</param>
	/// <returns>解析后的角色对象，失败返回null</returns>
	private static Character ParseCharacterFromCSVLine(string line)
	{
		try
		{
			string[] fields = LoadCsv.ParseCSVFields(line);

			if (fields.Length < 6)
			{
				GD.PrintErr($"Invalid character CSV format. Expected at least 6 fields, got {fields.Length}");
				return null;
			}

			// 解析字段
			int id = int.Parse(fields[0]);
			string name = fields[1];
			int maxHp = int.Parse(fields[2]);
			int iniAttack = int.Parse(fields[3]);
			int iniDefend = int.Parse(fields[4]);
			int drawCardNum = int.Parse(fields[5]);

			return new Character(id, name, maxHp, iniAttack, iniDefend, drawCardNum);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error parsing character CSV line: {line}\nException: {ex.Message}");
			return null;
		}
	}
}