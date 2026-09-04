// MapConfigData.cs
// Stage / 掉落表 / 角色卡池 CSV 的数据模型（纯逻辑，不依赖 Godot）。
using System.Collections.Generic;

public enum StageDifficulty
{
	Any = 0, // 无难度区分（无规则类型使用）
	Low = 1,
	Mid = 2,
	High = 3,
}

public enum DropCategory
{
	Card = 0,
	Gold = 1,
	Material = 2,
	Item = 3,
	Key = 4,
}

/// <summary>DataBase/Stage/&lt;层&gt;/&lt;节点类型&gt;.csv 的一行。</summary>
public sealed class StageEncounterRow
{
	public string Name = string.Empty;
	public string Layer = string.Empty;          // 所属层目录名（第一层/…）
	public MapNodeType NodeType = MapNodeType.Empty;
	public StageDifficulty Difficulty = StageDifficulty.Any;
	public int[] MonsterIds = System.Array.Empty<int>();
	public int DropTableId = 0;
	public int Weight = 1;
	public string Note = string.Empty;

	/// <summary>是否可作为「有配置」触发战斗（须解析出至少一个怪物 / 或后续事件类自定语义）。</summary>
	public bool IsUsable => MonsterIds != null && MonsterIds.Length > 0;
}

/// <summary>DataBase/Map/DropTable.csv 的一行。</summary>
public sealed class DropTableEntry
{
	public int DropTableId = 0;
	public DropCategory Category = DropCategory.Card;
	public int RewardParam = 0; // Gold=0；Card=角色过滤(0=当前队伍并集)；Material/Item=物id
	public int Amount = 0;
	public int Weight = 1;
}

/// <summary>DataBase/Card/CharacterRewardPool.csv 的一行：某角色可抽取的卡牌 CSV 来源。</summary>
public sealed class CharacterRewardSource
{
	public int CharacterId = 0;
	public string CardSource = string.Empty; // DataBase/Card/ 下相对路径，如 通用/通用Card.csv
}
