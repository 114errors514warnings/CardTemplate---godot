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
				NormalEncounterIndex = 0,
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
		Current.PendingMonsterIds = new List<int>(row.MonsterIds ?? Array.Empty<int>());
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
			MonsterIds = Current.PendingMonsterIds?.ToArray() ?? Array.Empty<int>(),
		};
	}

	/// <summary>战斗胜利、结算弹出前调用：落盘“胜利未领奖”存档，保证重进重现同款结算。</summary>
	public void EnterSettlement(string encounterName, int dropTableId, IReadOnlyList<int> candidateCardIds)
	{
		if (Current == null)
		{
			return;
		}

		Current.GameMode = RunGameModes.InSettlement;
		Current.SettlementEncounterName = encounterName ?? string.Empty;
		Current.SettlementDropTableId = dropTableId;
		Current.SettlementCandidateCardIds = candidateCardIds == null ? new List<int>() : new List<int>(candidateCardIds);
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
		Current.PendingEncounterLayer = string.Empty;
		Current.PendingEncounterNodeType = 0;
		Current.PendingEncounterName = string.Empty;
		Current.PendingDropTableId = 0;
		Current.PendingMonsterIds.Clear();
		ClearPendingEncounter();
		Save();
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
