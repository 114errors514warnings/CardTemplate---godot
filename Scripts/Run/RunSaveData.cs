// RunSaveData.cs
// 本局进度存档 DTO（纯数据类，无 Godot 依赖，便于 JSON 序列化与 xUnit 单测）。
using System;
using System.Collections.Generic;
using System.Linq;

public static class RunGameModes
{
	public const string OnMap = "OnMap";
	public const string InBattleStart = "InBattleStart";
	public const string InSettlement = "InSettlement";
}

public sealed class RunSaveData
{
	/// <summary>
	/// **当前**存档结构版本 = 5：版本 4 = 背包实例批（P0-17 / P1-19）新增 `BagEntries`（材料 / 道具 / 装备 / 食物的**实例**条目）、
	/// `CarryItemSlots`（局外随身 3 格）、`ActiveFoodEffects`（食物效果的寿命轴）与 `CookedThisRest`（本次休息已烹饪次数）。
	/// 版本 5（2026-10-02 批 E，P0-17 背包界面 / P0-18 装备链）新增：`RunCharacterSlotSave` 的左右手位字段
	/// （旧 `EquippedWeaponDefinitionId` 保留为**左手镜像**）与 `RunBagEntrySave.CarrySlot`（条目的随身格归属）。
	/// `Materials / Items / Equipment` 三个计数字典保留为**汇总视图**（继续写，旧档读起来不丢数）。
	/// 版本 ≤ 4 的旧档由 `MigrateToCurrentSchema()` 就地升级，不弃档。
	/// </summary>
	public const int CurrentSchemaVersion = 5;

	public int SchemaVersion = CurrentSchemaVersion;
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

	// ── 背包实例载体（2026-10-02，P0-17 / P1-19；SchemaVersion 4）──

	/// <summary>
	/// 背包**实例**条目（背包系统交互案 §六）：材料 / 道具 / 装备按定义合并计数，食物恒为独立实例
	/// （各自记录稀有度与剩余有效期）。`Materials / Items / Equipment` 是它的汇总视图，不是另一份真相。
	/// </summary>
	public List<RunBagEntrySave> BagEntries { get; set; } = new List<RunBagEntrySave>();

	/// <summary>局外随身 3 格（空串 = 空位）。默认**不**写入战斗开场（背包系统交互案 §九 第 1 条）。</summary>
	public List<string> CarryItemSlots { get; set; } = new List<string>();

	/// <summary>
	/// 仍在生效的食物效果（食物系统 §三，2026-10-02 口径 ②）：每条带寿命轴剩余量，
	/// 每场战斗开场重新应用一次；战斗结算（BattleCount）/ 时间点变动（TimePoint）/ 跨天休息（DayCount）各扣一次。
	/// </summary>
	public List<RunFoodEffectSave> ActiveFoodEffects { get; set; } = new List<RunFoodEffectSave>();

	/// <summary>本次休息已经烹饪的次数（每次休息上限 2 次；进入营地 / 跨天时清零）。</summary>
	public int CookedThisRest;

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

	/// <summary>
	/// 把任意旧档升级到 `CurrentSchemaVersion`（幂等：已是当前版本时只做非负校验与旧字段归入）。
	/// 读档路径的唯一入口：`RunSession.LoadSave` 反序列化后立即调用，避免各处各写一份迁移。
	/// </summary>
	public void MigrateToCurrentSchema()
	{
		MapState?.MigrateTimePointsToSchema3();
		MapState?.MigrateLegacyNormalEncounterCount();
		MigrateBagEntriesFromLegacyDictionaries();
		EnsureBagCollections();
		MigrateHandsToSchema5();
		MigrateCarrySlotOwnership();
		SchemaVersion = CurrentSchemaVersion;
	}

