// RunSession.cs
// 单局状态 + 存档读写（autoload 单例，project.godot 注册，跨场景存活）。
using Godot;
using System;
using System.Collections.Generic;

public partial class RunSession : Node
{
	/// <summary>本局存档路径（烟测也用它备份 / 还原，见 RunFlowScene 的存档守卫）。</summary>
	public const string SavePath = "user://run_save_v1.json";

	/// <summary>存档格式版本：与 `RunSaveData.CurrentSchemaVersion` 同步（版本 6 = 左右手位字段 + 条目的随身格归属 + 部位格字段）。</summary>
	public const string SaveSchemaVersion = "6";

	/// <summary>当前局数据（null = 无进行中的局）。</summary>
	public RunSaveData Current { get; private set; }

	/// <summary>等待进入战斗的遭遇层目录名（第一层…），运行时字段不入档。</summary>
	public string PendingEncounterLayer = string.Empty;

	/// <summary>等待进入战斗的已解析遭遇行（含怪物列表 / DropTableId），运行时字段不入档。</summary>
	public StageEncounterRow PendingEncounter;

	public static RunSession Instance { get; private set; }

	public bool HasActiveRun => Current != null;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (ReferenceEquals(Instance, this))
		{
			Instance = null;
		}
	}

	public static bool HasSave()
	{
		return FileAccess.FileExists(SavePath);
	}

	/// <summary>当天剩余时间点（0 ~ 4；无局时为 0）。</summary>
	public float RemainingToday => Current == null ? 0f : Current.MapState.RemainingToday;

	/// <summary>当前天数（1 起；无局时为 1）。</summary>
	public int CurrentDay => Current == null ? 1 : Current.MapState.CurrentDay;

	/// <summary>
	/// 开新局：按角色列表建档（HP = `Character.csv` 上限、默认卡组取自 `CharacterDefaultDeck.csv`、
	/// 初始武器见 <see cref="GetInitialWeaponDefinition"/>），地图从第一层起步，初始背包为空。
	/// </summary>
	public void StartNewRun(IReadOnlyList<int> characterIds, int? seed = null)
	{
		if (characterIds == null || characterIds.Count == 0)
		{
			GD.PrintErr("[RunSession] 开始新局失败：角色列表为空。");
			return;
		}

		RunSaveData data = new RunSaveData
		{
			SchemaVersion = RunSaveData.CurrentSchemaVersion,
			SavedAt = DateTime.Now.ToString("s"),
			GameMode = RunGameModes.OnMap,
			Gold = 0,
			Keys = 0,
			MapState = new RunMapStateSave
			{
				Act = 1,
				Seed = seed ?? (int)(Time.GetTicksUsec() & 0x7FFFFFFF),
				CurrentNodeId = -1,
			},
		};

		for (int i = 0; i < characterIds.Count; i++)
		{
			int characterId = characterIds[i];
			int maxHp = LoadingSystem.CharacterDictionary.TryGetValue(characterId, out Character template) && template != null
				? template.MAX_HP
				: 0;
			RunCharacterSlotSave slot = new RunCharacterSlotSave
			{
				CharacterId = characterId,
				CurrentHp = maxHp,
				MaxHp = maxHp,
			};

			// 初始武器（职业技能默认武器）写进**左右手位**（装备系统交互案 §五：手位 = 左右手两字段，
			// `EquippedWeaponDefinitionId` 由 SetHand 同步为左手镜像）；双手武器开档即占满两槽。
			string initialWeapon = GetInitialWeaponDefinition(characterId);
			RunEquipmentSystem.SetHand(slot, RunEquipmentSystem.LeftHand, initialWeapon);
			if (RunEquipmentSystem.IsTwoHandedDefinition(initialWeapon))
			{
				RunEquipmentSystem.SetHand(slot, RunEquipmentSystem.RightHand, initialWeapon);
			}

			// 部位格集合（SchemaVersion 6）：建档时就补齐到配置格数，与读档迁移同一口径。
			RunEquipmentSystem.EnsureBodySlots(slot);
			data.CharacterSlots.Add(slot);

			List<RunDeckEntry> deck = new List<RunDeckEntry>();
			List<int> defaultCardIds = LoadingSystem.GetCharacterDefaultCardIdListByKey(
				characterId, LoadingSystem.CharacterDefaultDeckCsvPathKey, true);
			foreach (int cardId in defaultCardIds)
			{
				deck.Add(new RunDeckEntry { CardId = cardId, PermanentUpgradeLevel = 0 });
			}

			data.DeckSlots.Add(deck);
		}

		RunBagSystem.EnsureCollections(data); // 新局也补齐集合（随身 3 格 / 背包列表），与 RunBagSystem 的口径一致
		Current = data;
		ClearPendingEncounter();
		Save();
		GD.Print($"[RunSession] 新局开始：{characterIds.Count} 名角色，地图种子 {data.MapState.Seed}。");
	}

	/// <summary>各角色开局武器（口径 = `Weapon.csv` 的 `DefinitionId`）。</summary>
	private static string GetInitialWeaponDefinition(int characterId) => characterId switch
	{
		1002 => "双手剑",
		1003 => "弓箭",
		1004 => "法典",
		_ => string.Empty,
	};
	/// <summary>读档：反序列化 → 就地迁移到当前版本 → 装载（失败保留原状态并返回 false）。</summary>
	public bool LoadSave()
	{
		if (!FileAccess.FileExists(SavePath))
		{
			GD.PrintErr("[RunSession] 存档不存在，无法读取。");
			return false;
		}

		using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PrintErr("[RunSession] 打开存档失败。");
			return false;
		}

		string json = file.GetAsText();
		file.Close();

		RunSaveData data;
		try
		{
			data = RunSaveJson.Deserialize(json);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RunSession] 存档反序列化异常：{ex.Message}");
			return false;
		}

		if (data == null)
		{
			GD.PrintErr("[RunSession] 存档内容为空。");
			return false;
		}

		data.MigrateToCurrentSchema();
		MigrateSettlementCompat(data);
		// 读档清洗（装备系统交互案 §九 第 4 条）：装备表里不存在的 / 部位对不上的装备清空该格并打印，
		// 不丢整档、不炸建场（原先未知装备会让战斗开场直接抛 `ArgumentException`）。
		foreach (string note in RunEquipmentSystem.SanitizeEquipment(data))
		{
			GD.PrintErr($"[RunSession] {note}");
		}

		Current = data;
		ClearPendingEncounter();
		GD.Print($"[RunSession] 读档成功：第 {data.MapState.CurrentDay} 天，存档版本 {data.SchemaVersion}。");
		return true;
	}

	/// <summary>落档：状态改动后立刻写（口径见 9 月施工文档 §39）。</summary>
	public void Save()
	{
		if (Current == null)
		{
			GD.PrintErr("[RunSession] 没有可保存的当前局。");
			return;
		}

		Current.SavedAt = DateTime.Now.ToString("s");
		string json;
		try
		{
			json = RunSaveJson.Serialize(Current);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RunSession] 存档序列化异常：{ex.Message}");
			return;
		}

		using (FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write))
		{
			if (file == null)
			{
				GD.PrintErr("[RunSession] 写入存档失败（无法创建文件）。");
				return;
			}

			file.StoreString(json);
			file.Close();
		}
	}

	/// <summary>删除存档文件（`user://` 不是绝对路径 → 先 GlobalizePath）。</summary>
	public static void DeleteSave()
	{
		if (FileAccess.FileExists(SavePath))
		{
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
			GD.Print("[RunSession] 已删除存档。");
		}
	}

	/// <summary>清掉内存中的当前局（不删档）。</summary>
	public void ClearCurrent()
	{
		Current = null;
		ClearPendingEncounter();
	}

	/// <summary>放弃本局：删档 + 清当前局。</summary>
	public void AbortRun()
	{
		DeleteSave();
		ClearCurrent();
		GD.Print("[RunSession] 已放弃本局。");
	}

	/// <summary>取角色槽（越界返回 null）。</summary>
	public RunCharacterSlotSave GetSlot(int index)
	{
		if (Current == null || index < 0 || index >= Current.CharacterSlots.Count)
		{
			return null;
		}

		return Current.CharacterSlots[index];
	}

	/// <summary>取该槽的永久卡组（越界返回空表）。</summary>
	public List<RunDeckEntry> GetSlotDeck(int index)
	{
		if (Current == null || index < 0 || index >= Current.DeckSlots.Count)
		{
			return new List<RunDeckEntry>();
		}

		return Current.DeckSlots[index];
	}

	/// <summary>把一张卡加入该槽的永久卡组（结算领卡 / 事件发卡共用）。</summary>
	public void AddCardToSlotDeck(int slotIndex, int cardId, int permanentUpgradeLevel = 0)
	{
		if (Current == null || cardId <= 0 || slotIndex < 0 || slotIndex >= Current.DeckSlots.Count)
		{
			return;
		}

		Current.DeckSlots[slotIndex].Add(new RunDeckEntry
		{
			CardId = cardId,
			PermanentUpgradeLevel = Math.Max(0, permanentUpgradeLevel),
		});
	}

	/// <summary>记录当前位置格点（移动到达 / 进入节点时调用）并落档。</summary>
	public void SetCurrentNode(int nodeId)
	{
		if (Current == null)
		{
			return;
		}

		Current.MapState.CurrentNodeId = nodeId;
		Save();
	}

	/// <summary>
	/// 节点内容完成（或经过已访问格）：把当前格点记入已访问并落档。**幂等** —— 同一个格点只记一次；
	/// 普通敌袭的档位计数由 `RunBattleScene` 在胜利结算时按 `NormalCombat` 增量（P2-11），这里不重复计数。
	/// </summary>
	public void MarkCurrentNodeVisitedAndAdvanceEncounter()
	{
		if (Current == null)
		{
			return;
		}

		RunMapStateSave state = Current.MapState;
		if (state.CurrentNodeId >= 0 && !state.VisitedNodeIds.Contains(state.CurrentNodeId))
		{
			state.VisitedNodeIds.Add(state.CurrentNodeId);
		}

		Save();
	}

	/// <summary>累加时间点进程（移动 / 战斗回合 / 事件代价）并落档；顺带扣食物效果的 TimePoint 寿命轴。</summary>
	public bool TryAddTimePoints(float delta, out string error)
	{
		error = string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		if (!Current.MapState.TryAddTimePoints(delta, out error))
		{
			return false;
		}

		// 时间点变动 → 食物效果的 TimePoint 寿命轴（2026-10-02 口径 ②）
		RunFoodSystem.TickTimePoints(Current, System.Math.Abs(delta));
		Save();
		return true;
	}

	/// <summary>支付时间点代价（移动 / 节点交互 / 事件选项）；不足则整笔拒绝，调用方应转向营地转场且不改动其它状态。</summary>
	public bool TrySpendTimePoints(float cost, out string error)
	{
		error = string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		bool spentTimePoints = Current.MapState.TrySpendTimePoints(cost, out error);
		if (spentTimePoints)
		{
			// 时间点消耗 → 食物效果的 TimePoint 寿命轴（2026-10-02 口径 ②）
			RunFoodSystem.TickTimePoints(Current, System.Math.Abs(cost));
		}

		if (!spentTimePoints)
		{
			return false;
		}

		Save();
		return true;
	}

	/// <summary>进入休息（营地打开时调用）：记下当刻的当天剩余并落档 —— 回复公式与营地预览都取这个值。</summary>
	public void BeginRestDay()
	{
		if (Current == null)
		{
			return;
		}

		Current.MapState.BeginRestDay();
		Current.CookedThisRest = 0; // 新的休息场次：烹饪次数重置（食物系统 §四）
		Save();
	}

	/// <summary>休息结算（营地「休息」按钮）：逐槽回复 + 推进到新一天并落档；返回每名角色的实际回复量。</summary>
	public List<int> ApplyRest(RunWatchMode watchMode, int watcherSlotIndex, int satiety)
	{
		if (Current == null)
		{
			return new List<int>();
		}

		List<int> healed = RunRestResolver.Apply(Current, watchMode, watcherSlotIndex, satiety);
		Save();
		return healed;
	}
	/// <summary>
	/// 休息结算（营地「休息」按钮，食物系统 §二 / §三）：先消耗篝火食物并把**仍生效**的效果写进寿命轴，
	/// 再按真实饱食度回复生命，然后跨天腐坏食物、扣 DayCount 轴、清零本次烹饪次数，最后落档。
	/// </summary>
	public List<int> ApplyRest(RunWatchMode watchMode, int watcherSlotIndex, RunFoodSystem.CampFirePlan plan)
	{
		if (Current == null)
		{
			return new List<int>();
		}

		int satiety = plan?.TotalSatiety ?? 0;
		List<RunFoodEffectSave> gained = RunFoodSystem.ConsumeCampFire(Current, plan);
		List<int> healed = RunRestResolver.Apply(Current, watchMode, watcherSlotIndex, satiety);
		List<RunBagEntrySave> spoiled = RunBagSystem.DecayFoodExpiry(Current);
		RunFoodSystem.TickRest(Current);
		Current.CookedThisRest = 0;
		Save();

		GD.Print($"[营地] 篝火总饱食度 {satiety}（计入效果 {plan?.EffectiveSatiety ?? 0}）→ 食物效果 {gained.Count} 条；腐坏移除 {spoiled.Count} 件。");
		return healed;
	}

	/// <summary>
	/// 烹饪一次（营地「烹饪」按钮，食物系统 §四）：每次休息 ≤ 2 次，只消耗**食物**（材料暂不参与烹饪）。
	/// 配方取自 `LoadingSystem.FoodRecipeDictionary`；成功即落档。`reservedInstanceIds` = 篝火草稿里已规划的食物实例，
	/// 它们不能被烹饪吃掉（否则「同一件食物既在篝火里又被当原料」会让本次休息吃到不存在的东西）。
	/// </summary>
	public bool TryCook(int recipeId, IReadOnlyCollection<string> reservedInstanceIds, out string error)
	{
		error = string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		if (!LoadingSystem.FoodRecipeDictionary.TryGetValue(recipeId, out FoodRecipeDefinition recipe))
		{
			error = $"配方不存在：{recipeId}";
			return false;
		}

		bool ok = RunFoodSystem.TryCook(Current, recipe, out RunBagEntrySave result, out error, reservedInstanceIds);
		if (ok)
		{
			GD.Print($"[营地] 烹饪 {recipe.Description} → {result?.DefinitionId}（本次休息已烹饪 {Current.CookedThisRest}/{RunFoodSystem.MaxCookPerRest}）");
			Save();
		}

		return ok;
	}

	/// <summary>不改动篝火草稿的通用入口（事件 / 脚本 / 单测用）：等价于预留集合为空的 <see cref="TryCook(int, IReadOnlyCollection{string}, out string)"/>。</summary>
	public bool TryCook(int recipeId, out string error) => TryCook(recipeId, null, out error);



	// ─────────────────────────────────────────────────────────────
	// 背包与食物效果（2026-10-02 批 B，P0-17 / P1-19）
	// ─────────────────────────────────────────────────────────────

	/// <summary>背包某类别的条目（材料 / 道具 / 装备 / 食物，背包系统交互案 §二）。</summary>
	public List<RunBagEntrySave> GetBagEntries(BagCategory category)
	{
		if (Current == null)
		{
			return new List<RunBagEntrySave>();
		}

		RunBagSystem.EnsureCollections(Current);
		return RunBagSystem.EntriesOf(Current, category);
	}

	/// <summary>当前背包总负荷（`Σ(单件负荷 × 数量)`；装备表暂无 `Load` 列，装备计 0）。</summary>
	public float CurrentBagLoad => Current == null ? 0f : RunBagSystem.TotalLoad(Current);

	/// <summary>按实例键取背包条目；不存在返回 null。</summary>
	public RunBagEntrySave FindBagEntry(string instanceId) => Current == null ? null : RunBagSystem.Find(Current, instanceId);

	/// <summary>把一个物品放进背包并立刻落档（事件奖励 / 调试补给 / 拾取回流都走这里）。</summary>
	public bool TryAddBagItem(BagCategory category, int definitionKey, int count, out string error)
	{
		error = string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		if (definitionKey <= 0 || count <= 0)
		{
			error = "物品定义或数量非法。";
			return false;
		}

		RunBagSystem.Add(Current, category, definitionKey, count);
		Save();
		return true;
	}

	/// <summary>从背包扣减并落档；数量不足时只扣现有部分并给出原因（调用方按提示展示）。</summary>
	public bool TryRemoveBagItem(string instanceId, int count, out string error)
	{
		error = string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		RunBagEntrySave entry = RunBagSystem.Find(Current, instanceId);
		if (entry == null)
		{
			error = "背包里没有该物品。";
			return false;
		}

		bool enough = RunBagSystem.Remove(Current, instanceId, count);
		if (!enough)
		{
			error = $"数量不足（现有 {entry.Count}，需求 {count}），已扣完现有部分。";
		}

		Save();
		return enough;
	}

	/// <summary>
	/// 局外随身 3 格：放入 / 交换。默认**不**写入战斗开场（背包系统交互案 §九 第 1 条），
	/// 因此这里只落档不联动战斗；打通口径时改在开战场时读 `CarryItemSlots` 即可。
	/// </summary>
	public bool TrySetCarrySlot(int slot, string instanceId, out string error)
	{
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		bool ok = RunBagSystem.TrySetCarrySlot(Current, slot, instanceId, out error);
		if (ok)
		{
			Save();
		}

		return ok;
	}

	/// <summary>清空一个随身格并落档。</summary>
	public bool TryClearCarrySlot(int slot, out string error)
	{
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		bool ok = RunBagSystem.TryClearCarrySlot(Current, slot, out error);
		if (ok)
		{
			Save();
		}

		return ok;
	}

	/// <summary>随身格显示文案（空格 = 「空」）。</summary>
	public string GetCarrySlotText(int slot) => Current == null ? "空" : RunBagSystem.CarrySlotText(Current, slot);

	/// <summary>手位显示文案（空格 = 「空」；左右手 = 0 / 1，见 `RunEquipmentSystem`）。</summary>
	public string GetHandText(int slotIndex, int hand) =>
		Current == null ? RunEquipmentSystem.EmptyHandText : RunEquipmentSystem.HandText(Current, slotIndex, hand);

	// ─────────────────────────────────────────────────────────────
	// 背包整理（2026-10-02 批 E，P0-17 界面半 / P0-18 装备链）
	// 拖动落点全部经这里：闸门 → 纯逻辑模块（RunBagSystem / RunEquipmentSystem）→ 立刻落档。
	// ─────────────────────────────────────────────────────────────

	/// <summary>整理被拒的四种情形（文案与判据见 `BagArrangeGate`，界面只读时提示行给出）。</summary>
	public const string BagArrangeBlockRest = BagArrangeGate.Rest;
	public const string BagArrangeBlockBattle = BagArrangeGate.Battle;
	public const string BagArrangeBlockEvent = BagArrangeGate.Event;
	public const string BagArrangeBlockSettlement = BagArrangeGate.Settlement;

	/// <summary>队伍负荷上限（背包系统交互案 §四；来自 InventoryConfig.csv 的 `Global` 行）。</summary>
	public float BagLoadLimit => RunBagSystem.LoadLimit;

	/// <summary>当前背包负荷（只算背包内物品：手位与随身格不计，背包系统交互案 §四）。</summary>
	public float BagLoad => CurrentBagLoad;

	/// <summary>是否已超载（奖励入账不受限，只是阻止新的拖入）。</summary>
	public bool IsBagOverloaded => Current != null && RunBagSystem.IsOverloaded(Current);

	/// <summary>
	/// 背包整理（拖动）的**运行时**阻断原因（空串 = 可拖动）。**不入档**：由 `RunFlowScene` 在每次
	/// 内容 / 界面状态变化时按 <see cref="DescribeBagArrangeBlock"/> 重算 —— 存档里不该有界面状态。
	/// </summary>
	public string BagArrangeBlockReason { get; set; } = string.Empty;

	/// <summary>
	/// **调试通道**的强制放行开关（运行时、不入档，只由 `DebugApiRun` 的 `debug.run.force_bag_gate` 写）：
	/// 为真时宿主每帧重算闸门也必须算成「可整理」—— 让「内容进行中」也能验证拖动规则本身（不必打完一场）。
	/// </summary>
	public bool BagArrangeOverride { get; set; }

	/// <summary>能不能拖动整理（背包界面的唯一判据）。</summary>
	public bool CanArrangeBag => Current != null && string.IsNullOrEmpty(BagArrangeBlockReason);

	/// <summary>
	/// 「无内容进行中」判据（背包系统交互案 §三）：规则与文案住在纯逻辑模块 `BagArrangeGate`，
	/// 这里只做转发（`RunSession` 是 Godot `Node`，单测不能直接引用它）。
	/// </summary>
	public static string DescribeBagArrangeBlock(bool campActive, bool settlementPanelOpen,
		bool battleContentActive, bool eventContentActive) =>
		BagArrangeGate.Describe(campActive, settlementPanelOpen, battleContentActive, eventContentActive);

	/// <summary>闸门：没有本局 / 有阻断原因时给出原因并拒绝（所有整理入口的第一步）。</summary>
	private bool RequireBagArrange(out string error)
	{
		error = BagArrangeBlockReason ?? string.Empty;
		if (Current == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		return error.Length == 0;
	}

	/// <summary>
	/// 背包 → 随身道具格（背包系统交互案 §三）：该格已有条目则**交换**（原物回背包）。
	/// 放进随身格的条目不再占背包负荷（§四）。成功立刻落档。
	/// </summary>
	public bool TryMoveBagEntryToCarrySlot(string instanceId, int slot, out string error)
	{
		error = string.Empty;
		if (!RequireBagArrange(out error))
		{
			return false;
		}

		if (!RunBagSystem.TrySetCarrySlot(Current, slot, instanceId, out error))
		{
			return false;
		}

		Save();
		return true;
	}

	/// <summary>随身格 → 背包（收入背包、局外不落地）：进包后超限则整笔拒绝并给出具体数字（§四）。</summary>
	public bool TryMoveCarrySlotToBag(int slot, out string error)
	{
		error = string.Empty;
		if (!RequireBagArrange(out error))
		{
			return false;
		}

		RunBagEntrySave entry = RunBagSystem.CarrySlotEntry(Current, slot);
		if (entry == null)
		{
			error = "该随身格是空的。";
			return false;
		}

		float load = ItemNameResolver.LoadOf(entry.CategoryEnum, entry.DefinitionKey) * entry.Count;
		if (RunBagSystem.WouldExceedLoad(Current, load))
		{
			error = RunBagSystem.DescribeLoadReject(Current, load);
			return false;
		}

		if (!RunBagSystem.TryClearCarrySlot(Current, slot, out error))
		{
			return false;
		}

		Save();
		return true;
	}

	/// <summary>
	/// 背包 → 手位（左手 = `RunEquipmentSystem.LeftHand` / 右手 = `RightHand`）：
	/// 只接装备；双手装备要求两槽同时让位；被换下的单手装备回背包。成功立刻落档。
	/// </summary>
	public bool TryEquipBagEntryToHand(string instanceId, int slotIndex, int hand, out string error)
	{
		error = string.Empty;
		if (!RequireBagArrange(out error))
		{
			return false;
		}

		if (!RunEquipmentSystem.TryEquipFromBag(Current, instanceId, slotIndex, hand, out error))
		{
			return false;
		}

		Save();
		return true;
	}

	/// <summary>手位 → 背包（卸下、局外不落地丢弃）；双手装备一次卸下两槽。成功立刻落档。</summary>
	public bool TryUnequipHandToBag(int slotIndex, int hand, out string error)
	{
		error = string.Empty;
		if (!RequireBagArrange(out error))
		{
			return false;
		}

		if (!RunEquipmentSystem.TryUnequipHand(Current, slotIndex, hand, out error))
		{
			return false;
		}

		Save();
		return true;
	}

	/// <summary>手位 → 另一手位（交换；双手装备不参与互换）。成功立刻落档。</summary>
	public bool TryMoveHandToHand(int slotIndex, int fromHand, int toHand, out string error)
	{
		error = string.Empty;
		if (!RequireBagArrange(out error))
		{
			return false;
		}

		if (!RunEquipmentSystem.TryMoveHandToHand(Current, slotIndex, fromHand, toHand, out error))
		{
			return false;
		}

		Save();
		return true;
	}

	/// <summary>
	/// 手位 → 随身道具格：**默认拒绝**（许可通道未开放）；`AllowsWeaponInItemSlots` 为真时才合法。
	/// 失败也走闸门 → 原因文案由 `RunEquipmentSystem` 给出（界面只做展示）。
	/// </summary>
	public bool TryMoveHandToCarrySlot(int slotIndex, int hand, int carrySlot, out string error)
	{
		error = string.Empty;
		if (!RequireBagArrange(out error))
		{
			return false;
		}

		if (!RunEquipmentSystem.TryMoveHandToCarrySlot(Current, slotIndex, hand, carrySlot, out error))
		{
			return false;
		}

		Save();
		return true;
	}


	// ── 部位格（装备系统交互案 §二 / §三；2026-10-02 装备界面批，SchemaVersion 6）──
	// 拖动落点同背包：闸门 → `RunEquipmentSystem`（规则本体）→ 成功立刻落档。

	/// <summary>饰品格数（配置读数口；装备界面按它铺格）。</summary>
	public int AccessorySlotCount => RunEquipmentSystem.AccessorySlotCount;

	/// <summary>部位格显示文案（空位 = 「空」；`slotKind` = `EquipmentSlotKind`）。</summary>
	public string GetBodySlotText(int slotIndex, int slotKind, int indexInKind) =>
		Current == null ? RunEquipmentSystem.EmptyHandText : RunEquipmentSystem.BodySlotText(Current, slotIndex, slotKind, indexInKind);

	/// <summary>
	/// 背包 → 部位格（案 §三）：部位不符 / 饰品栏满各给对应原因；替换下来的装备回背包。
	/// 成功立刻落档。落点受限（内容进行中）时由闸门拒绝并给原因。
	/// </summary>
	public bool TryEquipBagEntryToSlot(string instanceId, int slotIndex, int slotKind, int indexInKind, out string error)
	{
		error = string.Empty;
		if (!RequireBagArrange(out error))
		{
			return false;
		}

		if (!RunEquipmentSystem.TryEquipSlotFromBag(Current, instanceId, slotIndex, slotKind, indexInKind, out error))
		{
			return false;
		}

		Save();
		return true;
	}

	/// <summary>部位格 → 背包（卸下、局外不落地丢弃；进包超限则整笔拒绝）。成功立刻落档。</summary>
	public bool TryUnequipSlotToBag(int slotIndex, int slotKind, int indexInKind, out string error)
	{
		error = string.Empty;
		if (!RequireBagArrange(out error))
		{
			return false;
		}

		if (!RunEquipmentSystem.TryUnequipSlot(Current, slotIndex, slotKind, indexInKind, out error))
		{
			return false;
		}

		Save();
		return true;
	}

	/// <summary>部位格 ↔ 部位格（仅饰品之间可互换）。成功立刻落档。</summary>
	public bool TrySwapBodySlots(int slotIndex, int fromKind, int fromIndex, int toKind, int toIndex, out string error)
	{
		error = string.Empty;
		if (!RequireBagArrange(out error))
		{
			return false;
		}

		if (!RunEquipmentSystem.TrySwapBodySlots(Current, slotIndex, fromKind, fromIndex, toKind, toIndex, out error))
		{
			return false;
		}

		Save();
		return true;
	}


	// ── P0#9 存档状态机（OnMap / InBattleStart / InSettlement） ──

	public bool IsInBattleStart => Current != null && string.Equals(Current.GameMode, RunGameModes.InBattleStart, StringComparison.Ordinal);
	public bool IsInSettlement => Current != null && string.Equals(Current.GameMode, RunGameModes.InSettlement, StringComparison.Ordinal);
	public bool IsOnMap => Current != null && string.Equals(Current.GameMode, RunGameModes.OnMap, StringComparison.Ordinal);

	/// <summary>进入战斗：把遭遇持久化并置 InBattleStart（档内角色 = 战前状态，重进=重新开局）。</summary>
	public void BeginRunBattleEncounter(string layer, StageEncounterRow row)
	{
		BeginPendingEncounter(layer, row);
		if (Current == null || row == null)
		{
			return;
		}

		Current.GameMode = RunGameModes.InBattleStart;
		Current.PendingEncounterLayer = layer ?? string.Empty;
		Current.PendingEncounterNodeType = (int)row.NodeType;
		Current.PendingEncounterName = row.Name ?? string.Empty;
		Current.PendingDropTableId = row.DropTableId;
		Current.PendingLevelId = row.LevelId ?? string.Empty;
		Current.PendingMonsterIds = new List<int>(row.MonsterIds ?? Array.Empty<int>());
		Current.PendingContentType = "Level";
		Current.PendingContentId = Current.PendingLevelId;
		Save();
	}

	/// <summary>进入事件节点：置内容为 Event 并落档；`sourceNodeType` = 来源节点类型（商人 / 普通事件 / 危险事件），供非战斗来源发卡的结算来源与放弃日志使用（§5.7）。
	/// </summary>
	public void BeginRunEvent(string eventId, int sourceNodeId, MapNodeType sourceNodeType = MapNodeType.Empty)
	{
		if (Current == null || string.IsNullOrWhiteSpace(eventId)) return;
		Current.GameMode = RunGameModes.InBattleStart;
		Current.PendingContentType = "Event";
		Current.PendingContentId = eventId;
		Current.PendingSourceNodeId = sourceNodeId;
		Current.PendingSourceNodeType = (int)sourceNodeType;
		Save();
	}

	public void CompletePendingEventToMap()
	{
		if (Current == null) return;
		MarkCurrentNodeVisitedAndAdvanceEncounter();
		Current.GameMode = RunGameModes.OnMap;
		Current.PendingContentType = string.Empty; Current.PendingContentId = string.Empty; Current.PendingSourceNodeId = -1;
		Current.PendingSourceNodeType = 0;
		Save();
	}

	/// <summary>从存档字段重建遭遇行（重进战斗/结算时使用）。</summary>
	public StageEncounterRow BuildPendingEncounterRowFromSave()
	{
		if (Current == null || Current.PendingEncounterNodeType <= 0)
		{
			return null;
		}

		return new StageEncounterRow
		{
			Layer = Current.PendingEncounterLayer ?? string.Empty,
			NodeType = (MapNodeType)Current.PendingEncounterNodeType,
			Name = Current.PendingEncounterName ?? string.Empty,
			Difficulty = StageDifficulty.Any,
			DropTableId = Current.PendingDropTableId,
			LevelId = Current.PendingLevelId ?? string.Empty,
			MonsterIds = Current.PendingMonsterIds?.ToArray() ?? Array.Empty<int>(),
		};
	}

	/// <summary>战斗胜利、结算弹出前调用：落盘“胜利未领奖”存档，保证重进重现同款结算。</summary>
	public void EnterSettlement(SettlementStartRequest request)
	{
		if (Current == null)
		{
			return;
		}

		Current.GameMode = RunGameModes.InSettlement;
		SettlementStartRequest start = request ?? new SettlementStartRequest();
		Current.SettlementEncounterName = start.SourceName ?? string.Empty;
		Current.SettlementSourceNodeType = (int)start.SourceNodeType;
		Current.SettlementDropTableId = start.DropTableId;
		Current.SettlementCardPools = start.CardPools == null
			? new List<SettlementCardPoolSave>()
			: new List<SettlementCardPoolSave>(start.CardPools);
		Current.SettlementCandidateCardIds = new List<int>(); // 旧字段不再写入：只在读旧档时按「1 份」还原
		Current.SettlementCardClaims = new List<SettlementCardClaimSave>();
		Current.SettlementClaimedRewardKeys = new List<string>();
		Current.SettlementPanelClosed = false;
		Current.SettlementLossTier = start.LossTier;
		Current.SettlementValueRatio = start.ValueRatio;
		// 新一次结算 = 新一张战后战场：旧快照立即作废（事件 / 商人发卡结算不会有新快照）。
		Current.PostBattleBattlefield = null;
		Save();
	}

	/// <summary>领取奖励完成、即将回地图时调用：清结算/待战状态并落盘（OnMap）。</summary>
	public void CompleteSettlementToMap()
	{
		if (Current == null)
		{
			return;
		}

		Current.GameMode = RunGameModes.OnMap;
		Current.SettlementEncounterName = string.Empty;
		Current.SettlementDropTableId = 0;
		Current.SettlementCandidateCardIds.Clear();
		Current.SettlementCardPools.Clear();
		Current.SettlementCardClaims.Clear();
		Current.SettlementPanelClosed = false;
		Current.SettlementSourceNodeType = 0;
		Current.SettlementLossTier = 0;
		Current.SettlementValueRatio = 1.0;
		Current.SettlementClaimedRewardKeys.Clear();
		Current.PendingEncounterLayer = string.Empty;
		Current.PendingEncounterNodeType = 0;
		Current.PendingEncounterName = string.Empty;
		Current.PendingDropTableId = 0;
		Current.PendingLevelId = string.Empty;
		Current.PendingMonsterIds.Clear();
		Current.PostBattleBattlefield = null; // 结算完成 = 战后战场不再需要：位置落档的使命结束
		ClearPendingEncounter();
		Save();
	}

	// ── 结算未领取项：领取 / 关闭 / 放弃闸门（交互案 §四 §五 §6 §7） ──

	/// <summary>物品 Tab 领取：立即入账 + 去重键落档；已领过返回 false（不重复发放）。</summary>
	public bool TryClaimSettlementReward(string claimKey, DropTableEntry entry)
	{
		if (Current == null || entry == null || string.IsNullOrEmpty(claimKey))
		{
			return false;
		}

		if (Current.SettlementClaimedRewardKeys.Contains(claimKey))
		{
			return false;
		}

		BattleRewardPresenter.ApplyRewardEntryToRun(entry, Current);
		Current.SettlementClaimedRewardKeys.Add(claimKey);
		Save();
		return true;
	}

	/// <summary>卡牌份领取：该份只能领一次、只能领一张；入组槽位取「份自带槽位」（旧档没有槽位时按卡池反查兜底）。</summary>
	public bool TryClaimSettlementCard(int slotIndex, int cardId, out int ownerSlot)
	{
		ownerSlot = slotIndex;
		if (Current == null || cardId <= 0 || SettlementRewardPresenter.FindCardClaim(Current, slotIndex) != null)
		{
			return false;
		}

		bool hasPool = false;
		foreach (SettlementCardPoolSave pool in Current.SettlementCardPools)
		{
			if (pool != null && pool.SlotIndex == slotIndex)
			{
				hasPool = true;
				break;
			}
		}

		if (!hasPool)
		{
			return false;
		}

		if (ownerSlot < 0 || ownerSlot >= Current.CharacterSlots.Count)
		{
			ownerSlot = BattleRewardPresenter.FindOwningSlotIndex(Current, cardId);
			if (ownerSlot < 0)
			{
				ownerSlot = 0;
			}
		}

		AddCardToSlotDeck(ownerSlot, cardId, 0);
		Current.SettlementCardClaims.Add(new SettlementCardClaimSave { SlotIndex = slotIndex, CardId = cardId });
		Save();
		return true;
	}

	/// <summary>
	/// 战后布局落档（交互案 §七 4 改口径「位置落档」）：进入战后操作态时落一次，之后每次战后移动 / 拾取再落一次；
	/// 读档重进结算界面时按它重建同一张战场（`RunBattleScene` 的 `InSettlement` 分支）。
	/// </summary>
	public void SavePostBattleBattlefield(RunPostBattleSave snapshot)
	{
		if (Current == null || snapshot == null)
		{
			return;
		}

		Current.PostBattleBattlefield = snapshot;
		Save();
	}

	/// <summary>关闭 / 重新打开结算面板的落档标记（读档重进：true → 只显示待领取浮窗，不自动弹面板）。</summary>
	public void SetSettlementPanelClosed(bool closed)
	{
		if (Current == null)
		{
			return;
		}

		Current.SettlementPanelClosed = closed;
		Save();
	}

	/// <summary>放弃确认弹窗的「本局游戏内不再显示」勾选落档（本局有效；新局 / 放弃本局后重置）。</summary>
	public void SetSuppressAbandonSettlementConfirm(bool suppress)
	{
		if (Current == null)
		{
			return;
		}

		Current.SuppressAbandonSettlementConfirm = suppress;
		Save();
	}

	/// <summary>队伍槽位的角色显示名（交互案 §3.1 的唯一取名口）：同名按出现次序编号（重剑手 / 重剑手2）。</summary>
	public string GetSlotDisplayName(int slotIndex)
	{
		if (Current == null || slotIndex < 0 || slotIndex >= Current.CharacterSlots.Count)
		{
			return "角色 ?";
		}

		List<int> characterIds = new List<int>();
		foreach (RunCharacterSlotSave slot in Current.CharacterSlots)
		{
			characterIds.Add(slot.CharacterId);
		}

		return CharacterSlotNaming.GetDisplayName(characterIds, slotIndex, ResolveCharacterName);
	}

	/// <summary>槽位角色 Id → `Character.csv` 的 `Name`（统一取值见 LoadingSystem.GetCharacterName）；
	/// 找不到返回空串，由 CharacterSlotNaming 兜底为「角色 {id}」。</summary>
	private static string ResolveCharacterName(int characterId)
	{
		return LoadingSystem.GetCharacterName(characterId);
	}

	/// <summary>旧档兼容（交互案 §八）：只有单一 `SettlementCandidateCardIds` 的旧档按「1 份」还原到 `SettlementCardPools`。</summary>
	private static void MigrateSettlementCompat(RunSaveData data)
	{
		if (data == null)
		{
			return;
		}

		if (data.SettlementCardPools == null)
		{
			data.SettlementCardPools = new List<SettlementCardPoolSave>();
		}

		if (data.SettlementCardClaims == null)
		{
			data.SettlementCardClaims = new List<SettlementCardClaimSave>();
		}

		if (data.SettlementCardPools.Count > 0
			|| data.SettlementCandidateCardIds == null
			|| data.SettlementCandidateCardIds.Count == 0)
		{
			return;
		}

		data.SettlementCardPools.Add(new SettlementCardPoolSave
		{
			SlotIndex = SettlementRewardPresenter.LegacySlotIndex,
			CharacterId = 0,
			CandidateCardIds = new List<int>(data.SettlementCandidateCardIds),
		});
		GD.Print($"[RunSession] 旧档结算候选按「1 份」还原：{data.SettlementCandidateCardIds.Count} 张候选。");
	}

	public void BeginPendingEncounter(string layer, StageEncounterRow row)
	{
		PendingEncounterLayer = layer ?? string.Empty;
		PendingEncounter = row;
	}

	public void ClearPendingEncounter()
	{
		PendingEncounterLayer = string.Empty;
		PendingEncounter = null;
	}
}
