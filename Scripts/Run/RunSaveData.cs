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
	public Dictionary<int, int> Materials { get; set; } = new Dictionary<int, int>();
	public Dictionary<int, int> Items { get; set; } = new Dictionary<int, int>();
	public Dictionary<int, int> Equipment { get; set; } = new Dictionary<int, int>();

	/// <summary>被怪物窃取的金币账本（**按怪物实例分开记**）：每条记录对应关卡里的一个怪物实例，
	/// 战斗内按实例实时记账（进入存档），结算时只返还"该实例被击杀"的那一条。</summary>
	public List<StolenGoldEntry> StolenGoldFromMonsters { get; set; } = new List<StolenGoldEntry>();

	public RunMapStateSave MapState { get; set; } = new RunMapStateSave();

	// ── 待处理战斗（InBattleStart）──
	/// <summary>遭遇层目录名（第一层…）。</summary>
	public string PendingEncounterLayer = string.Empty;
	public int PendingEncounterNodeType;
	public string PendingEncounterName = string.Empty;
	public List<int> PendingMonsterIds { get; set; } = new List<int>();
	public int PendingDropTableId;
	public string PendingLevelId = string.Empty;
	public string PendingContentType = string.Empty;
	public string PendingContentId = string.Empty;
	public int PendingSourceNodeId = -1;

	/// <summary>待处理事件（InBattleStart 且 `PendingContentType = Event`）的来源节点类型（`MapNodeType`）：
	/// 非战斗来源发卡的结算来源名 / 放弃日志的 `{节点类型}` 用它（交互案 §5.7）。</summary>
	public int PendingSourceNodeType;

	// ── 结算未领取（InSettlement）──
	public string SettlementEncounterName = string.Empty;
	public int SettlementDropTableId;
	public List<int> SettlementCandidateCardIds { get; set; } = new List<int>();
	/// <summary>已点击领取的非卡牌奖励键，防止结算读档后重复领取。</summary>
	public List<string> SettlementClaimedRewardKeys { get; set; } = new List<string>();

	/// <summary>每份卡牌奖励（= 结算面板上一个卡牌 Tab）的槽位、角色与候选；重开面板沿用落档候选，不重抽。</summary>
	public List<SettlementCardPoolSave> SettlementCardPools { get; set; } = new List<SettlementCardPoolSave>();

	/// <summary>已领取的卡牌份（未领的份不出现）：每份只能领一次、只能领一张。</summary>
	public List<SettlementCardClaimSave> SettlementCardClaims { get; set; } = new List<SettlementCardClaimSave>();

	/// <summary>结算面板是否已被关闭：读档重进时 false → 直接复现面板，true → 只显示待领取浮窗。</summary>
	public bool SettlementPanelClosed;

	/// <summary>放弃确认弹窗的「本局游戏内不再显示」勾选（本局有效、落档；新局 / 放弃本局后重置）。</summary>
	public bool SuppressAbandonSettlementConfirm;

	/// <summary>本次结算来源的节点类型（`MapNodeType`）：浮窗来源名与放弃日志的 `{节点类型}` 用它。</summary>
	public int SettlementSourceNodeType;

	/// <summary>本次结算的卡牌折损档位 = **被取消的份数**（0 = 全额 3 份、1 = 2 份、2 = 1 份）。</summary>
	public int SettlementLossTier;

	/// <summary>本次结算的击败比例（综合价值点数口径，P2-10.3）；全额 / 非战斗来源为 1.0。</summary>
	public double SettlementValueRatio = 1.0;

	/// <summary>
	/// 战后战场快照（交互案 §七 4 改口径：**位置落档**）：关闭结算面板后的战场布局 ——
	/// 单位位置 / 存活 / 生命、地面物件、每个角色槽的随身道具与左右手装备。
	/// 读档重进结算界面时按它重建同一战场（`RunBattleScene` 的 `InSettlement` 分支）；
	/// 为空 = 没有可重建的战后战场（事件 / 商人发卡、旧档，或结算已完成）。
	/// </summary>
	public RunPostBattleSave PostBattleBattlefield { get; set; }
}

public sealed class RunCharacterSlotSave
{
	public int CharacterId;
	public int CurrentHp;
	public int MaxHp;
	/// <summary>局外装备状态；战斗只还原此值，不在每场战斗创建职业默认武器。</summary>
	public string EquippedWeaponDefinitionId = string.Empty;
}

