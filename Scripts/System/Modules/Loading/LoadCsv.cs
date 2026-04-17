using Godot;
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 通用CSV读写工具类，提供基础的CSV解析和保存功能
/// </summary>
[GlobalClass]
public partial class LoadCsv : Node
{
	/// <summary>
	/// 从CSV文件读取所有行
	/// </summary>
	/// <param name="filePath">文件路径</param>
	/// <returns>包含所有行的字符串数组，如果失败返回空数组</returns>
	public static string[] LoadCSVLines(string filePath)
	{
		if (!File.Exists(filePath))
		{
			GD.PrintErr($"CSV file not found: {filePath}");
			return Array.Empty<string>();
		}

		try
		{
			return File.ReadAllLines(filePath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error reading CSV file {filePath}: {ex.Message}");
			return Array.Empty<string>();
		}
	}

	/// <summary>
	/// 将字符串写入CSV文件
	/// </summary>
	/// <param name="filePath">文件路径</param>
	/// <param name="lines">要写入的行</param>
	/// <returns>是否成功保存</returns>
	public static bool SaveCSVLines(string filePath, string[] lines)
	{
		try
		{
			File.WriteAllLines(filePath, lines);
			GD.Print($"Successfully saved CSV to {filePath}");
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error saving CSV file {filePath}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// 使用流写入器逐行写入CSV文件（大文件优化）
	/// </summary>
	/// <param name="filePath">文件路径</param>
	/// <param name="lines">要写入的行</param>
	/// <returns>是否成功保存</returns>
	public static bool SaveCSVLinesStream(string filePath, string[] lines)
	{
		try
		{
			using (StreamWriter writer = new StreamWriter(filePath))
			{
				foreach (string line in lines)
				{
					writer.WriteLine(line);
				}
			}

			GD.Print($"Successfully saved CSV to {filePath}");
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error saving CSV file {filePath}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// 解析CSV行中的字段，处理引号和逗号
	/// </summary>
	/// <param name="line">CSV行</param>
	/// <returns>字段数组</returns>
	public static string[] ParseCSVFields(string line)
	{
		List<string> fields = new List<string>();
		string currentField = "";
		bool insideQuotes = false;

		for (int i = 0; i < line.Length; i++)
		{
			char c = line[i];

			if (c == '"')
			{
				insideQuotes = !insideQuotes;
			}
			else if (c == ',' && !insideQuotes)
			{
				fields.Add(currentField.Trim().Trim('"'));
				currentField = "";
			}
			else
			{
				currentField += c;
			}
		}

		// 添加最后一个字段
		fields.Add(currentField.Trim().Trim('"'));

		return fields.ToArray();
	}

	/// <summary>
	/// 转义CSV字段中的特殊字符
	/// </summary>
	/// <param name="field">字段内容</param>
	/// <returns>转义后的字段</returns>
	public static string EscapeCSVField(string field)
	{
		if (field == null)
			return "";

		if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
		{
			return $"\"{field.Replace("\"", "\"\"")}\"";
		}
		return field;
	}

	/// <summary>
	/// 跳过CSV文件的表头行，返回数据行
	/// </summary>
	/// <param name="filePath">文件路径</param>
	/// <returns>不包含表头的数据行数组</returns>
	public static string[] LoadCSVDataLines(string filePath)
	{
		string[] allLines = LoadCSVLines(filePath);

		if (allLines.Length <= 1)
			return Array.Empty<string>();

		// 跳过表头（第一行）
		string[] dataLines = new string[allLines.Length - 1];
		Array.Copy(allLines, 1, dataLines, 0, allLines.Length - 1);

		return dataLines;
	}
}
