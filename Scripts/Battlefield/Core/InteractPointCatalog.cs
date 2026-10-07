// InteractPointCatalog.cs
// **交互点定义表**（`DataBase/Level/InteractPoint.csv`）的读取与校验（纯逻辑 + 一处 Godot 读表入口）。
// 口径出处：README/功能说明文档/数据系统/数据配置/配表规范.md §「交互点定义表」（新增，2026-10-06）、
//   README/施工文档/2026/2026.10/10月施工文档.md §33.2.1。
// 一句话：**地点内一切可交互单位（设施 / 商人 / 入口格 / 离开格）都是同一个 `ObjectType = InteractPoint`**，
//   区别只在关卡 CSV 的 `DefinitionId` 指向本表的哪一行 —— 与「不同的怪都是 Monster、靠 DefinitionId 区分」同构。
// 分工：本类只解析**行内参数**（DefinitionId / Name / Trigger / Ui）；机制（旅馆过夜、锻造配方、搜寻权重、
//   货架）仍由各自的系统表与交互案承载，不往本表堆（配表规范同条）。
// 判表纪律：表头顺序即列序；`Trigger` 取 `None / Door / Adjacent`；`DefinitionId` 表内唯一、不得含空格；
//   空单元格必须留空（`Ui = None` 是**枚举值**「不开界面」，与「空值禁止写字面量 None」的数据纪律不是一回事）。
using System;
using System.Collections.Generic;

namespace CardSimulator.Battlefield;

/// <summary>交互点触发形态（`InteractPoint.csv` 的 `Trigger` 列）。</summary>
public enum InteractTriggerKind
{
	/// <summary>无可交互内容：踏入不做事（入口格）。</summary>
	None = 0,

	/// <summary>踏入门口格触发（设施 / 离开格；本体格不可通行，见 `Extra = Door`）。</summary>
	Door = 1,

	/// <summary>走到相邻格自动触发（商人口径；商人本体不可通行，靠相邻触发）。</summary>
	Adjacent = 2,
}

/// <summary>交互点定义表的一行。</summary>
public sealed record InteractPointDefinition(string DefinitionId, string Name, InteractTriggerKind Trigger, string Ui);

public static class InteractPointCatalog
{
	public const string TablePath = "res://DataBase/Level/InteractPoint.csv";

	/// <summary>固定表头（顺序即列序，与配表规范「交互点定义表」一致）。</summary>
	public static readonly string[] Header =
	{
		"DefinitionId", "Name", "Trigger", "Ui", "TimePointCost", "GoldCost", "Params",
	};

	/// <summary>`Ui` 列表示「不开界面」的枚举值（**不是**空值里的字面量 `None`，见文件头判表纪律）。</summary>
	public const string UiNone = "None";

	/// <summary>入口格（世界地图落点）的定义 ID。</summary>
	public const string EntranceId = "Entrance";

	/// <summary>离开格的定义 ID（踏入 = 该关卡完成）。</summary>
	public const string ExitId = "Exit";

	/// <summary>商人的定义 ID（相邻自动触发）。</summary>
	public const string MerchantId = "Merchant";

	/// <summary>关卡 CSV `Extra` 列里表示「门口格」的值。</summary>
	public const string DoorExtraValue = "Door";

	/// <summary>读表（**只有运行期与烟测调它**；纯逻辑单测请用 <see cref="Parse"/>）。</summary>
	public static List<InteractPointDefinition> Load() => Parse(LoadCsv.LoadCSVLines(TablePath));

	public static List<InteractPointDefinition> Parse(IEnumerable<string> lines)
	{
		List<InteractPointDefinition> table = new List<InteractPointDefinition>();
		bool headerSeen = false;
		int lineNumber = 0;
		foreach (string line in lines ?? Array.Empty<string>())
		{
			lineNumber++;
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] cells = LoadCsv.ParseCSVFields(line);
			if (!headerSeen)
			{
				ValidateHeader(cells);
				headerSeen = true;
				continue;
			}

			string context = $"[InteractPoint] 第 {lineNumber} 行（{line}）";
			if (cells.Length != Header.Length)
			{
				throw new FormatException($"{context}：列数不符（期望 {Header.Length} 列，实际 {cells.Length} 列）");
			}

			string definitionId = cells[0].Trim();
			if (definitionId.Length == 0)
			{
				throw new FormatException($"{context}：DefinitionId 不能为空");
			}

			if (definitionId.IndexOf(' ') >= 0)
			{
				throw new FormatException($"{context}：DefinitionId 不得含空格（`{definitionId}`）");
			}

			if (table.Exists(x => string.Equals(x.DefinitionId, definitionId, StringComparison.Ordinal)))
			{
				throw new FormatException($"{context}：DefinitionId 重复（`{definitionId}`）");
			}

			table.Add(new InteractPointDefinition(
				definitionId,
				cells[1].Trim(),
				ParseTrigger(cells[2], context),
				cells[3].Trim()));
		}

		if (!headerSeen)
		{
			throw new FormatException("[InteractPoint] 缺表头");
		}

		return table;
	}

	/// <summary>表头校验（顺序即列序，大小写不敏感）；不符抛 <see cref="FormatException"/>。</summary>
	public static void ValidateHeader(IReadOnlyList<string> cells)
	{
		if (cells == null || cells.Count != Header.Length)
		{
			throw new FormatException($"[InteractPoint] 表头列数不符：期望 {Header.Length} 列，实际 {(cells?.Count ?? 0)} 列");
		}

		for (int i = 0; i < Header.Length; i++)
		{
			if (!string.Equals((cells[i] ?? string.Empty).Trim(), Header[i], StringComparison.OrdinalIgnoreCase))
			{
				throw new FormatException($"[InteractPoint] 第 {i + 1} 列表头应为 `{Header[i]}`，实际 `{(cells[i] ?? string.Empty).Trim()}`");
			}
		}
	}

	/// <summary>`Trigger` 列解析；空单元格 = `None`（无可交互内容），写错值报错不静默降级。</summary>
	public static InteractTriggerKind ParseTrigger(string raw, string context)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0 || string.Equals(text, "None", StringComparison.OrdinalIgnoreCase))
		{
			return InteractTriggerKind.None;
		}

		if (Enum.TryParse(text, true, out InteractTriggerKind trigger) && Enum.IsDefined(trigger))
		{
			return trigger;
		}

		throw new FormatException($"{context}：Trigger 不认识：`{text}`（可用：None / Door / Adjacent）");
	}

	/// <summary>按 `DefinitionId` 找一行（大小写不敏感；找不到返回 null，由调用方判成配置错）。</summary>
	public static InteractPointDefinition Find(IReadOnlyList<InteractPointDefinition> table, string definitionId)
	{
		foreach (InteractPointDefinition row in table ?? Array.Empty<InteractPointDefinition>())
		{
			if (row != null && string.Equals(row.DefinitionId, (definitionId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
			{
				return row;
			}
		}

		return null;
	}
}
