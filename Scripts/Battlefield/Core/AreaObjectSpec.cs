// AreaObjectSpec.cs
// 区域物 / 格点效果定义（纯逻辑）：`DataBase/Battlefield/AreaObject.csv` 的一行。
// 2026-10-04（T3 + T8 / P2-30 后半）：进入触发由「任何 Trap 一律 3 点伤害」改为**按 TrapId 查表结算**；
// 同一张表同时承载「进入触发（OnEnter）」与「停留 / 回合末结算（OnTurnEnd）」，
// 效果内容复用卡表语法（`EffectType` 用 `|` 分隔、`Params` 用 `|` 分组 / `;` 分参），
// 因此施加减益（debuff）与增益（buff）走同一条 `AddState` 通道，不为增益另开枚举。
// Godot I/O 留在 `LoadAreaObjectCsv` / `BattlefieldAreaObjectRepository`，本文件可被纯 .NET 单测直接调用。
using System;
using System.Collections.Generic;

namespace CardSimulator.Battlefield;

/// <summary>格点效果的触发时机（可组合：进入 + 回合末）。</summary>
[Flags]
public enum AreaTriggerTiming
{
	None = 0,
	OnEnter = 1,
	OnTurnEnd = 2,
}

/// <summary>
/// 格点效果的作用对象过滤（相对该格点效果的**来源单位**；`Triggerer` = 触发者 / 停留者）。
/// 「仅敌方 / 仅友方 / 双方 / 仅触发者」四选一，且对增益与减益一视同仁（T8 的「格上 buff 或 debuff」）。
/// </summary>
public enum CellEffectSideFilter
{
	Triggerer = 0,  // 触发者本人（进入者 / 停留在格上的单位）
	Enemy = 1,      // 来源单位的敌人
	Ally = 2,       // 来源单位的友军（含来源自身）
	All = 3,        // 不分阵营
}

/// <summary>地形状态自身的衰减口径（每次回合末结算后如何变化）。</summary>
public enum TerrainDecayTiming
{
	Never = 0,      // 常驻（默认）
	OnTurnEnd = 1,  // 回合末结算后层数 −1，归零即移除
}

/// <summary>一行区域物 / 格点效果定义。</summary>
public sealed class AreaObjectSpec
{
	public string TrapId = string.Empty;
	public string Name = string.Empty;
	public AreaTriggerTiming Timing = AreaTriggerTiming.OnEnter;
	public CellEffectSideFilter SideFilter = CellEffectSideFilter.Triggerer;
	public int MaxTriggers;                             // 0 = 无限次
	public TerrainDecayTiming DecayTiming = TerrainDecayTiming.Never;
	public EffectType[] EffectTypes = Array.Empty<EffectType>();
	public int[][] Params = Array.Empty<int[]>();

	public bool TriggersOnEnter => (Timing & AreaTriggerTiming.OnEnter) != 0;
	public bool TriggersOnTurnEnd => (Timing & AreaTriggerTiming.OnTurnEnd) != 0;
	/// <summary>表里是否写了可结算效果（`None` / 空视为纯标记物）。</summary>
	public bool HasEffects => EffectTypes.Length > 0;

	/// <summary>按表列顺序解析：`TrapId,Name,TriggerTiming,SideFilter,MaxTriggers,DecayTiming,EffectType,Params`。TrapId 为空返回 null。</summary>
	public static AreaObjectSpec ParseFields(string[] fields)
	{
		if (fields == null || fields.Length == 0)
		{
			return null;
		}

		string trapId = Field(fields, 0);
		if (trapId.Length == 0)
		{
			return null;
		}

		AreaObjectSpec spec = new AreaObjectSpec
		{
			TrapId = trapId,
			Name = Field(fields, 1),
			Timing = ParseTiming(Field(fields, 2)),
			SideFilter = ParseSideFilter(Field(fields, 3)),
		};

		spec.MaxTriggers = int.TryParse(Field(fields, 4), out int maxTriggers) && maxTriggers > 0 ? maxTriggers : 0;
		spec.DecayTiming = ParseDecayTiming(Field(fields, 5));
		spec.EffectTypes = ParseEffectTypes(Field(fields, 6));
		spec.Params = ParseParams(Field(fields, 7));
		return spec;
	}