	/// <summary>
	/// 版本 ≤ 4 → 5 的手位迁移（装备系统交互案 §五）：旧 `EquippedWeaponDefinitionId` → **左手**，
	/// 双手装备同时占右手；随后该字段一律保持为左手镜像（既有读写点与旧档读者照旧可用）。
	/// 幂等：左右手字段已有值时不动，只补镜像。
	/// </summary>
	private void MigrateHandsToSchema5()
	{
		if (CharacterSlots == null)
		{
			return;
		}

		foreach (RunCharacterSlotSave slot in CharacterSlots)
		{
			if (slot == null)
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(slot.LeftHandDefinitionId) && !string.IsNullOrWhiteSpace(slot.EquippedWeaponDefinitionId))
			{
				slot.LeftHandDefinitionId = slot.EquippedWeaponDefinitionId;
				if (string.IsNullOrWhiteSpace(slot.RightHandDefinitionId)
					&& ItemNameResolver.HandsRequiredOfDefinition(slot.EquippedWeaponDefinitionId) >= 2)
				{
					slot.RightHandDefinitionId = slot.EquippedWeaponDefinitionId;
				}
			}

			slot.SyncLegacyWeaponField();
		}
	}

	/// <summary>
	/// 版本 ≤ 4 → 5 的随身格迁移：`CarryItemSlots`（v4 只有这份镜像列表）→ 条目的 `CarrySlot` 字段。
	/// 双向补齐（镜像 → 条目、条目 → 镜像），使「条目的随身位」成为唯一真相、镜像只作兼容视图。
	/// </summary>
	private void MigrateCarrySlotOwnership()
	{
		BagEntries ??= new List<RunBagEntrySave>();
		CarryItemSlots ??= new List<string>();
		while (CarryItemSlots.Count < RunBagSystem.CarryItemSlotCount)
		{
			CarryItemSlots.Add(string.Empty);
		}

		for (int slot = 0; slot < RunBagSystem.CarryItemSlotCount; slot++)
		{
			RunBagEntrySave entry = BagEntries.FirstOrDefault(
				x => x != null && !string.IsNullOrWhiteSpace(x.InstanceId)
					&& string.Equals(x.InstanceId, CarryItemSlots[slot], StringComparison.Ordinal));
			if (entry != null && entry.CarrySlot < 0)
			{
				entry.CarrySlot = slot;
			}
		}

		foreach (RunBagEntrySave entry in BagEntries)
		{
			if (entry == null || entry.CarrySlot < 0 || entry.CarrySlot >= RunBagSystem.CarryItemSlotCount)
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(CarryItemSlots[entry.CarrySlot]))
			{
				CarryItemSlots[entry.CarrySlot] = entry.InstanceId;
			}
		}
	}

	/// <summary>
	/// 版本 3 → 4 的背包迁移：把 `Materials / Items / Equipment` 计数字典**展开**成 `BagEntries`
	/// （每个定义一条、`Count` 原样搬过来），不丢数量。已有 `BagEntries` 的档不动（幂等）。
	/// </summary>
	private void MigrateBagEntriesFromLegacyDictionaries()
	{
		BagEntries ??= new List<RunBagEntrySave>();
		if (BagEntries.Count > 0)
		{
			return;
		}

		AppendLegacyEntries(BagCategory.Material, Materials);
		AppendLegacyEntries(BagCategory.Item, Items);
		AppendLegacyEntries(BagCategory.Equipment, Equipment);
	}

	private void AppendLegacyEntries(BagCategory category, Dictionary<int, int> legacy)
	{
		if (legacy == null)
		{
			return;
		}

		foreach (KeyValuePair<int, int> pair in legacy)
		{
			if (pair.Key <= 0 || pair.Value <= 0)
			{
				continue;
			}

			RunBagEntrySave entry = RunBagSystem.CreateEntry(this, category, pair.Key, count: pair.Value);
			BagEntries.Add(entry);
		}
	}

	/// <summary>补齐四个集合字段：旧档反序列化后可能是 null（`List` 缺字段），这里统一兜住。</summary>
	private void EnsureBagCollections()
	{
		BagEntries ??= new List<RunBagEntrySave>();
		CarryItemSlots ??= new List<string>();
		ActiveFoodEffects ??= new List<RunFoodEffectSave>();
		while (CarryItemSlots.Count < RunBagSystem.CarryItemSlotCount)
		{
			CarryItemSlots.Add(string.Empty);
		}
	}

}

public sealed class RunCharacterSlotSave
{
	public int CharacterId;
	public int CurrentHp;
	public int MaxHp;

	/// <summary>
	/// **左手位**装备的定义名（空串 = 空手位）；双手装备时左右手同名（SchemaVersion 5）。
	/// 局外手位的唯一真相 = 左右手两字段（装备系统交互案 §五）。
	/// </summary>
	public string LeftHandDefinitionId = string.Empty;

	/// <summary>**右手位**装备的定义名（空串 = 空手位）。</summary>
	public string RightHandDefinitionId = string.Empty;

