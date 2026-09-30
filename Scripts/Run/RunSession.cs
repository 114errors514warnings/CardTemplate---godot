// RunSession.cs
// 单局状态 + 存档读写（autoload 单例，project.godot 注册，跨场景存活）。
using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

public partial class RunSession : Node
{
	public const string SavePath = "user://run_save_v1.json";
	public const string SaveSchemaVersion = "1";

	/// <summary>当前局数据（null = 无进行中的局）。</summary>
	public RunSaveData Current { get; private set; }

	/// <summary>等待进入战斗的遭遇层目录名（第一层…），运行时字段不入档。</summary>
	public string PendingEncounterLayer = string.Empty;

	/// <summary>等待进入战斗的已解析遭遇行（含怪物列表/DropTableId），运行时字段不入档。</summary>
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

	public void StartNewRun(IReadOnlyList<int> characterIds, int? seed = null)
	{
		if (characterIds == null || characterIds.Count == 0)
		{
			GD.PrintErr("[RunSession] 开始新局失败：角色列表为空。");
			return;
		}

		RunSaveData data = new RunSaveData
		{
			SchemaVersion = 1,
			SavedAt = DateTime.Now.ToString("s"),
			Gold = 0,
			Keys = 0,
			MapState = new RunMapStateSave
			{
				Act = 1,
				Seed = seed ?? new Random().Next(),
				LayoutVersion = 1,
				CurrentNodeId = -1,
				TimePoints = 0,
			},
		};

		foreach (int characterId in characterIds)
		{
			if (!LoadingSystem.CharacterDictionary.TryGetValue(characterId, out Character template))
			{
				GD.PrintErr($"[RunSession] 角色 {characterId} 未在缓存中找到，无法开始新局。");
				continue;
			}

			data.CharacterSlots.Add(new RunCharacterSlotSave
			{
				CharacterId = characterId,
				CurrentHp = template.MAX_HP,
				MaxHp = template.MAX_HP,
				EquippedWeaponDefinitionId = GetInitialWeaponDefinition(characterId),
			});

			List<RunDeckEntry> deck = new List<RunDeckEntry>();
			List<int> defaultCardIds = LoadingSystem.GetCharacterDefaultCardIdListByKey(
				characterId, LoadingSystem.CharacterDefaultDeckCsvPathKey, true);
			foreach (int cardId in defaultCardIds)
			{
				deck.Add(new RunDeckEntry { CardId = cardId, PermanentUpgradeLevel = 0 });
			}

			data.DeckSlots.Add(deck);
		}

		if (data.CharacterSlots.Count == 0)
		{
			GD.PrintErr("[RunSession] 开始新局失败：无有效角色。");
			return;
		}

		Current = data;
		Save();
	}

	private static string GetInitialWeaponDefinition(int characterId) => characterId switch
	{
		1002 => "双手剑",
		1003 => "弓箭",
		1004 => "法典",
		_ => string.Empty,
	};

	public bool LoadSave()
	{
		if (!FileAccess.FileExists(SavePath))
		{
			GD.PrintErr("[RunSession] 存档不存在，无法读取。");
			return false;
		}

		using (FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read))
		{
			if (file == null)
			{
				GD.PrintErr("[RunSession] 打开存档失败。");
				return false;
			}

			string json = file.GetAsText();
			try
			{
				RunSaveData data = RunSaveJson.Deserialize(json);
				if (data == null)
				{
					GD.PrintErr("[RunSession] 存档内容解析失败。");
					return false;
				}

				MigrateSettlementCompat(data);
				// P2-11：旧档遗留的全局普通敌袭计数按当前层归入（新档该字段恒为 0，此调用无副作用）。
				data.MapState.MigrateLegacyNormalEncounterCount();
				Current = data;
				GD.Print($"[RunSession] 已读取存档：角色 {Current.CharacterSlots.Count}，当前位置 {Current.MapState.CurrentNodeId}。");
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[RunSession] 存档反序列化异常：{ex.Message}");
				return false;
			}
		}
	}

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

	public static void DeleteSave()
	{
		if (FileAccess.FileExists(SavePath))
		{
			DirAccess.RemoveAbsolute(SavePath);
			GD.Print("[RunSession] 已删除存档。");
		}
	}

	/// <summary>清空内存中的当前局（不删档；弃档流程先调 DeleteSave 再调本方法）。</summary>
	public void ClearCurrent()
	{
		Current = null;
	}

	/// <summary>结束本局：清空内存态并删档（战斗失败等路径使用）。</summary>
	public void AbortRun()
	{
		Current = null;
		DeleteSave();
	}

	public RunCharacterSlotSave GetSlot(int index)
	{
		if (Current == null || index < 0 || index >= Current.CharacterSlots.Count)
		{
			return null;
		}

		return Current.CharacterSlots[index];
	}

	public List<RunDeckEntry> GetSlotDeck(int index)
	{
		if (Current == null || index < 0 || index >= Current.DeckSlots.Count)
		{
			return new List<RunDeckEntry>();
		}

		return Current.DeckSlots[index];
	}

	/// <summary>把一张卡（新奖励 / 升级）追加到指定槽位永久卡组。</summary>
	public void AddCardToSlotDeck(int slotIndex, int cardId, int permanentUpgradeLevel = 0)
	{
		List<RunDeckEntry> deck = GetSlotDeck(slotIndex);
		deck.Add(new RunDeckEntry { CardId = cardId, PermanentUpgradeLevel = permanentUpgradeLevel });
	}

	public void SetCurrentNode(int nodeId)
	{
		if (Current != null)
		{
			Current.MapState.CurrentNodeId = nodeId;
		}
	}

	public void MarkCurrentNodeVisitedAndAdvanceEncounter()
	{
		if (Current == null)
		{
			return;
		}

		int nodeId = Current.MapState.CurrentNodeId;
		if (nodeId >= 0 && !Current.MapState.VisitedNodeIds.Contains(nodeId))
		{
			Current.MapState.VisitedNodeIds.Add(nodeId);
		}

		Save();
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