	private static string Field(string[] fields, int index) => index < fields.Length ? (fields[index] ?? string.Empty).Trim() : string.Empty;

	public static AreaTriggerTiming ParseTiming(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return AreaTriggerTiming.OnEnter;
		}

		AreaTriggerTiming timing = AreaTriggerTiming.None;
		foreach (string token in SplitTokens(raw))
		{
			if (Enum.TryParse(token, true, out AreaTriggerTiming parsed) && Enum.IsDefined(typeof(AreaTriggerTiming), parsed))
			{
				timing |= parsed;
			}
		}

		return timing == AreaTriggerTiming.None ? AreaTriggerTiming.OnEnter : timing;
	}

	public static CellEffectSideFilter ParseSideFilter(string raw) =>
		Enum.TryParse(raw, true, out CellEffectSideFilter filter) && Enum.IsDefined(typeof(CellEffectSideFilter), filter)
			? filter : CellEffectSideFilter.Triggerer;

	public static TerrainDecayTiming ParseDecayTiming(string raw) =>
		Enum.TryParse(raw, true, out TerrainDecayTiming decay) && Enum.IsDefined(typeof(TerrainDecayTiming), decay)
			? decay : TerrainDecayTiming.Never;

	private static EffectType[] ParseEffectTypes(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return Array.Empty<EffectType>();
		}

		List<EffectType> types = new List<EffectType>();
		foreach (string token in raw.Split('|'))
		{
			string trimmed = token.Trim();
			if (trimmed.Length == 0 || trimmed.Equals("None", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (Enum.TryParse(trimmed, true, out EffectType type) && Enum.IsDefined(typeof(EffectType), type))
			{
				types.Add(type);
			}
		}

		return types.ToArray();
	}

	private static int[][] ParseParams(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return Array.Empty<int[]>();
		}

		List<int[]> groups = new List<int[]>();
		foreach (string group in raw.Split('|'))
		{
			List<int> values = new List<int>();
			foreach (string part in group.Split(';'))
			{
				if (int.TryParse(part.Trim(), out int value))
				{
					values.Add(value);
				}
			}

			groups.Add(values.ToArray());
		}

		return groups.ToArray();
	}

	private static IEnumerable<string> SplitTokens(string raw) =>
		raw.Split(new[] { '|', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>区域物 / 格点效果表的表结构判定与合并（纯逻辑，供加载器与单测共用）。</summary>
public static class AreaObjectCatalog
{
	public const string TrapIdColumn = "TrapId";

	/// <summary>表头首列是否为 `TrapId`（判定「这是不是格点效果表」）。</summary>
	public static bool IsAreaObjectTableHeader(string[] headerFields) =>
		headerFields != null && headerFields.Length > 0 &&
		Trim(headerFields[0]).Equals(TrapIdColumn, StringComparison.OrdinalIgnoreCase);

	/// <summary>合并多来源行：同 `TrapId` 先到先得，重复的跳过并回传一条告警文案（对齐卡表口径）。</summary>
	public static List<string> Merge(IEnumerable<string[]> rows, Dictionary<string, AreaObjectSpec> target, string source)
	{
		List<string> conflicts = new List<string>();
		if (rows == null || target == null)
		{
			return conflicts;
		}

		foreach (string[] row in rows)
		{
			if (IsHeaderRow(row))
			{
				continue;
			}

			AreaObjectSpec spec = AreaObjectSpec.ParseFields(row);
			if (spec == null)
			{
				continue;
			}

			if (target.ContainsKey(spec.TrapId))
			{
				conflicts.Add($"区域物 {spec.TrapId} 重复定义，已跳过（来源 {source}）。");
				continue;
			}

			target[spec.TrapId] = spec;
		}

		return conflicts;
	}

	/// <summary>纯解析入口（单测用，避免依赖加载器）。</summary>
	public static AreaObjectSpec ParseFields(string[] fields) => AreaObjectSpec.ParseFields(fields);

	private static bool IsHeaderRow(string[] row) =>
		row != null && row.Length > 0 && Trim(row[0]).Equals(TrapIdColumn, StringComparison.OrdinalIgnoreCase);

	private static string Trim(string raw) => (raw ?? string.Empty).Trim().TrimStart('\uFEFF');
}