	/// <summary>
	/// **保留字段**（背包系统交互案 §五 的旧档口径）：旧的单件装备位，现为 <see cref="LeftHandDefinitionId"/> 的
	/// **镜像**（每次手位写入同步一次）。保留是为了不动既有读写点与旧档；新代码一律读写左右手字段。
	/// </summary>
	public string EquippedWeaponDefinitionId = string.Empty;

	/// <summary>把镜像字段同步为左手（局外手位写入后调用；`RunEquipmentSystem` 是唯一写入口）。</summary>
	public void SyncLegacyWeaponField() => EquippedWeaponDefinitionId = LeftHandDefinitionId ?? string.Empty;
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

	/// <summary>
	/// 时间点**进程**（地图玩法 §5.1）。P0-3：精度 = 0.1（对应战斗 1 回合），**只增不减** ——
	/// 任何玩法结果都不能让它回退（回退会改动跨天边界并使休息回复公式失效），负向写入一律由
	/// `TryAddTimePoints` / `TrySpendTimePoints` 拒绝。累加对齐 0.1，避免浮点漂移。
	/// </summary>
	public float TimePoints;

	/// <summary>进入休息当刻的**当天剩余时间点**：营地界面显示与回复公式取值（落档 —— 读档重进同一个休息界面不重算）。</summary>
	public float RestRemainingTimePoints;

	/// <summary>
	/// **当天是否已耗尽而等待休息**：进程在战斗 / 移动中跨过日界（每满 4 点）时置位 —— 那一刻「这一天」的剩余
	/// 已写进 `RestRemainingTimePoints`，必须先回营地休息才能继续（地图交互 §五「剩余为 0 时强制进入休息」）。
	/// 休息结算（`AdvanceToNextDay`）后清除；单独落档，读档重进不会漏掉这次强制休息。
	/// </summary>
	public bool PendingRestDay;

	/// <summary>当前天数（1 起，由进程推导，不落档）。</summary>
	public int CurrentDay => RunTimePoints.DayIndex(TimePoints);

	/// <summary>当天剩余时间点（0 ~ 4，由进程推导，不落档）。</summary>
	public float RemainingToday => RunTimePoints.RemainingToday(TimePoints);

	/// <summary>时间点是否够支付一项代价（不足则禁止前往，转营地转场）。</summary>
	public bool CanSpendTimePoints(float cost) => RunTimePoints.CanSpend(TimePoints, cost);

	/// <summary>累加时间点进程（移动 0.3 / 战斗每回合 0.1 / 事件代价）。**拒绝负向写入**：时间点是单调递增的运行时钟。</summary>
	public bool TryAddTimePoints(float delta, out string error)
	{
		error = string.Empty;
		if (float.IsNaN(delta) || delta < 0f)
		{
			error = $"时间点只能消耗、不能回复（收到 {delta}）。";
			return false;
		}

		float value = RunTimePoints.Quantize(TimePoints + delta);
		if (value <= TimePoints)
		{
			return false; // 不足一个计量单位（0.1）的增量不进账
		}

		// 跨过日界 = 这一天就此耗尽：把「那一天」的剩余记成回复比例来源，并置为待休息（强制营地转场）。
		if (RunTimePoints.DayIndex(value) > RunTimePoints.DayIndex(TimePoints))
		{
			RestRemainingTimePoints = RunTimePoints.RemainingToday(TimePoints);
			PendingRestDay = true;
		}

		TimePoints = value;
		return true;
	}

	/// <summary>支付一项时间点代价（移动 / 节点交互 / 事件选项）：不足则整笔拒绝，不允许透支。</summary>
	public bool TrySpendTimePoints(float cost, out string error)
	{
		error = string.Empty;
		if (float.IsNaN(cost) || cost < 0f)
		{
			error = $"时间点代价不能为负（收到 {cost}）。";
			return false;
		}

		if (!RunTimePoints.CanSpend(TimePoints, cost))
		{
			error = $"时间点不足：当天剩余 {RunTimePoints.Format(RemainingToday)}，需要 {RunTimePoints.Format(cost)}。";
			return false;
		}

		return TryAddTimePoints(cost, out error);
	}

	/// <summary>
	/// 进入休息：主动结束当天时记下当刻的当天剩余；若这一天是**耗尽**来的（`PendingRestDay`），
	/// 保留跨日界那一刻记下的剩余（那才是"这一天"的剩余），并确保仍处于待休息态。
	/// </summary>
	public void BeginRestDay()
	{
		if (!PendingRestDay)
		{
			RestRemainingTimePoints = RemainingToday;
		}

		PendingRestDay = true;
	}

