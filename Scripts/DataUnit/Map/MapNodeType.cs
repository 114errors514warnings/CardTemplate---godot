// MapNodeType.cs
// 地图格点类型枚举 + Stage 目录（层×节点类型）相关常量映射（纯逻辑，不依赖 Godot）。
using System;
using System.Collections.Generic;

public enum MapNodeType
{
	Empty = 0,          // 空白（无配置，不建表）
	NormalCombat = 1,   // 普通敌袭
	HighRiskCombat = 2, // 高危敌袭
	NormalEvent = 3,    // 普通事件
	DangerousEvent = 4, // 危险事件
	Merchant = 5,       // 商人
	Village = 6,        // 村庄（特殊节点）
	Elite = 7,          // 精英（特殊节点）
	Boss = 8,           // Boss（特殊节点）
	Start = 9,          // 起点（特殊节点，非地图可选类型）
}

/// <summary>
/// 节点类型 ↔ Stage 目录文件名的映射（文件名即节点类型标题）。
/// </summary>
public static class MapNodeTypeUtil
{
	/// <summary>有对应 Stage 配置文件（DataBase/Stage/&lt;层&gt;/&lt;类型&gt;.csv）的类型。</summary>
	public static readonly IReadOnlyList<MapNodeType> StageConfigTypes = new MapNodeType[]
	{
		MapNodeType.NormalCombat,
		MapNodeType.HighRiskCombat,
		MapNodeType.NormalEvent,
		MapNodeType.DangerousEvent,
		MapNodeType.Merchant,
		MapNodeType.Village,
		MapNodeType.Elite,
		MapNodeType.Boss,
	};

	private static readonly Dictionary<MapNodeType, string> FileNames = new Dictionary<MapNodeType, string>
	{
		{ MapNodeType.NormalCombat, "普通敌袭" },
		{ MapNodeType.HighRiskCombat, "高危敌袭" },
		{ MapNodeType.NormalEvent, "普通事件" },
		{ MapNodeType.DangerousEvent, "危险事件" },
		{ MapNodeType.Merchant, "商人" },
		{ MapNodeType.Village, "村庄" },
		{ MapNodeType.Elite, "精英" },
		{ MapNodeType.Boss, "Boss" },
	};

	/// <summary>Stage 根目录下的层目录名（顺序固定，Act=1 → 第一层）。</summary>
	public static readonly IReadOnlyList<string> LayerNames = new string[] { "第一层", "第二层", "第三层" };

	public static bool HasStageConfigFile(MapNodeType type) => FileNames.ContainsKey(type);

	public static bool TryGetStageConfigFileName(MapNodeType type, out string fileName)
	{
		return FileNames.TryGetValue(type, out fileName);
	}

	public static string GetStageConfigFileName(MapNodeType type)
	{
		return FileNames.TryGetValue(type, out string fileName) ? fileName : string.Empty;
	}

	public static bool TryParseStageConfigFileName(string fileName, out MapNodeType type)
	{
		type = MapNodeType.Empty;
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return false;
		}

		foreach (KeyValuePair<MapNodeType, string> pair in FileNames)
		{
			if (string.Equals(pair.Value, fileName, StringComparison.OrdinalIgnoreCase))
			{
				type = pair.Key;
				return true;
			}
		}

		return false;
	}

	/// <summary>把关卡序号(Act，从 1 起)映射为 Stage 层目录名；超出范围返回空串。</summary>
	public static string GetLayerNameByAct(int act)
	{
		if (act < 1 || act > LayerNames.Count)
		{
			return string.Empty;
		}

		return LayerNames[act - 1];
	}
}