public sealed class RunDeckEntry
{
	public int CardId;
	public int PermanentUpgradeLevel;
}

/// <summary>单个怪物实例的窃取记录：`InstanceId` 取自关卡 CSV 的对象实例 ID（如 `F1-006-M01`），
/// 同一 `MonsterId` 的多只怪物各占一条，互不合并。</summary>
public sealed class StolenGoldEntry
{
	public string InstanceId = string.Empty;
	public int MonsterId;
	public int Amount;
}

/// <summary>一份卡牌奖励的落档：槽位、角色与候选卡（`SlotIndex = -1` = 旧档兼容的单份奖励）。</summary>
public sealed class SettlementCardPoolSave
{
	public int SlotIndex = -1;
	public int CharacterId;
	public List<int> CandidateCardIds { get; set; } = new List<int>();
}

/// <summary>一份卡牌奖励的领取记录（未领取的份不出现）。</summary>
public sealed class SettlementCardClaimSave
{
	public int SlotIndex = -1;
	public int CardId;
}

/// <summary>进入结算（`InBattleStart → InSettlement`）的一次性入参：来源、掉落表、卡牌份与折损结果。</summary>
public sealed class SettlementStartRequest
{
	/// <summary>来源名（战斗 = 关卡名 / 遭遇名；事件 / 商人 = 事件名 / 节点名）。</summary>
	public string SourceName = string.Empty;

	/// <summary>来源节点类型（浮窗来源名与放弃日志的 `{节点类型}`）。</summary>
	public MapNodeType SourceNodeType = MapNodeType.Empty;

	public int DropTableId;

	/// <summary>本次卡牌奖励份（折损后；非战斗来源不折损）。候选沿用这里的抽样结果，重进不重抽。</summary>
	public List<SettlementCardPoolSave> CardPools { get; set; } = new List<SettlementCardPoolSave>();

	/// <summary>折损档位 = 被取消的份数（0 / 1 / 2）。</summary>
	public int LossTier;

	/// <summary>击败比例（综合价值点数口径）；全额 / 非战斗来源为 1.0。</summary>
	public double ValueRatio = 1.0;
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

	/// <summary>旧档（SchemaVersion ≤ 2）遗留的全局普通敌袭计数：读档时迁入按层计数后清零（P2-11）。</summary>
	public int NormalEncounterIndex;

	/// <summary>普通敌袭分档计数：**按层（Act）独立**（P2-11）：键 = 层号，值 = 该层已打场次。</summary>
	public Dictionary<int, int> NormalEncounterCounts { get; set; } = new Dictionary<int, int>();

	/// <summary>本局当前层的普通敌袭已打场次（地图取档位 / 进战斗取档位用）。</summary>
	public int CurrentNormalEncounterCount => GetNormalEncounterCount(Act);

	/// <summary>取该层已打的普通敌袭场次（无记录 = 0）。</summary>
	public int GetNormalEncounterCount(int act)
	{
		if (NormalEncounterCounts != null && NormalEncounterCounts.TryGetValue(act, out int count))
		{
			return count;
		}

		return 0;
	}

	/// <summary>该层普通敌袭打过一场：只写回该层的计数（进新层自然从第 1 场重算）。</summary>
	public void IncrementNormalEncounterCount(int act)
	{
		if (NormalEncounterCounts == null)
		{
			NormalEncounterCounts = new Dictionary<int, int>();
		}

		NormalEncounterCounts[act] = GetNormalEncounterCount(act) + 1;
	}

	public void IncrementCurrentNormalEncounterCount()
	{
		IncrementNormalEncounterCount(Act);
	}

	/// <summary>旧档迁移：遗留的全局计数按「当前 Act」归入（P2-11 口径），只迁一次，之后旧字段恒为 0。</summary>
	public void MigrateLegacyNormalEncounterCount()
	{
		if (NormalEncounterCounts == null)
		{
			NormalEncounterCounts = new Dictionary<int, int>();
		}

		if (NormalEncounterIndex > 0 && !NormalEncounterCounts.ContainsKey(Act))
		{
			NormalEncounterCounts[Act] = NormalEncounterIndex;
		}

		NormalEncounterIndex = 0;
	}

