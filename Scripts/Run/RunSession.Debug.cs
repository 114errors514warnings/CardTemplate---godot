// Scripts/Run/RunSession.Debug.cs
// 运行局调试指令集合：供 `DebugApiRun`（`debug.run.*`）调用 —— 全部**超出玩家范围**，
// 与战斗侧的 `BattlefieldSession.Debug.cs` 同一口径（正式版删掉本文件即可）。
//
// 约定：这里只做「写状态 + 落档」，不做可达性 / 时间点 / 规则校验（那是玩家通道的职责）。

using System;
using System.Collections.Generic;
using Godot;

public partial class RunSession
{
    /// <summary>
    /// 直接设时间点（**允许负向**，与正式口径「时间点只增不减」相反）：调试要能把当天耗光来触发营地转场。
    /// 归零时同步「必须先休息」标记（`PendingRestDay` + `RestRemainingTimePoints`），与正式跨天口径一致。
    /// </summary>
    public bool DebugSetTimePoints(float value, out string error)
    {
        error = string.Empty;
        if (Current == null) { error = "没有进行中的本局。"; return false; }
        if (float.IsNaN(value) || value < 0f) { error = "时间点不能为负。"; return false; }

        float target = RunTimePoints.Quantize(value);
        float delta = target - Current.MapState.TimePoints;
        if (delta >= 0f)
        {
            // 正向：走正式入口（跨日界时它会记下剩余并置待休息）；不足一个计量单位（0.1）视为已达成。
            Current.MapState.TryAddTimePoints(delta, out error);
            error = string.Empty;
        }
        else
        {
            Current.MapState.TimePoints = target;
        }

        if (RunTimePoints.RemainingToday(Current.MapState.TimePoints) <= 0f)
        {
            Current.MapState.RestRemainingTimePoints = 0f;
            Current.MapState.PendingRestDay = true;
        }

        RunFoodSystem.TickTimePoints(Current, Math.Abs(delta));
        Save();
        return true;
    }

    /// <summary>把一个角色槽的 HP 直接写成指定值（可同时改上限；超出上限的当前值会被夹回）。</summary>
    public bool DebugSetSlotHp(int slotIndex, int hp, int maxHp, out string error)
    {
        error = string.Empty;
        if (Current == null) { error = "没有进行中的本局。"; return false; }
        if (slotIndex < 0 || slotIndex >= Current.CharacterSlots.Count) { error = $"角色槽 {slotIndex} 不存在。"; return false; }
        RunCharacterSlotSave slot = Current.CharacterSlots[slotIndex];
        if (maxHp > 0) slot.MaxHp = maxHp;
        slot.CurrentHp = Math.Clamp(hp, 0, Math.Max(1, slot.MaxHp));
        Save();
        return true;
    }

    /// <summary>直接塞一件背包物品（不做负荷 / 数量校验；分类与定义键见 `DataBase/Item/*.csv`）。</summary>
    public RunBagEntrySave DebugAddBagItem(BagCategory category, int definitionKey, int count)
    {
        if (Current == null) return null;
        RunBagEntrySave entry = RunBagSystem.Add(Current, category, definitionKey, Math.Max(1, count));
        Save();
        return entry;
    }

    /// <summary>清空背包（含随身格里放着的条目）：调试夹具清理用，返回移除的条目数。</summary>
    public int DebugClearBag()
    {
        if (Current == null) return 0;
        int removed = Current.BagEntries.Count;
        Current.BagEntries.Clear();
        Current.CarryItemSlots.Clear();
        for (int i = 0; i < RunBagSystem.CarryItemSlotCount; i++) Current.CarryItemSlots.Add(string.Empty);
        Save();
        return removed;
    }

