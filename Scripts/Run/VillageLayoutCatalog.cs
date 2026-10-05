// VillageLayoutCatalog.cs
// `DataBase/WorldMap/VillageLayout.csv` 的**纯逻辑**解析（无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/村庄地图交互案.md §2.2 / §十。
//
// 表头（固定 4 列，顺序即列序）：
//   FacilityId,DisplayName,NodeIds,EntranceNodeId
// 写法口径：
//   · FacilityId 取 7 个枚举名之一：Inn / Guesthouse / Smithy / Restaurant / Forest / Entrance / Exit
//     （前 5 个是设施、带入口格；Entrance / Exit 是「世界地图入口格」与「离开格」，第 4 列留空）；
//   · NodeIds 用 `;` 分隔、**升序写**（运行时不必再排序；`VillagePlot.NodeIds` 仍按写入顺序保存）；
//   · EntranceNodeId = 踏入即触发本设施的那一格（村庄案 §2.2 / §五）——
//     它是唯一可踏入的格，必须在内圈、与本设施占格相邻（由 `VillageLayout.Validate` 兜底）。
// 与物品四表 / 配方表同一风格：表头 / 枚举 / 数值有误一律抛 FormatException，**不静默降级**。
using System;
using System.Collections.Generic;
using System.Globalization;

public static class VillageLayoutCatalog
{
	/// <summary>表头（顺序即列序）。</summary>
	public static readonly string[] Header = { "FacilityId", "DisplayName", "NodeIds", "EntranceNodeId" };

	/// <summary>按固定列序校验表头；不符抛 <see cref="FormatException"/>。</summary>
	public static void ValidateHeader(IReadOnlyList<string> cells)
	{
		if (cells == null || cells.Count != Header.Length)
		{
			throw new FormatException($"[VillageLayout] 表头列数不符：期望 {Header.Length} 列，实际 {(cells?.Count ?? 0)} 列");
		}

		for (int i = 0; i < Header.Length; i++)
		{
			if (!string.Equals((cells[i] ?? string.Empty).Trim(), Header[i], StringComparison.OrdinalIgnoreCase))
			{
				throw new FormatException($"[VillageLayout] 第 {i + 1} 列表头应为 `{Header[i]}`，实际 `{(cells[i] ?? string.Empty).Trim()}`");
			}
		}
	}

	/// <summary>解析整表（传入**含表头**的行序列）；同一 FacilityId 重复出现即报错（一单位一行）。</summary>
	public static VillageLayoutData ParseLines(IEnumerable<string> lines)
	{
		VillageLayoutData data = new VillageLayoutData();
		HashSet<VillagePlotKind> seen = new HashSet<VillagePlotKind>();
		bool headerSeen = false;
		int lineNumber = 0;
		foreach (string line in lines ?? Array.Empty<string>())
		{
			lineNumber++;
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] cells = line.Split(',');
			if (!headerSeen)
			{
				ValidateHeader(cells);
				headerSeen = true;
				continue;
			}

			string context = $"[VillageLayout] 第 {lineNumber} 行（{line}）";
			if (cells.Length != Header.Length)
			{
				throw new FormatException($"{context}：列数不符（期望 {Header.Length} 列，实际 {cells.Length} 列）");
			}

			VillagePlot plot = new VillagePlot
			{
				Kind = ParseKind(cells[0], context),
				DisplayName = ParseName(cells[1], context),
				NodeIds = ParseNodeIds(cells[2], context),
				EntranceNodeId = ParseNodeIdOrNone(cells[3], context, "EntranceNodeId"),
			};

			if (plot.IsFacility && plot.EntranceNodeId < 0)
			{
				throw new FormatException($"{context}：设施必须给出 EntranceNodeId");
			}

			if (!plot.IsFacility && plot.EntranceNodeId >= 0)
			{
				throw new FormatException($"{context}：入口格 / 离开格行不得带 EntranceNodeId（该列留空）");
			}

			if (plot.NodeIds.Count > 1 && !IsAscending(plot.NodeIds))
			{
				throw new FormatException($"{context}：NodeIds 必须升序写（收到 {string.Join(";", plot.NodeIds)}）");
			}

			if (!seen.Add(plot.Kind))
			{
				throw new FormatException($"{context}：FacilityId `{plot.Kind}` 重复（一块单位只写一行）");
			}

			data.Plots.Add(plot);
		}

		if (!headerSeen)
		{
			throw new FormatException("[VillageLayout] 缺表头（首行应为 FacilityId,DisplayName,NodeIds,EntranceNodeId）");
		}

		return data;
	}

	private static bool IsAscending(List<int> nodeIds)
	{
		for (int i = 1; i < nodeIds.Count; i++)
		{
			if (nodeIds[i] <= nodeIds[i - 1])
			{
				return false;
			}
		}

		return true;
	}

	private static VillagePlotKind ParseKind(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (Enum.TryParse(text, true, out VillagePlotKind kind) && Enum.IsDefined(kind))
		{
			return kind;
		}

		throw new FormatException($"{context}：FacilityId 不认识：`{text}`（可用：Inn / Guesthouse / Smithy / Restaurant / Forest / Entrance / Exit）");
	}

	private static string ParseName(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			throw new FormatException($"{context}：DisplayName 不得为空");
		}

		return text;
	}

	/// <summary>`15;22;28;33` → 4 个 NodeId（升序校验见调用处）。</summary>
	private static List<int> ParseNodeIds(string raw, string context)
	{
		List<int> nodeIds = new List<int>();
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			throw new FormatException($"{context}：NodeIds 不得为空");
		}

		foreach (string part in text.Split(';'))
		{
			string item = part.Trim();
			if (item.Length == 0)
			{
				continue;
			}

			nodeIds.Add(ParseNodeId(item, context, "NodeIds"));
		}

		if (nodeIds.Count == 0)
		{
			throw new FormatException($"{context}：NodeIds 不得为空");
		}

		return nodeIds;
	}

	private static int ParseNodeIdOrNone(string raw, string context, string column)
	{
		string text = (raw ?? string.Empty).Trim();
		return text.Length == 0 ? -1 : ParseNodeId(text, context, column);
	}

	private static int ParseNodeId(string raw, string context, string column)
	{
		if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
		{
			throw new FormatException($"{context}：{column} 必须是非负整数 NodeId，实际 `{raw}`");
		}

		return value;
	}
}