	/// <summary>时间点计数（占位：本期不结算玩法，仅存取）。</summary>
	public int TimePoints;
}

/// <summary>
/// 战后战场快照（纯数据，无 Godot 依赖）：进入战后操作态时落档一次，之后每次战后移动 / 拾取再落档一次，
/// 使「关掉面板 → 走几步 / 捡东西 → 退出再继续」回到的是同一张战场。
/// </summary>
public sealed class RunPostBattleSave
{
	/// <summary>导出时的关卡 ID（`PendingLevelId`）：与重建时的待处理内容不一致就不还原。</summary>
	public string LevelId = string.Empty;

	/// <summary>战场地图 ID（`BattleMapDefinition.MapId`）：与重建战场不一致就不还原。</summary>
	public string MapId = string.Empty;

	/// <summary>导出时的回合号：重建后 HUD 的「回合 N」与存档当刻一致。</summary>
	public int Round = 1;

	/// <summary>导出时选中的角色槽位：重建后选中的是该角色。</summary>
	public int SelectedSlotIndex;

	/// <summary>全体单位的战场布局（含已退场的敌人：退场记录让重建后的战场保持同样的空位）。</summary>
	public List<RunUnitPlacementSave> Units { get; set; } = new List<RunUnitPlacementSave>();

	/// <summary>地面物件（道具 / 装备 / 陷阱）：战后拾取的结果就体现在这里（拾走后不再出现）。</summary>
	public List<RunGroundObjectSave> GroundObjects { get; set; } = new List<RunGroundObjectSave>();

	/// <summary>每个角色槽（与 `CharacterSlots` 一一对应）的随身道具与左右手装备。</summary>
	public List<RunLoadoutSave> Loadouts { get; set; } = new List<RunLoadoutSave>();
}

/// <summary>
/// 快照里的一个单位：位置 + 在场状态 + 生命。`OrderIndex` = 导出时的占位枚举序（重建战场与导出同源 → 同序），
/// 重建时按它匹配；`SlotIndex`（玩家）与 `InstanceKey` / `Name` 作为兜底匹配键。
/// </summary>
public sealed class RunUnitPlacementSave
{
	public int OrderIndex;
	public int UnitId;
	public string Name = string.Empty;

	/// <summary>`BattlefieldRole`：0 = 玩家、1 = 敌人、2 = 保护目标。</summary>
	public int Role;

	/// <summary>怪物实例键（关卡 CSV 的 `InstanceId`）：跨场次稳定，用于兜底匹配。</summary>
	public string InstanceKey = string.Empty;

	/// <summary>玩家所属角色槽位（非玩家为 -1）。</summary>
	public int SlotIndex = -1;

	public int Q;
	public int R;

	/// <summary>`BattlefieldPresence`：0 = 在场、1 = 已阵亡、2 = 已离场。</summary>
	public int Presence;

	public int Hp;
}

/// <summary>地面物件（或随身 / 手位上的物件）的完整定义 + 所在格：位置落档后能重建同一个物品堆。</summary>
public sealed class RunGroundObjectSave
{
	public string InstanceId = string.Empty;
	public string DefinitionId = string.Empty;

	/// <summary>`GroundObjectKind`；`TriggerMode` = `EntryTriggerMode`。</summary>
	public int Kind;
	public int TriggerMode;

	public int HandsRequired = 1;
	public int AttackRange = 1;
	public int MoveBonus;
	public int HealAmount;

	/// <summary>投掷型道具的空间规格（`ItemSpatialShape` 与射程 / 半径 / 长度 / 陷阱 ID / 伤害）。</summary>
	public int SpatialShape;
	public int ItemMaxRange = 1;
	public int ItemRadius = 1;
	public int ItemLength = 1;
	public string ItemTrapId = string.Empty;
	public int DamageAmount;

	/// <summary>所在格（随身 / 手位上的物件留 0,0）。</summary>
	public int Q;
	public int R;
}

/// <summary>一个角色槽的携带状态（战后拾取后与战斗内不同）：左手 / 右手装备 + 三个随身道具槽。</summary>
public sealed class RunLoadoutSave
{
	public RunGroundObjectSave LeftHand { get; set; }
	public RunGroundObjectSave RightHand { get; set; }
	public List<RunGroundObjectSave> Items { get; set; } = new List<RunGroundObjectSave>();
}

