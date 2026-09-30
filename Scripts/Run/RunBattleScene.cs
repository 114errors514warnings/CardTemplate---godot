// RunBattleScene.cs
// 正式运行局的六边形战斗宿主：注入角色、永久卡组、装备状态与遭遇怪物。
// 胜利后按「综合价值点数」口径算奖励折损、落盘 InSettlement，并通知 RunFlowScene 弹出结算面板
// （结算界面本体见 SettlementUi；本文件只负责战斗、折损与落档）。
using Godot;
using System;
using System.Collections.Generic;
using CardSimulator.Battlefield;

public partial class RunBattleScene : Control
{
	public event Action ContentFinished;

	/// <summary>结算已落档（InSettlement），宿主应显示结算界面；参数 = 本场追回的被窃金币（未追回为 0）。</summary>
	public event Action<int> SettlementReady;

	/// <summary>
	/// 本内容是否持有**实机战场**（这一场真的打过，而不是读档重进直接复现结算界面）。
	/// 关闭结算面板后的落点据此分流（新案 §三）：true → 停留战场（不自动打开世界地图）；
	/// false → 没有可操作的战场，仍以「已打开 + 可选」的世界地图覆盖。
	/// </summary>
	public bool HasLiveBattlefield => battleView != null && GodotObject.IsInstanceValid(battleView) && battleView.MapView != null;

	/// <summary>本内容的战场视图（烟测断言 / 宿主接管表现用）；没有实机战场时为 null。</summary>
	public HexBattleScene BattleView => battleView;

	public const string MapScenePath = "res://Scenes/Map/MapScene.tscn";
	public const string MainMenuScenePath = "res://Scenes/MainMenu/MainMenuScene.tscn";

	private HexBattleScene battleView;
	private BattlefieldSession battlefield;
	private bool outcomeResolved;
	private bool resultShown;
	private bool resultWasVictory;
	private int refundedStolenGold;

	/// <summary>失败面板层（ModalLayer）；胜利结算面板由 RunFlowScene 的 SettlementUi 常驻持有。</summary>
	private CanvasLayer resultLayer;

	public override void _Ready()
	{
		// 层号只认 RunUiLayers：失败面板与结算面板同属模态层（40），高于世界地图（30）、低于全局按钮（50）。
		resultLayer = new CanvasLayer { Layer = RunUiLayers.Modal };
		AddChild(resultLayer);

		RunSession session = RunSession.Instance;
		if (session == null || session.Current == null)
		{
			GD.PrintErr("[RunBattle] 缺少本局数据，回到主菜单。");
			CallDeferred(nameof(GoToMainMenuAbort));
			return;
		}

		// InSettlement：重进不再开新战斗。有战后布局落档（位置落档，交互案 §七 4 改口径）时先按快照重建战场，
		// 让「关掉面板 → 走几步 / 捡东西 → 退出再继续」回到同一张战场；没有快照（事件 / 商人发卡、旧档）
		// 就只把「胜利未领奖」交给宿主复现（弹面板还是只显示浮窗由 `SettlementPanelClosed` 决定，见 SettlementUi.RefreshFromSave）。
		if (session.IsInSettlement)
		{
			TryRestorePostBattleBattlefield(session);
			SettlementReady?.Invoke(0);
			return;
		}

		// InBattleStart 读档重进：从存档字段重建遭遇，重新开局
		if (session.PendingEncounter == null)
		{
			session.PendingEncounter = session.BuildPendingEncounterRowFromSave();
		}

		if (session.PendingEncounter == null)
		{
			GD.PrintErr("[RunBattle] 未指定遭遇，回到主菜单。");
			CallDeferred(nameof(GoToMainMenuAbort));
			return;
		}

		// 正式运行局复用六边形战场表现；角色、卡组和怪物由 RunSession 注入。
		CreateBattleView();
	}

	/// <summary>正式运行局复用六边形战场表现；角色、卡组和怪物由 RunSession 注入。返回 false = 场景不可用（已转主菜单）。</summary>
	private bool CreateBattleView()
	{
		PackedScene battleScene = GD.Load<PackedScene>("res://Scenes/Battle/HexBattleScene.tscn");
		if (battleScene == null)
		{
			GD.PrintErr("[RunBattle] 无法加载 HexBattleScene.tscn。");
			CallDeferred(nameof(GoToMainMenuAbort));
			return false;
		}

		battleView = battleScene.Instantiate<HexBattleScene>();
		battleView.UseRunSession = true;
		battleView.ShowBuiltInResult = false;
		battleView.EnableCommandApi = false;
		battleView.EnableDebugPanel = true;
		battleView.BattleReady += readySession => battlefield = readySession;
		battleView.BattleFinished += OnHexBattleFinished;
		battleView.PostBattleStateChanged += OnPostBattleStateChanged;
		AddChild(battleView); // AddChild 同步跑 HexBattleScene._Ready：BattleReady 已在上一行的事件处回填 battlefield
		return true;
	}