	/// <summary>休息结算完成：推进到新一天（当天剩余作废；进程只增不减，补齐到次日边界），并清掉待休息标记。</summary>
	public void AdvanceToNextDay()
	{
		float next = RunTimePoints.NextDayStart(TimePoints);
		if (next > TimePoints)
		{
			TimePoints = RunTimePoints.Quantize(next);
		}

		PendingRestDay = false;
	}

	/// <summary>旧档迁移（SchemaVersion ≤ 2 → 3）：`TimePoints` 由 int 升为 0.1 精度的 float 语义，非负并对齐步长。</summary>
	public void MigrateTimePointsToSchema3()
	{
		TimePoints = RunTimePoints.Quantize(TimePoints);
		RestRemainingTimePoints = RunTimePoints.Quantize(RestRemainingTimePoints);
		// 旧档停在日界上（int 语义下常见）时补一次待休息：否则那一天的耗尽是断的。
		if (TimePoints > 0f && RunTimePoints.RemainingToday(TimePoints) >= RunTimePoints.PointsPerDay)
		{
			PendingRestDay = true;
			RestRemainingTimePoints = 0f;
		}
	}
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
/// <summary>背包条目类别（背包系统交互案 §二 的页签口径：材料 / 道具 / 装备 / 食物）。</summary>
public enum BagCategory
{
	Material = 0,
	Item = 1,
	Equipment = 2,
	Food = 3,
}

/// <summary>
/// 背包的一条**实例**条目（SchemaVersion 4）。材料 / 道具 / 装备按 `DefinitionKey` 合并计数；
/// 食物恒为一条一实例（`ExpireDaysRemaining` 各自衰减、到 0 腐坏移除）。
/// </summary>
public sealed class RunBagEntrySave
{
	/// <summary>局内唯一实例键（`bag-{类别}{定义键}-{序号}`）：食物按它区分同类不同批。</summary>
	public string InstanceId = string.Empty;

	/// <summary>定义表主键（MaterialId / ItemId / FoodId / WeaponId）。</summary>
	public int DefinitionKey;

	/// <summary>定义名（材料 / 道具 / 食物 / 装备的显示名，冗余保存便于脱离配表展示）。</summary>
	public string DefinitionId = string.Empty;

	/// <summary>`BagCategory`。</summary>
	public int Category;

	public int Count = 1;

	/// <summary>`ItemRarity`：普通 / 罕见 / 稀有。</summary>
	public int Rarity;

	/// <summary>食物剩余有效期（天）；非食物为 -1。到 0 即腐坏移除（食物系统 §二）。</summary>
	public int ExpireDaysRemaining = -1;

	/// <summary>
	/// 随身格归属（SchemaVersion 5）：`-1` = 在背包里；`0..2` = 已放进局外随身 3 格。
	/// 放进随身格的条目**不占背包负荷**（背包系统交互案 §四），也不出现在背包列表里；
	/// `RunSaveData.CarryItemSlots` 是它的兼容镜像（同一次写入一起同步）。
	/// </summary>
	public int CarrySlot = -1;

	/// <summary>该条目是否在背包里（不在随身格上）。</summary>
	public bool IsInBag => CarrySlot < 0;

	public BagCategory CategoryEnum => (BagCategory)Category;
}

/// <summary>
/// 一条仍在生效的食物效果（SchemaVersion 4，食物系统 §三 / 2026-10-02 口径 ②）。
/// `DurationKind` 取 `FoodEffectDurationKind`，`Remaining` 按对应轴扣减。
/// </summary>
public sealed class RunFoodEffectSave
{
	/// <summary>`CardSimulator.EffectType`。</summary>
	public int EffectType;

	public List<int> Params { get; set; } = new List<int>();

	/// <summary>`FoodEffectDurationKind`（0 = None / 1 = BattleCount / 2 = TimePoint / 3 = DayCount）。</summary>
	public int DurationKind = 1;

	/// <summary>剩余寿命数量：BattleCount = 剩余场次、DayCount = 剩余天数、TimePoint = 剩余时间点（0.1 精度）。</summary>
	public float Remaining = 1f;

	/// <summary>来源食物显示名（面板 / 日志用）。</summary>
	public string SourceFoodId = string.Empty;
}


