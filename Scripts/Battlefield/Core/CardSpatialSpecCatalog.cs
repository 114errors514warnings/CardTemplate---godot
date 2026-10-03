// CardSpatialSpecCatalog.cs
// 纯逻辑：把多张卡表的字段行合并成 CardId → CardSpatialSpec 的字典（跨表查重、越界列天然兼容）。
// 本类不引用 Godot（LoadCsv / DirAccess / FileAccess 都留在 Runtime 侧的调用方），便于单测直接喂行数据。
using System;
using System.Collections.Generic;

namespace CardSimulator.Battlefield;

public static class CardSpatialSpecCatalog
{
	/// <summary>卡表表头首列名（用于判断一个 CSV 是不是卡表）。</summary>
	public const string CardIdColumnName = "CardId";

	/// <summary>表头首列是否为 CardId（容忍 BOM 与大小写 / 空白差异）。</summary>
	public static bool IsCardTableHeader(string[] headerFields)
	{
		if (headerFields == null || headerFields.Length == 0)
		{
			return false;
		}

		string first = (headerFields[0] ?? string.Empty).Trim().TrimStart('\uFEFF');
		return string.Equals(first, CardIdColumnName, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 是否为「声明了空间列」的卡表：首列是 CardId **且**表头含 SpatialShape。
	/// 只有这类表才进空间规格字典 —— 未声明空间列的表（通用 / 重剑手）若也进字典，
	/// 它们的卡会拿到 <c>Shape = None</c>（而不是回落 <c>Single</c>），攻击牌会退化成「打自己」。
	/// </summary>
	public static bool IsSpatialCardTableHeader(string[] headerFields)
	{
		if (!IsCardTableHeader(headerFields))
		{
			return false;
		}

		foreach (string column in headerFields)
		{
			if (string.Equals((column ?? string.Empty).Trim().TrimStart('\uFEFF'), "SpatialShape", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// 按「先到先得」把字段行合并进 target：重复 CardId 跳过并返回一条告警文本（调用方负责打印）。
	/// 表头 / 空行 / 非卡表行（CardId 解析不出正整数）自动跳过；列数不足的行由 CardSpatialSpec 的越界取值兜底。
	/// 文件顺序决定优先级，因此调用方必须先对文件路径排序再逐表调用本方法。
	/// </summary>
	public static List<string> Merge(IEnumerable<string[]> fieldRows, Dictionary<int, CardSpatialSpec> target, string sourceLabel = null)
	{
		List<string> conflicts = new List<string>();
		if (fieldRows == null || target == null)
		{
			return conflicts;
		}

		foreach (string[] fields in fieldRows)
		{
			if (fields == null || fields.Length == 0)
			{
				continue;
			}

			CardSpatialSpec spec = CardSpatialSpec.ParseFields(fields);
			if (spec.CardId <= 0)
			{
				continue;   // 表头 / 空行 / 非卡表
			}

			if (target.TryGetValue(spec.CardId, out CardSpatialSpec existing))
			{
				conflicts.Add($"CardId {spec.CardId} 在 {Label(sourceLabel)} 与 {Describe(existing)} 重复，本条已跳过（先到先得）。");
				continue;
			}

			target[spec.CardId] = spec;
		}

		return conflicts;
	}

	private static string Label(string sourceLabel) => string.IsNullOrWhiteSpace(sourceLabel) ? "匿名来源" : sourceLabel;

	private static string Describe(CardSpatialSpec spec) => spec == null ? "已加载条目" : $"已加载条目（{spec.Shape}）";
}
