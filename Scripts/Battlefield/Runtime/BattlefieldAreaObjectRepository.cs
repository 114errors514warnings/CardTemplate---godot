// BattlefieldAreaObjectRepository.cs
// 区域物 / 格点效果定义的内存字典（TrapId → AreaObjectSpec），供战场进入触发与回合末结算查表。
// 2026-10-04（T3 + T8 / P2-30 后半）：此前 `OnEntered` 对任何 `Trap` 一律 3 点伤害、不看 `DefinitionId`，
// 现在改为「查表结算」；查不到的行（含旧存档 / 调试用的自定义 TrapId）保留 3 点伤害兜底并打告警。
using System;
using System.Collections.Generic;
using Godot;

namespace CardSimulator.Battlefield;

public static class BattlefieldAreaObjectRepository
{
	/// <summary>区域物 / 格点效果表路径（单表；新表已登记 `DataBase/FilePathRegistry.csv`）。</summary>
	public const string AreaObjectCsvPath = "res://DataBase/Battlefield/AreaObject.csv";

	private static Dictionary<string, AreaObjectSpec> cache;

	/// <summary>读取并缓存全部区域物定义（缺表 / 空表打告警，不抛异常）。</summary>
	public static Dictionary<string, AreaObjectSpec> LoadAll(bool useCache = true)
	{
		if (useCache && cache != null)
		{
			return cache;
		}

		Dictionary<string, AreaObjectSpec> fresh = new Dictionary<string, AreaObjectSpec>(StringComparer.Ordinal);
		List<string[]> rows = LoadAreaObjectCsv.LoadRowsFromCSV(AreaObjectCsvPath);
		foreach (string conflict in AreaObjectCatalog.Merge(rows, fresh, AreaObjectCsvPath))
		{
			GD.PrintErr($"[AreaObject] {conflict}");
		}

		if (fresh.Count == 0)
		{
			GD.PrintErr($"[AreaObject] {AreaObjectCsvPath} 没有可用行 —— 区域物进入触发会全部回落到「3 点伤害」兜底。");
		}

		cache = fresh;
		return cache;
	}

	/// <summary>按 `TrapId` 取定义；未登记返回 null（调用方走兜底）。</summary>
	public static AreaObjectSpec ForId(string trapId)
	{
		if (string.IsNullOrWhiteSpace(trapId))
		{
			return null;
		}

		if (cache == null)
		{
			LoadAll();
		}

		return cache != null && cache.TryGetValue(trapId.Trim(), out AreaObjectSpec spec) ? spec : null;
	}
}