    /// <summary>直接写左右手定义名（不校验部位组合、不处理背包条目归属 —— 与调试面板「装备到手」同一口径）。</summary>
    public bool DebugSetHand(int slotIndex, int hand, string definitionId, out string error)
    {
        error = string.Empty;
        if (Current == null) { error = "没有进行中的本局。"; return false; }
        if (slotIndex < 0 || slotIndex >= Current.CharacterSlots.Count) { error = $"角色槽 {slotIndex} 不存在。"; return false; }
        RunCharacterSlotSave slot = Current.CharacterSlots[slotIndex];
        string id = definitionId ?? string.Empty;
        if (hand == RunEquipmentSystem.RightHand) slot.RightHandDefinitionId = id;
        else { slot.LeftHandDefinitionId = id; slot.SyncLegacyWeaponField(); }
        Save();
        return true;
    }

    /// <summary>把当前格标成「已访问」并推进层内遭遇计数（= 不打架直接过一关）。</summary>
    public int DebugCompleteCurrentNode()
    {
        if (Current == null) return -1;
        int nodeId = Current.MapState.CurrentNodeId;
        MarkCurrentNodeVisitedAndAdvanceEncounter();
        return nodeId;
    }

    /// <summary>
    /// 强制背包闸门（**调试通道**，运行时字段、不入档）：`reason` 空串 = 强制**放行**
    /// （内容进行中也能拖，供模块级验证）；非空 = 强制按该原因阻断。宿主每帧重算闸门时会读 `BagArrangeOverride`。
    /// </summary>
    public void DebugSetBagGate(string reason)
    {
        BagArrangeOverride = string.IsNullOrEmpty(reason);
        BagArrangeBlockReason = reason ?? string.Empty;
        GD.Print($"[API] 调试已把背包闸门设为「{((reason ?? string.Empty).Length == 0 ? "强制放行" : reason)}」。");
    }

    /// <summary>写「本局游戏内不再显示放弃确认」并落档。</summary>
    public void DebugSetSuppressConfirm(bool suppress)
    {
        if (Current == null) return;
        Current.SuppressAbandonSettlementConfirm = suppress;
        Save();
    }

    /// <summary>存档路径（`debug.run` 侧回读/清档提示用）。</summary>
    public static string DebugSavePath => SavePath;

    /// <summary>
    /// 直接写金币（**允许任意值**，含 0）：商人 / 旅馆这类「金币侧验收」需要先摆出指定的钱包状态
    /// （村庄与商人交互案的验收都要求「金币为 0 时给原因」「金币够时能买」两态可复现）。
    /// </summary>
    public bool DebugSetGold(int value, out string error)
    {
        error = string.Empty;
        if (Current == null) { error = "没有进行中的本局。"; return false; }
        if (value < 0) { error = "金币不能为负。"; return false; }
        Current.Gold = value;
        Save();
        return true;
    }

    /// <summary>直接加钥匙（**绕过战斗掉落**）：商人钥匙格的验收与 Boss 门槛（≥2 把）的夹具。</summary>
    public bool DebugAddKeys(int amount, out string error)
    {
        error = string.Empty;
        if (Current == null) { error = "没有进行中的本局。"; return false; }
        if (amount == 0) { error = "钥匙增量不能为 0。"; return false; }
        Current.Keys = Math.Max(0, Current.Keys + amount);
        Save();
        return true;
    }

    /// <summary>清档（等于删掉本局进度；调用方通常随后 `debug.run.new_run`）。</summary>
    public static void DebugDeleteSave() => DeleteSave();

    /// <summary>当前背包条目快照（调试通道直接读原始条目，含随身格归属）。</summary>
    public List<object> DebugBagEntries()
    {
        var list = new List<object>();
        if (Current == null) return list;
        foreach (RunBagEntrySave entry in Current.BagEntries)
        {
            list.Add(new
            {
                instanceId = entry.InstanceId,
                category = entry.CategoryEnum.ToString(),
                definitionKey = entry.DefinitionKey,
                definitionId = entry.DefinitionId,
                count = entry.Count,
                rarity = entry.Rarity,
                expireDaysRemaining = entry.ExpireDaysRemaining,
                carrySlot = entry.CarrySlot,
                inBag = entry.IsInBag,
            });
        }

        return list;
    }
}
