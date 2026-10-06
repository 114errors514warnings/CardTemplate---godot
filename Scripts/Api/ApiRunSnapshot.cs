// Scripts/Api/ApiRunSnapshot.cs
// 运行局侧的只读快照：字段全部从 `RunFlowScene` 的公开访问面 + `RunSession` 的只读入口推导。
// 分块（`run.state` / `run.bag.state` / `run.camp.state` / `run.map.state` / `run.settlement.state`）
// 让调用方按需取，不必每次拉全量。

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>运行局 API 的只读投影。</summary>
public static class ApiRunSnapshot
{
    /// <summary>本局全景：时间点 / 天数 / 模式 / 角色槽 / 背包负荷 / 界面形态。</summary>
    public static object State(RunFlowScene scene)
    {
        RunSession session = RunSession.Instance;
        RunSaveData run = session?.Current;
        if (run == null) return new { hasRun = false, contentKind = scene?.ContentKind, campOpen = scene?.IsCampOpen ?? false };

        return new
        {
            hasRun = true,
            gameMode = run.GameMode,
            day = session.CurrentDay,
            timePoints = run.MapState.TimePoints,
            remainingToday = session.RemainingToday,
            pointsPerDay = RunTimePoints.PointsPerDay,
            act = run.MapState.Act,
            seed = run.MapState.Seed,
            currentNodeId = run.MapState.CurrentNodeId,
            gold = run.Gold,
            keys = run.Keys,
            visitedNodeCount = run.MapState.VisitedNodeIds.Count,
            pendingRestDay = run.MapState.PendingRestDay,
            currentNormalEncounterCount = run.MapState.CurrentNormalEncounterCount,
            pendingContentId = run.PendingContentId,
            pendingContentType = run.PendingContentType,
            characters = Characters(run).ToArray(),
            bag = new
            {
                load = session.CurrentBagLoad,
                loadLimit = session.BagLoadLimit,
                overloaded = session.IsBagOverloaded,
                arrangeBlockReason = session.BagArrangeBlockReason,
                canArrange = session.CanArrangeBag,
                entryCount = run.BagEntries.Count,
            },
            ui = new
            {
                contentKind = scene?.ContentKind,
                campOpen = scene?.IsCampOpen ?? false,
                mapVisible = scene?.IsMapVisible ?? false,
                mapSelectable = scene?.IsMapSelectable ?? false,
                bagOpen = scene?.Bag?.IsOpen ?? false,
                settlementOpen = scene?.Settlement?.IsPanelOpen ?? false,
                settlementBlocking = scene?.IsSettlementBlocking ?? false,
            },
        };
    }

    /// <summary>角色槽（HP / 手位 / 佩戴武器镜像），地图与营地都要用。</summary>
    public static IEnumerable<object> Characters(RunSaveData run)
    {
        RunSession session = RunSession.Instance;
        for (int i = 0; i < run.CharacterSlots.Count; i++)
        {
            RunCharacterSlotSave slot = run.CharacterSlots[i];
            yield return new
            {
                slotIndex = i,
                displayName = session.GetSlotDisplayName(i),
                characterId = slot.CharacterId,
                hp = slot.CurrentHp,
                maxHp = slot.MaxHp,
                equippedWeaponDefinitionId = slot.EquippedWeaponDefinitionId,
                leftHand = RunEquipmentSystem.HandText(run, i, RunEquipmentSystem.LeftHand),
                rightHand = RunEquipmentSystem.HandText(run, i, RunEquipmentSystem.RightHand),
                deckCount = session.GetSlotDeck(i).Count,
            };
        }
    }

    /// <summary>背包界面状态：页签 / 页码 / 负荷 / 横幅 / 每一格的格名与拖动载荷。</summary>
    public static object Bag(RunFlowScene scene)
    {
        RunSession session = RunSession.Instance;
        if (session?.Current == null) return new { hasRun = false };
        BagUi bag = scene?.Bag;
        if (bag == null || !bag.IsOpen)
            return new { hasRun = true, open = false, arrangeBlockReason = session.BagArrangeBlockReason, canArrange = session.CanArrangeBag };

        return new
        {
            hasRun = true,
            open = true,
            arrangeBlocked = bag.IsArrangeBlocked,
            arrangeBlockReason = session.BagArrangeBlockReason,
            load = bag.LoadText,
            tab = bag.ActiveTabLabel,
            activeSlotIndex = bag.ActiveSlotIndex,
            page = bag.PageNumber,
            pageCount = bag.PageCount,
            pageText = bag.PageText,
            canPreviousPage = bag.CanGoPreviousPage,
            canNextPage = bag.CanGoNextPage,
            filledCellCount = bag.BagCellCount,
            gridCellCount = bag.BagSlotCellCount,
            banner = bag.BannerText,
            hint = bag.HintText,
            bagCells = BagCells(bag).ToArray(),
            carryCells = CarryCells(scene, bag).ToArray(),
            handCells = HandCells(scene, bag).ToArray(),
        };
    }

