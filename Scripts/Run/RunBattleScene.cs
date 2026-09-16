// RunBattleScene.cs
// 正式运行局的六边形战斗宿主：注入角色、永久卡组、装备状态与遭遇怪物，负责结算回写。
using Godot;
using System;
using System.Collections.Generic;
using CardSimulator.Battlefield;

public partial class RunBattleScene : Control
{
	public const string MapScenePath = "res://Scenes/Map/MapScene.tscn";
	public const string MainMenuScenePath = "res://Scenes/MainMenu/MainMenuScene.tscn";

	private HexBattleScene battleView;
	private BattlefieldSession battlefield;
	private bool outcomeResolved;
	private bool resultShown;
	private bool resultWasVictory;

	private CanvasLayer resultLayer;
	private int chosenCardId;
	private bool hasCardReward;

	public override void _Ready()
	{
		// 结算浮层（CanvasLayer，避免被战斗 UI 内部层级遮挡）
		resultLayer = new CanvasLayer { Layer = 20 };
		AddChild(resultLayer);

		RunSession session = RunSession.Instance;
		if (session == null || session.Current == null)
		{
			GD.PrintErr("[RunBattle] 缺少本局数据，回到主菜单。");
			CallDeferred(nameof(GoToMainMenuAbort));
			return;
		}

		// InSettlement：重进不再开新战斗，直接重现“胜利未领奖”的结算界面
		if (session.IsInSettlement)
		{
			ShowStoredSettlement(session);
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
		PackedScene battleScene = GD.Load<PackedScene>("res://Scenes/Battle/HexBattleScene.tscn");
		if (battleScene == null)
		{
			GD.PrintErr("[RunBattle] 无法加载 HexBattleScene.tscn。");
			CallDeferred(nameof(GoToMainMenuAbort));
			return;
		}

		battleView = battleScene.Instantiate<HexBattleScene>();
		battleView.UseRunSession = true;
		battleView.ShowBuiltInResult = false;
		battleView.EnableCommandApi = false;
		battleView.EnableDebugPanel = false;
		battleView.BattleReady += session => battlefield = session;
		battleView.BattleFinished += OnHexBattleFinished;
		AddChild(battleView);
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

	private void ShowVictoryResult()
	{
		RunSession session = RunSession.Instance;
		if (session == null || session.Current == null)
		{
			return;
		}

		// 战后把活体角色全量回写：HP + 整副默认卡组（含战斗中永久升级级数与顺序）
		WriteBackLiveCharacters(session);

		StageEncounterRow row = session.PendingEncounter;
		int dropTableId = row != null ? row.DropTableId : session.Current.PendingDropTableId;
		string name = row != null && !string.IsNullOrEmpty(row.Name) ? row.Name : session.Current.PendingEncounterName;

		// 生成候选并**先落盘为 InSettlement（未领奖）**，再弹窗——重进可重现同款结算
		List<int> candidateIds = BuildCardCandidates(session, dropTableId);
		session.EnterSettlement(string.IsNullOrEmpty(name) ? "胜利" : name, dropTableId, candidateIds);
		BuildResultOverlay(true, string.IsNullOrEmpty(name) ? "胜利" : name, candidateIds);
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

	/// <summary>InSettlement 读档重进：不开新战斗，直接用存档中的同款候选卡重现结算。</summary>
	private void ShowStoredSettlement(RunSession session)
	{
		List<int> candidates = new List<int>(session.Current.SettlementCandidateCardIds);
		hasCardReward = candidates.Count > 0;
		string name = string.IsNullOrEmpty(session.Current.SettlementEncounterName)
			? "胜利"
			: session.Current.SettlementEncounterName;
		BuildResultOverlay(true, name, candidates);
	}

	private List<int> BuildCardCandidates(RunSession session, int dropTableId)
	{
		hasCardReward = false;
		List<int> candidates = new List<int>();
		List<DropTableEntry> rows = BattleRewardPresenter.GetEntriesForTable(LoadingSystem.DropTableEntries, dropTableId);
		DropTableEntry cardRow = null;
		foreach (DropTableEntry entry in rows)
		{
			if (entry != null && entry.Category == DropCategory.Card)
			{
				cardRow = entry;
				break;
			}
		}

		if (cardRow == null)
		{
			return candidates;
		}

		hasCardReward = true;
		List<int> characterIds = BattleRewardPresenter.ResolveCardRewardCharacterIds(cardRow, session.Current);
		List<int> pool = new List<int>();
		foreach (int characterId in characterIds)
		{
			List<int> ids = LoadingSystem.GetCharacterRewardCardIds(characterId);
			foreach (int id in ids)
			{
				if (!pool.Contains(id))
				{
					pool.Add(id);
				}
			}
		}

		int amount = cardRow.Amount > 0 ? cardRow.Amount : 3;
		return BattleRewardPresenter.SampleFromPool(pool, amount, BattleSytem.RandomGenerator);
	}

	private void ShowDefeatResult()
	{
		BuildResultOverlay(false, "失败", new List<int>());
	}

	private void BuildResultOverlay(bool victory, string titleText, List<int> candidateIds)
	{
		Control overlay = new Control();
		overlay.Name = "ResultOverlay";
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = MouseFilterEnum.Stop;
		resultLayer.AddChild(overlay);

		ColorRect dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
		dim.SetAnchorsPreset(LayoutPreset.FullRect);
		dim.MouseFilter = MouseFilterEnum.Ignore;
		overlay.AddChild(dim);

		CenterContainer center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		PanelContainer panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(780, 520);
		center.AddChild(panel);

		MarginContainer margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 28);
		margin.AddThemeConstantOverride("margin_top", 24);
		margin.AddThemeConstantOverride("margin_right", 28);
		margin.AddThemeConstantOverride("margin_bottom", 24);
		panel.AddChild(margin);

		VBoxContainer vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 14);
		margin.AddChild(vbox);

		Label title = new Label
		{
			Text = victory ? $"胜利 —— {titleText}" : titleText,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 34);
		title.AddThemeColorOverride("font_color", victory ? Colors.LightYellow : Colors.IndianRed);
		vbox.AddChild(title);

		RunSession session = RunSession.Instance;
		int settlementDropTableId = session?.Current?.SettlementDropTableId ?? 0;
		List<DropTableEntry> rewardRows = victory
			? BattleRewardPresenter.GetEntriesForTable(LoadingSystem.DropTableEntries, settlementDropTableId)
			: new List<DropTableEntry>();
		HBoxContainer rewardTabs = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		rewardTabs.AddThemeConstantOverride("separation", 10);
		vbox.AddChild(rewardTabs);
		VBoxContainer cardSection = new VBoxContainer { Visible = false };
		cardSection.AddThemeConstantOverride("separation", 12);
		vbox.AddChild(cardSection);

		for (int rewardIndex = 0; rewardIndex < rewardRows.Count; rewardIndex++)
		{
			DropTableEntry entry = rewardRows[rewardIndex];
			if (entry == null) continue;
			Button rewardTab = new Button { Text = entry.Category == DropCategory.Card ? "卡牌" : BattleRewardPresenter.FormatRewardLine(entry) };
			rewardTab.CustomMinimumSize = new Vector2(150, 48);
			rewardTabs.AddChild(rewardTab);
			if (entry.Category == DropCategory.Card)
			{
				rewardTab.Pressed += () => cardSection.Visible = true;
				continue;
			}

			string rewardKey = $"{rewardIndex}:{entry.Category}:{entry.RewardParam}:{entry.Amount}";
			bool claimed = session?.Current?.SettlementClaimedRewardKeys?.Contains(rewardKey) == true;
			rewardTab.Disabled = claimed;
			if (claimed) rewardTab.Text += "（已领取）";
			rewardTab.Pressed += () =>
			{
				RunSession currentSession = RunSession.Instance;
				if (currentSession?.Current == null || currentSession.Current.SettlementClaimedRewardKeys.Contains(rewardKey)) return;
				BattleRewardPresenter.ApplyRewardEntryToRun(entry, currentSession.Current);
				currentSession.Current.SettlementClaimedRewardKeys.Add(rewardKey);
				currentSession.Save();
				rewardTab.Text = BattleRewardPresenter.FormatRewardLine(entry) + "（已领取）";
				rewardTab.Disabled = true;
			};
		}

		chosenCardId = 0;
		Button confirmReturn = null;
		bool hasPickableCards = victory && hasCardReward && candidateIds.Count > 0;

		if (hasPickableCards)
		{
			Label hint = new Label { Text = "请选择一张卡牌加入永久卡组：", HorizontalAlignment = HorizontalAlignment.Center };
			hint.AddThemeFontSizeOverride("font_size", 18);
			cardSection.AddChild(hint);

			HBoxContainer cardRowBox = new HBoxContainer();
			cardRowBox.Alignment = BoxContainer.AlignmentMode.Center;
			cardRowBox.AddThemeConstantOverride("separation", 12);
			cardSection.AddChild(cardRowBox);

			List<Button> cardButtons = new List<Button>();
			foreach (int cardId in candidateIds)
			{
				Card template = LoadingSystem.CardDictionary.TryGetValue(cardId, out Card c) ? c : null;
				int ownerSlot = RunSession.Instance?.Current == null ? -1 : BattleRewardPresenter.FindOwningSlotIndex(RunSession.Instance.Current, cardId);
				string ownerName = "未知角色";
				if (ownerSlot >= 0 && ownerSlot < RunSession.Instance.Current.CharacterSlots.Count)
				{
					int ownerCharacterId = RunSession.Instance.Current.CharacterSlots[ownerSlot].CharacterId;
					ownerName = LoadingSystem.CharacterDictionary.TryGetValue(ownerCharacterId, out Character owner) ? owner.Name : $"角色 {ownerCharacterId}";
				}
				string text = template == null
					? $"卡牌ID {cardId}"
					: $"归属：{ownerName}\n{template.CardName}\n费用 {template.EnergyCost}　类型 {template.Category}";
				Button cardButton = new Button { Text = text, ToggleMode = true };
				cardButton.CustomMinimumSize = new Vector2(210, 110);
				cardButton.AddThemeFontSizeOverride("font_size", 15);
				int capturedId = cardId;
				cardButton.Toggled += (bool on) =>
				{
					if (on)
					{
						chosenCardId = capturedId;
						foreach (Button other in cardButtons)
						{
							if (other != cardButton)
							{
								other.ButtonPressed = false;
							}
						}
					}
					else if (chosenCardId == capturedId)
					{
						chosenCardId = 0;
					}

					if (confirmReturn != null)
					{
						confirmReturn.Disabled = chosenCardId == 0;
					}
				};
				cardRowBox.AddChild(cardButton);
				cardButtons.Add(cardButton);
			}
		}
		else if (victory)
		{
			Label noReward = new Label
			{
				Text = hasCardReward ? "该角色暂无可用卡池（请检查 CharacterRewardPool.csv）" : "本次没有额外掉落。",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			noReward.AddThemeFontSizeOverride("font_size", 18);
			noReward.AddThemeColorOverride("font_color", Colors.OrangeRed);
			cardSection.Visible = true;
			cardSection.AddChild(noReward);
		}

		confirmReturn = new Button
		{
			Text = victory ? (hasCardReward ? "确认选择并返回地图" : "返回地图") : "返回主菜单",
			Disabled = hasPickableCards,
		};
		confirmReturn.CustomMinimumSize = new Vector2(300, 52);
		confirmReturn.AddThemeFontSizeOverride("font_size", 22);
		confirmReturn.Pressed += () =>
		{
			if (victory)
			{
				OnReturnToMap();
			}
			else
			{
				OnAbortToMainMenu();
			}
		};
		vbox.AddChild(confirmReturn);
	}

	private void OnReturnToMap()
	{
		RunSession session = RunSession.Instance;
		if (session == null || session.Current == null)
		{
			GoToMainMenuAbort();
			return;
		}

		StageEncounterRow row = session.PendingEncounter ?? session.BuildPendingEncounterRowFromSave();
		int dropTableId = row != null ? row.DropTableId : session.Current.SettlementDropTableId;

		if (chosenCardId > 0)
		{
			int ownerSlot = BattleRewardPresenter.FindOwningSlotIndex(session.Current, chosenCardId);
			if (ownerSlot < 0)
			{
				ownerSlot = 0;
			}

			session.AddCardToSlotDeck(ownerSlot, chosenCardId, 0);
		}

		if (row != null && row.NodeType == MapNodeType.NormalCombat)
		{
			session.Current.MapState.NormalEncounterIndex++;
		}

		session.MarkCurrentNodeVisitedAndAdvanceEncounter();
		session.CompleteSettlementToMap();
		GetTree().ChangeSceneToFile(MapScenePath);
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
