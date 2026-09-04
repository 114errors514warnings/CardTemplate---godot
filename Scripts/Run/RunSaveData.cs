// RunSaveData.cs
// 本局进度存档 DTO（纯数据类，无 Godot 依赖，便于 JSON 序列化与 xUnit 单测）。
using System;
using System.Collections.Generic;

public static class RunGameModes
{
	public const string OnMap = "OnMap";
	public const string InBattleStart = "InBattleStart";
	public const string InSettlement = "InSettlement";
}

public sealed class RunSaveData
{
	public int SchemaVersion = 2;
	public string SavedAt = string.Empty;

	/// <summary>存档状态机：OnMap / InBattleStart / InSettlement。</summary>
	public string GameMode = RunGameModes.OnMap;

	/// <summary>3 个战斗槽位（与选人顺序一致，允许重复角色）。</summary>
	public List<RunCharacterSlotSave> CharacterSlots { get; set; } = new List<RunCharacterSlotSave>();

	/// <summary>每槽一副永久卡组（与 CharacterSlots 一一对应，CardId + 永久升级级数）。</summary>
	public List<List<RunDeckEntry>> DeckSlots { get; set; } = new List<List<RunDeckEntry>>();

	public int Gold;
	public int Keys;

	public RunMapStateSave MapState { get; set; } = new RunMapStateSave();

	// ── 待处理战斗（InBattleStart）──
	/// <summary>遭遇层目录名（第一层…）。</summary>
	public string PendingEncounterLayer = string.Empty;
	public int PendingEncounterNodeType;
	public string PendingEncounterName = string.Empty;
	public List<int> PendingMonsterIds { get; set; } = new List<int>();
	public int PendingDropTableId;

	// ── 结算未领取（InSettlement）──
	public string SettlementEncounterName = string.Empty;
	public int SettlementDropTableId;
	public List<int> SettlementCandidateCardIds { get; set; } = new List<int>();
}

public sealed class RunCharacterSlotSave
{
	public int CharacterId;
	public int CurrentHp;
	public int MaxHp;
}

public sealed class RunDeckEntry
{
	public int CardId;
	public int PermanentUpgradeLevel;
}

public sealed class RunMapStateSave
{
	/// <summary>当前层（Act=1 → 第一层，对应 DataBase/Stage/第一层）。</summary>
	public int Act = 1;

	/// <summary>地图随机种子（用于确定性重建同一版图）。</summary>
	public int Seed;

	public int LayoutVersion = 1;

	/// <summary>当前位置格点 NodeId（最近一次已结算格点）。</summary>
	public int CurrentNodeId = -1;

	public List<int> VisitedNodeIds { get; set; } = new List<int>();

	/// <summary>本局已打普通敌袭场次（难度分档计数用）。</summary>
	public int NormalEncounterIndex;

	/// <summary>时间点计数（占位：本期不结算玩法，仅存取）。</summary>
	public int TimePoints;
}