    /// <summary>背包网格（固定 25 格，空格也在：`title` 为空即空格）。</summary>
    private static IEnumerable<object> BagCells(BagUi bag)
    {
        for (int index = 0; index < BagUi.PageCapacity; index++)
        {
            string name = BagUi.BagCellName(index);
            yield return new
            {
                cell = name,
                title = bag.BagSlotText(index),
                width = (int)bag.BagSlotSize(index).X,
                height = (int)bag.BagSlotSize(index).Y,
                payload = bag.PayloadOfCell(name),
            };
        }
    }

    private static IEnumerable<object> CarryCells(RunFlowScene scene, BagUi bag)
    {
        RunSession session = RunSession.Instance;
        for (int slot = 0; slot < RunBagSystem.CarryItemSlotCount; slot++)
        {
            string name = BagUi.CarryCellName(slot);
            yield return new
            {
                cell = name,
                slot,
                title = session.GetCarrySlotText(slot),
                payload = bag.PayloadOfCell(name),
            };
        }
    }

    private static IEnumerable<object> HandCells(RunFlowScene scene, BagUi bag)
    {
        RunSession session = RunSession.Instance;
        int slotIndex = bag.ActiveSlotIndex;
        for (int hand = 0; hand < RunEquipmentSystem.HandCount; hand++)
        {
            string name = BagUi.HandCellName(slotIndex, hand);
            yield return new
            {
                cell = name,
                slotIndex,
                hand = hand == RunEquipmentSystem.LeftHand ? "left" : "right",
                title = session.GetHandText(slotIndex, hand),
                payload = bag.PayloadOfCell(name),
            };
        }
    }

    /// <summary>装备界面状态（P0-18 界面半）：角色 Tab / 部位格（含部位与格序）/ 手位 / 横幅与提示。</summary>
    public static object Equip(RunFlowScene scene)
    {
        RunSession session = RunSession.Instance;
        if (session?.Current == null) return new { hasRun = false };
        EquipmentUi ui = scene?.Equipment;
        if (ui == null || !ui.IsOpen)
            return new { hasRun = true, open = false, arrangeBlockReason = session.BagArrangeBlockReason, canArrange = session.CanArrangeBag };

        return new
        {
            hasRun = true,
            open = true,
            arrangeBlocked = ui.IsArrangeBlocked,
            arrangeBlockReason = session.BagArrangeBlockReason,
            load = ui.LoadText,
            activeSlotIndex = ui.ActiveSlotIndex,
            accessorySlotCount = session.AccessorySlotCount,
            bodySlotCount = ui.BodySlotCount,
            banner = ui.BannerText,
            hint = ui.HintText,
            bagCells = EquipBagCells(ui).ToArray(),
            bodyCells = BodyCells(ui).ToArray(),
            handCells = EquipHandCells(ui).ToArray(),
        };
    }

    /// <summary>装备界面的背包装备列表（固定 25 格，空格 `title` 为空）。</summary>
    private static IEnumerable<object> EquipBagCells(EquipmentUi ui)
    {
        for (int index = 0; index < EquipmentUi.PageCapacity; index++)
        {
            string name = EquipmentUi.BagCellName(index);
            yield return new
            {
                cell = name,
                title = ui.BagSlotText(index),
                payload = ui.PayloadOfCell(name),
            };
        }
    }

    /// <summary>部位格（部位 × 格序；`kind` = `EquipmentSlotKind` 数值，`caption` = 界面格标题）。</summary>
    private static IEnumerable<object> BodyCells(EquipmentUi ui)
    {
        RunSession session = RunSession.Instance;
        for (int kind = 0; kind < RunEquipmentSystem.BodySlotKindCount; kind++)
        {
            for (int index = 0; index < RunEquipmentSystem.SlotCountOf(kind); index++)
            {
                string name = EquipmentUi.BodySlotName(kind, index);
                yield return new
                {
                    cell = name,
                    kind,
                    kindName = ((EquipmentSlotKind)kind).ToString(),
                    index,
                    caption = EquipmentUi.SlotCaption(kind, index),
                    title = session.GetBodySlotText(ui.ActiveSlotIndex, kind, index),
                    payload = ui.PayloadOfCell(name),
                };
            }
        }
    }