	/// <summary>
	/// 战后布局落档（交互案 §七 4 改口径「位置落档」）：进入战后操作态、以及每次战后移动 / 拾取后，
	/// 把位置、存活 / 生命、地面物件与随身 / 手位写进本局存档；读档重进结算界面时按它重建战场。
	/// </summary>
	private void OnPostBattleStateChanged()
	{
		RunSession session = RunSession.Instance;
		if (session == null || session.Current == null || battlefield == null || battleView == null) return;
		if (!battleView.IsPostSettlementMode) return;
		session.SavePostBattleBattlefield(battlefield.ExportPostBattleState(session.Current.PendingLevelId));
	}

	/// <summary>
	/// 读档重进结算界面：有本场战斗的战后布局落档就重建战场并还原布局（宿主随即停留战场、进入战后操作态）；
	/// 还原失败（快照与待处理内容对不上、地图不一致、角色数量不符）或场景不可用时销毁重建的战场，
	/// 退回「只复现结算界面」——关闭面板后打开可选地图。
	/// </summary>
	private void TryRestorePostBattleBattlefield(RunSession session)
	{
		if (!CanRebuildPostBattleBattlefield(session)) return;
		if (!CreateBattleView()) return;

		if (battleView.ApplyPostBattleSnapshot(session.Current.PostBattleBattlefield, out string error))
		{
			battleView.SetPostSettlementMode(true);
			GD.Print($"[RunBattle] 战后战场按落档重建：单位 {session.Current.PostBattleBattlefield.Units.Count}、地面物件 {session.Current.PostBattleBattlefield.GroundObjects.Count}。");
			return;
		}

		GD.PrintErr("[RunBattle] 战后战场还原失败，退回「只复现结算界面」：" + error);
		// 快照已被证明不可用（与本次待处理内容对不上）：清掉它，避免每次读档都重复失败与刷日志。
		session.Current.PostBattleBattlefield = null;
		session.Save();
		battleView.QueueFree();
		battleView = null;
		battlefield = null;
	}

