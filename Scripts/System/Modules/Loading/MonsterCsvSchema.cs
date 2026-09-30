// MonsterCsvSchema.cs
// `Monster.csv` 的列结构（目前只有「是否为爪牙」一列）：表头按列名定位 + 取值解析。
// 纯逻辑：**不引用 Godot、不读写文件、不打日志**，因此 `LoadMonsterCsv` 与纯 .NET 单测（`Tests/MonsterCsvSchemaTests.cs`）可以共用。
// 语义出处：玩法 §7.4 / §7.5（爪牙不属于必杀目标、不计入战利品价值比例）；落地记录见 9 月施工文档 §35。
using System;
using System.Collections.Generic;

/// <summary>`Monster.csv` 的可选列定义与取值解析。</summary>
public static class MonsterCsvSchema
{
	/// <summary>「是否为爪牙」列名：写在意图列之后（意图列按固定偏移解析，本列位置由表头决定）。</summary>
	public const string IsMinionColumnName = "IsMinion";

	/// <summary>列名别名：中文写法同样接受（大小写与首尾空白不敏感）。</summary>
	public static readonly string[] IsMinionColumnAliases = { IsMinionColumnName, "是否爪牙" };

	/// <summary>解析为「是爪牙」的取值（小写）。</summary>
	private static readonly HashSet<string> TruthyValues = new HashSet<string> { "1", "true", "yes", "是", "爪牙" };

	/// <summary>解析为「非爪牙」的取值（小写）；空值与 `-` 同样按非爪牙处理。</summary>
	private static readonly HashSet<string> FalsyValues = new HashSet<string> { "0", "false", "no", "否", "-" };

	/// <summary>
	/// 在表头字段里定位 `IsMinion` 列。
	/// </summary>
	/// <param name="headerFields">表头行拆出的字段</param>
	/// <returns>列下标；找不到返回 -1（旧表无此列 → 该表不记爪牙，全部按非爪牙）</returns>
	public static int ResolveIsMinionColumnIndex(string[] headerFields)
	{
		if (headerFields == null)
		{
			return -1;
		}

		for (int i = 0; i < headerFields.Length; i++)
		{
			string header = headerFields[i]?.Trim() ?? string.Empty;
			foreach (string alias in IsMinionColumnAliases)
			{
				if (string.Equals(header, alias, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}
		}

		return -1;
	}

	/// <summary>
	/// 取 `IsMinion` 列的值：`1 / true / yes / 是 / 爪牙` = 是；`0 / false / no / 否 / - / 空` = 否。
	/// 列不存在、该行没写到这一列、或值为空都按「否」处理（返回 true + false）。
	/// 只有**无法识别的非空值**才返回 false，由调用方决定如何告警。
	/// </summary>
	/// <param name="fields">该行拆出的字段</param>
	/// <param name="columnIndex">`IsMinion` 列下标（-1 = 该表无此列）</param>
	/// <param name="isMinion">解析结果</param>
	/// <param name="error">无法识别时的原因描述（可识别时为空串）</param>
	/// <returns>值是否可识别（含「列不存在 / 值为空」的缺省情况）</returns>
	public static bool TryParseIsMinion(string[] fields, int columnIndex, out bool isMinion, out string error)
	{
		isMinion = false;
		error = string.Empty;

		if (fields == null || columnIndex < 0 || columnIndex >= fields.Length)
		{
			return true;
		}

		string raw = fields[columnIndex]?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return true;
		}

		string normalized = raw.ToLowerInvariant();
		if (FalsyValues.Contains(normalized))
		{
			return true;
		}

		if (TruthyValues.Contains(normalized))
		{
			isMinion = true;
			return true;
		}

		error = $"{IsMinionColumnName} 列的值 '{raw}' 无法识别（按非爪牙处理）";
		return false;
	}
}