    /// <summary>手位格（左手 / 右手；与 `run.bag.state` 的 handCells 同形）。</summary>
    private static IEnumerable<object> EquipHandCells(EquipmentUi ui)
    {
        RunSession session = RunSession.Instance;
        int slotIndex = ui.ActiveSlotIndex;
        for (int hand = 0; hand < RunEquipmentSystem.HandCount; hand++)
        {
            string name = EquipmentUi.HandCellName(slotIndex, hand);
            yield return new
            {
                cell = name,
                slotIndex,
                hand = hand == RunEquipmentSystem.LeftHand ? "left" : "right",
                title = session.GetHandText(slotIndex, hand),
                payload = ui.PayloadOfCell(name),
            };
        }
    }

    /// <summary>营地（夜间 UI）状态。</summary>
    public static object Camp(RunFlowScene scene)
    {
        CampScene camp = scene?.Camp;
        if (camp == null || !Godot.GodotObject.IsInstanceValid(camp)) return new { open = false };
        return new
        {
            open = true,
            day = RunSession.Instance?.CurrentDay ?? 1,
            remainingToday = RunSession.Instance?.RemainingToday ?? 0f,
            watchMode = camp.WatchMode.ToString(),
            watcherSlot = camp.WatcherSlot,
            plannedSatiety = camp.PlannedSatiety,
            plannedEffectiveSatiety = camp.PlannedEffectiveSatiety,
            plannedEntryCount = camp.PlannedEntryCount,
            satietyText = camp.SatietyProgressText,
            foodPanelVisible = camp.FoodPanelVisible,
            cookPanelVisible = camp.CookPanelVisible,
            watchPanelVisible = camp.WatchPanelVisible,
            foodButtonDisabled = camp.FoodButtonDisabled,
            cookButtonDisabled = camp.CookButtonDisabled,
            cookedThisRest = camp.CookedThisRest,
            resolving = camp.IsResolving,
            previewText = camp.PreviewText,
            resultText = camp.ResultText,
            foodHint = camp.FoodHint,
            cookHint = camp.CookHint,
        };
    }

    /// <summary>地图状态（节点表见 `debug.run.map_state`；玩家口径这里只给可达格与当前位置）。</summary>
    public static object Map(RunFlowScene scene)
    {
        MapScene map = scene?.Map;
        if (map == null || !Godot.GodotObject.IsInstanceValid(map)) return new { ready = false };
        return new
        {
            ready = true,
            visible = scene.IsMapVisible,
            selectable = scene.IsMapSelectable,
            readOnly = map.IsReadOnly,
            currentNodeId = map.CurrentNodeId,
            startNodeId = map.StartNodeId,
            pendingNodeEnterId = map.PendingNodeEnterId,
            reachableNodes = map.ReachableNodeIds.ToArray(),
        };
    }

    /// <summary>结算界面状态（本轮只读：领取 / 关闭仍走玩家手动点击）。</summary>
    public static object Settlement(RunFlowScene scene)
    {
        SettlementUi ui = scene?.Settlement;
        RunSession session = RunSession.Instance;
        if (ui == null || session?.Current == null) return new { ready = false };
        List<SettlementItemTab> tabs = SettlementRewardPresenter.BuildVisibleItemTabs(session.Current, LoadingSystem.DropTableEntries);
        List<SettlementCardPoolSave> pools = SettlementRewardPresenter.BuildVisibleCardPools(session.Current);
        return new
        {
            ready = true,
            panelOpen = ui.IsPanelOpen,
            cardPickOpen = ui.IsCardPickOpen,
            confirmOpen = ui.IsConfirmOpen,
            badgeVisible = ui.IsBadgeVisible,
            hasUnclaimed = ui.HasUnclaimed,
            unclaimedCount = ui.UnclaimedCount,
            items = tabs.Select(tab => new { claimKey = tab.ClaimKey, text = tab.Text, claimed = tab.Claimed }).ToArray(),
            cardPoolCount = pools.Count,
            // 每份的候选卡（`run.settlement.claim_card` 的 cardId 就取自这里；已领的份不再列出）。
            cardPools = pools.Select(pool => new
            {
                slotIndex = pool.SlotIndex,
                candidateCardIds = pool.CandidateCardIds == null ? Array.Empty<int>() : pool.CandidateCardIds.ToArray(),
            }).ToArray(),
        };
    }

    // 地点场景（村庄）快照：`Village` / `VillageSmithy` / `VillageRestaurant` 三个只读面
    // 2026-10-06 随村庄专用场景（`VillageScene` / `VillageVisit` / `VillageLayout`）撤除（方案甲）；
    // 「统一关卡通道」批接上关卡场景时，按新案重新出快照（设施界面形态可能复用 `SmithyUi` / `RestaurantUi`）。
}