	/// <summary>能否按落档重建战后战场：必须是同一关卡的战斗结算（事件 / 商人发卡、旧档没有快照）。</summary>
	private static bool CanRebuildPostBattleBattlefield(RunSession session)
	{
		RunSaveData data = session.Current;
		RunPostBattleSave snapshot = data.PostBattleBattlefield;
		return snapshot != null
			&& string.Equals(data.PendingContentType, "Level", StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(data.PendingLevelId)
			&& string.Equals(snapshot.LevelId, data.PendingLevelId, StringComparison.Ordinal);
	}

	private void OnHexBattleFinished(BattlefieldSession.BattlePhase outcome)
	{
		if (resultShown) return;
		outcomeResolved = true;
		resultShown = true;
		resultWasVictory = outcome == BattlefieldSession.BattlePhase.Victory;
		if (resultWasVictory) ShowVictoryResult();
		else ShowDefeatResult();
	}

	/// <summary>
	/// 胜利：回写角色 → 追回被窃金币 → 按击败比例算卡牌份（含折损）→ 落档 InSettlement → 通知宿主弹结算面板。
	/// 候选与折损结果都先落盘：重进不再重抽、折损行沿用落档值（§6.4）。
	/// </summary>
	private void ShowVictoryResult()
	{
		RunSession session = RunSession.Instance;
		if (session == null || session.Current == null)
		{
			return;
		}

		// 战后把活体角色全量回写：HP + 整副默认卡组（含战斗中永久升级级数与顺序）
		WriteBackLiveCharacters(session);

		// 结算被窃金币：只返还被击杀怪物偷走的部分，其余清账（未被击杀的不返还）。
		refundedStolenGold = RefundStolenGold(session);

		StageEncounterRow row = session.PendingEncounter;
		int dropTableId = row != null ? row.DropTableId : session.Current.PendingDropTableId;
		string name = row != null && !string.IsNullOrEmpty(row.Name) ? row.Name : session.Current.PendingEncounterName;
		if (string.IsNullOrEmpty(name))
		{
			name = "胜利";
		}

		MapNodeType nodeType = row != null ? row.NodeType : (MapNodeType)session.Current.PendingEncounterNodeType;
		DropTableEntry cardRow = FindCardRewardRow(dropTableId);

		// 折损（P2-10.3）：只有本场真的会发卡时才折损；非战斗来源 / 无卡牌奖励不折损（§5.7）；
		// 生存关按回合数结算、**通关总是全额**（玩法 §7.4），因此直接按「无折损」落档（比例 1.0 → 档位 0）。
		bool survival = MonsterValuePoints.IsSurvivalLevelType(battleView == null ? string.Empty : battleView.LevelType);
		double valueRatio = cardRow == null || survival ? 1.0 : ComputeDefeatValueRatio();
		int rewardCount = MonsterValuePoints.GetCardRewardCount(valueRatio);
		List<SettlementCardPoolSave> pools = cardRow == null
			? new List<SettlementCardPoolSave>()
			: BuildCardPools(session.Current, cardRow, rewardCount);

		session.EnterSettlement(new SettlementStartRequest
		{
			SourceName = name,
			SourceNodeType = nodeType,
			DropTableId = dropTableId,
			CardPools = pools,
			LossTier = MonsterValuePoints.GetLossTier(valueRatio),
			ValueRatio = valueRatio,
		});

		SettlementReady?.Invoke(refundedStolenGold);
	}

	/// <summary>本场掉落表里的 Card 行：多行 Card 只作为「本次卡牌奖励」的整体配置，不额外增加卡牌份（§三）。</summary>
	private static DropTableEntry FindCardRewardRow(int dropTableId)
	{
		foreach (DropTableEntry entry in BattleRewardPresenter.GetEntriesForTable(LoadingSystem.DropTableEntries, dropTableId))
		{
			if (entry != null && entry.Category == DropCategory.Card)
			{
				return entry;
			}
		}

		return null;
	}

	/// <summary>每份的宿主槽位 = 卡牌行角色过滤命中的槽位（`RewardParam = 0` → 全队每个槽位各一份）。</summary>
	private static List<SettlementCardSlot> BuildCardSlots(RunSaveData run, DropTableEntry cardRow)
	{
		List<SettlementCardSlot> slots = new List<SettlementCardSlot>();
		if (run == null || cardRow == null)
		{
			return slots;
		}

		List<int> characterIds = BattleRewardPresenter.ResolveCardRewardCharacterIds(cardRow, run);
		for (int slotIndex = 0; slotIndex < run.CharacterSlots.Count; slotIndex++)
		{
			int characterId = run.CharacterSlots[slotIndex].CharacterId;
			if (characterIds.Contains(characterId))
			{
				slots.Add(new SettlementCardSlot { SlotIndex = slotIndex, CharacterId = characterId });
			}
		}

		return slots;
	}

	/// <summary>按份数生成候选：每份**只取该份角色自己的卡池**（§5.3），候选常态 3 张、不随折损变化。</summary>
	private static List<SettlementCardPoolSave> BuildCardPools(RunSaveData run, DropTableEntry cardRow, int rewardCount)
	{
		return SettlementRewardPresenter.BuildCardPools(
			BuildCardSlots(run, cardRow),
			rewardCount,
			characterId => LoadingSystem.GetCharacterRewardCardIds(characterId),
			SettlementRewardPresenter.DefaultCandidateCount,
			BattleSytem.RandomGenerator);
	}

	/// <summary>
	/// 击败比例（P2-10.3）：按战场占位统计本场部署的全部敌方单位与被击败者，用「综合价值点数」口径求比例
	/// （**分母含逃跑 / 存活者**）；**爪牙不计入**（玩法 §7.4 / §7.5）；没有部署数据时返回 1.0（视为不折损）。
	/// 爪牙标记来自 `Monster.csv` 的 `IsMinion` 列（`LoadMonsterCsv` → `Monster.IsMinion`，`MonsterInstance` 继承）；
	/// 当前无怪物置 1，因此实际全部按非爪牙计入。
	/// </summary>
	private double ComputeDefeatValueRatio()
	{
		if (battlefield == null)
		{
			return 1.0;
		}

		List<MonsterValuePoints.EnemyValueSample> samples = new List<MonsterValuePoints.EnemyValueSample>();
		foreach (BattleUnitPlacement placement in battlefield.Occupancy.Placements.Values)
		{
			if (placement == null || placement.Role != BattlefieldRole.Enemy || placement.Unit is not MonsterInstance monster)
			{
				continue;
			}

			samples.Add(new MonsterValuePoints.EnemyValueSample(monster.id, placement.Presence == BattlefieldPresence.Defeated, monster.IsMinion));
		}

		return MonsterValuePoints.GetDefeatRatio(samples, MonsterValuePoints.GetValuePointsForMonster);
	}

	/// <summary>胜利结算：把「被击杀怪物实例」偷走的金币原额返还（存活 / 逃跑实例的不返还），并清掉剩余账本。</summary>
	private int RefundStolenGold(RunSession session)
	{
		if (session?.Current == null) return 0;
		var defeatedInstanceKeys = new List<string>();
		if (battlefield != null)
		{
			foreach (BattleUnitPlacement placement in battlefield.Occupancy.Placements.Values)
			{
				if (placement.Role != BattlefieldRole.Enemy || placement.Presence != BattlefieldPresence.Defeated) continue;
				string instanceKey = battlefield.GetMonsterInstanceKey(placement.UnitId);
				if (!string.IsNullOrWhiteSpace(instanceKey)) defeatedInstanceKeys.Add(instanceKey);
			}
		}

		int refunded = RunGoldLedger.RefundDefeated(session.Current, defeatedInstanceKeys);
		RunGoldLedger.Clear(session.Current);
		return refunded;
	}

	/// <summary>把战后角色 HP 与 DefaultDeck（含每张永久升级级数）回写进存档。</summary>
	private void WriteBackLiveCharacters(RunSession session)
	{
		if (battlefield == null)
		{
			return;
		}

		for (int i = 0; i < battlefield.PlayerIds.Count && i < session.Current.CharacterSlots.Count; i++)
		{
			if (!battlefield.Occupancy.Placements.TryGetValue(battlefield.PlayerIds[i], out BattleUnitPlacement placement)
				|| placement.Unit is not CharacterInstance player)
			{
				continue;
			}

			session.Current.CharacterSlots[i].CurrentHp = Math.Max(0, player.HP);
			BattlefieldSession.PlayerLoadout loadout = battlefield.GetLoadout(placement.UnitId);
			session.Current.CharacterSlots[i].EquippedWeaponDefinitionId = loadout?.LeftHand?.DefinitionId ?? string.Empty;

			List<RunDeckEntry> deckSnapshot = new List<RunDeckEntry>();
			foreach (Card card in player.DefaultDeck)
			{
				if (card != null)
				{
					deckSnapshot.Add(new RunDeckEntry
					{
						CardId = card.CardId,
						PermanentUpgradeLevel = card.PermanentUpgradeLevel,
					});
				}
			}

			session.Current.DeckSlots[i] = deckSnapshot;
		}
	}

	/// <summary>
	/// 结算完成（全部领取后关闭 / 确认放弃）后由宿主调用：推进节点 → 清结算态 → 回地图（§7.3）。
	/// 已领取的物品与卡牌保留（领取当刻已入账 / 已入组并落档）；未领取的卡牌不补偿。
	/// </summary>
	public void FinishSettlementToMap()
	{
		RunSession session = RunSession.Instance;
		if (session == null || session.Current == null)
		{
			GoToMainMenuAbort();
			return;
		}

		// 结算界面已被消费：隐藏失败 / 结算层，让世界地图覆盖层能盖住已完成的战斗（结算层 40 > 地图层 30）。
		if (resultLayer != null)
		{
			resultLayer.Visible = false;
		}

		StageEncounterRow row = session.PendingEncounter ?? session.BuildPendingEncounterRowFromSave();
		if (row != null && row.NodeType == MapNodeType.NormalCombat)
		{
			session.Current.MapState.IncrementCurrentNormalEncounterCount();
		}

		session.MarkCurrentNodeVisitedAndAdvanceEncounter();
		session.CompleteSettlementToMap();
		if (ContentFinished != null) { ContentFinished.Invoke(); return; }
		GetTree().ChangeSceneToFile(MapScenePath);
	}

	/// <summary>失败面板（交互案 §二）：无奖励、无浮窗，只提供「返回主菜单」。</summary>
	private void ShowDefeatResult()
	{
		Control overlay = new Control { Name = "DefeatOverlay", MouseFilter = MouseFilterEnum.Stop };
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		resultLayer.AddChild(overlay);

		ColorRect dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
		dim.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(dim);

		CenterContainer center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(440, 220) };
		center.AddChild(panel);

		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		panel.AddChild(margin);

		VBoxContainer vbox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		vbox.AddThemeConstantOverride("separation", 18);
		margin.AddChild(vbox);

		Label title = new Label { Text = "失败", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 34);
		title.AddThemeColorOverride("font_color", Colors.IndianRed);
		vbox.AddChild(title);

		Button back = new Button { Text = "返回主菜单", CustomMinimumSize = new Vector2(300, 52) };
		back.AddThemeFontSizeOverride("font_size", 22);
		back.Pressed += OnAbortToMainMenu;
		vbox.AddChild(back);
	}

	private void OnAbortToMainMenu()
	{
		if (RunSession.Instance != null)
		{
			RunSession.Instance.AbortRun();
		}

		GetTree().ChangeSceneToFile(MainMenuScenePath);
	}

	private void GoToMainMenuAbort()
	{
		if (RunSession.Instance != null)
		{
			RunSession.Instance.AbortRun();
		}

		GetTree().ChangeSceneToFile(MainMenuScenePath);
	}
}
