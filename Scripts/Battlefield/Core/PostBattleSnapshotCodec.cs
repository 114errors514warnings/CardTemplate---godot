using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

/// <summary>
/// 战后战场快照的纯逻辑编解码（设计出处见 `README/施工文档/2026/2026.09/交互/战斗结算后战场操作交互案.md`
/// §七 4 改口径「位置落档」）：把单位布局、地面物件与随身 / 手位导出成存档 DTO，并在重建战场时还原。
/// 不依赖表现层与运行局，xUnit 可直接覆盖。
/// </summary>
public static class PostBattleSnapshotCodec
{
    /// <summary>地面物件 → 快照条目；<paramref name="cell"/> 为空 = 随身 / 手位上的物件（坐标写 0,0）。</summary>
    public static RunGroundObjectSave ToSave(GroundObject item, AxialHex? cell = null)
    {
        if (item == null) return null;
        return new RunGroundObjectSave
        {
            InstanceId = item.InstanceId,
            DefinitionId = item.DefinitionId,
            Kind = (int)item.Kind,
            TriggerMode = (int)item.TriggerMode,
            HandsRequired = item.HandsRequired,
            AttackRange = item.AttackRange,
            MoveBonus = item.MoveBonus,
            HealAmount = item.HealAmount,
            SpatialShape = (int)item.SpatialShape,
            ItemMaxRange = item.ItemMaxRange,
            ItemRadius = item.ItemRadius,
            ItemLength = item.ItemLength,
            ItemTrapId = item.ItemTrapId ?? string.Empty,
            DamageAmount = item.DamageAmount,
            Q = cell?.Q ?? 0,
            R = cell?.R ?? 0,
        };
    }

    /// <summary>快照条目 → 物件；<paramref name="save"/> 为空返回 null（空槽位 / 空手位）。</summary>
    public static GroundObject ToGroundObject(RunGroundObjectSave save)
    {
        if (save == null || string.IsNullOrWhiteSpace(save.InstanceId) || string.IsNullOrWhiteSpace(save.DefinitionId)) return null;
        GroundObjectKind kind = Enum.IsDefined(typeof(GroundObjectKind), save.Kind) ? (GroundObjectKind)save.Kind : GroundObjectKind.Item;
        EntryTriggerMode trigger = Enum.IsDefined(typeof(EntryTriggerMode), save.TriggerMode) ? (EntryTriggerMode)save.TriggerMode : EntryTriggerMode.Once;
        var item = new GroundObject(save.InstanceId, save.DefinitionId, kind, trigger,
            Math.Clamp(save.HandsRequired, 1, 2), Math.Max(1, save.AttackRange), save.MoveBonus, Math.Max(0, save.HealAmount))
        {
            SpatialShape = Enum.IsDefined(typeof(ItemSpatialShape), save.SpatialShape) ? (ItemSpatialShape)save.SpatialShape : ItemSpatialShape.None,
            ItemMaxRange = Math.Max(1, save.ItemMaxRange),
            ItemRadius = Math.Max(1, save.ItemRadius),
            ItemLength = Math.Max(1, save.ItemLength),
            ItemTrapId = save.ItemTrapId ?? string.Empty,
            DamageAmount = save.DamageAmount,
        };
        return item;
    }

    /// <summary>
    /// 把快照里的地面物件重建到战场：**先清空现有地面物件，再按快照放回**（重建战场的地面内容一律以快照为准，
    /// 与地图随机生成无关）。个别物件放不回（例如格点被改坏）只记进 <paramref name="warning"/>，不打断整体还原。
    /// </summary>
    public static void RestoreGroundObjects(BattleBoard board, IReadOnlyList<RunGroundObjectSave> objects, out string warning)
    {
        warning = "";
        if (board == null) { warning = "战场为空。"; return; }

        foreach (BattleCell cell in board.Cells.Values)
        {
            if (cell.Trigger != null) board.TryRemoveObject(cell.Coord, cell.Trigger.InstanceId, out _);
            foreach (GroundObject item in cell.Items.ToArray()) board.TryRemoveObject(cell.Coord, item.InstanceId, out _);
        }

        var failures = new List<string>();
        foreach (RunGroundObjectSave entry in objects ?? Array.Empty<RunGroundObjectSave>())
        {
            if (entry == null) continue;
            var coord = new AxialHex(entry.Q, entry.R);
            if (!board.TryAddObject(coord, ToGroundObject(entry), out string error))
                failures.Add($"{entry.DefinitionId}@({entry.Q},{entry.R})：{error}");
        }
        if (failures.Count > 0) warning = $"{failures.Count} 件地面物件未能按快照放回（{string.Join("；", failures.Take(3))}）。";
    }

    /// <summary>
    /// 快照 → 单位布局：玩家按角色槽位匹配，其余按导出序（`OrderIndex`）匹配，并用实例键 / 名字兜底；
    /// 输出「单位 → (坐标, 在场状态)」与「单位 → 生命」两张表。没有任何可匹配的单位时返回 false。
    /// </summary>
    public static bool ResolveUnitLayout(RunPostBattleSave snapshot, IReadOnlyList<int> playerIds,
        Func<int, string> instanceKeyOf, IReadOnlyDictionary<int, BattleUnitPlacement> placements,
        out Dictionary<int, (AxialHex Coord, BattlefieldPresence Presence)> layout,
        out Dictionary<int, int> hp, out string error)
    {
        layout = new Dictionary<int, (AxialHex, BattlefieldPresence)>();
        hp = new Dictionary<int, int>();
        error = "";
        if (snapshot?.Units == null || snapshot.Units.Count == 0) { error = "战后快照没有单位。"; return false; }
        if (placements == null || placements.Count == 0) { error = "重建战场没有单位。"; return false; }

        List<BattleUnitPlacement> order = placements.Values.ToList();
        var matched = new HashSet<int>();
        foreach (RunUnitPlacementSave entry in snapshot.Units.Where(x => x != null).OrderBy(x => x.OrderIndex))
        {
            BattleUnitPlacement target = null;
            // ① 玩家按角色槽位（重建战场的编组顺序与导出一致）。
            if (entry.Role == (int)BattlefieldRole.Player && entry.SlotIndex >= 0 && entry.SlotIndex < playerIds.Count)
                placements.TryGetValue(playerIds[entry.SlotIndex], out target);
            // ② 导出序：同源关卡重建 → 占位枚举序一致。
            if (target == null && entry.OrderIndex >= 0 && entry.OrderIndex < order.Count && !matched.Contains(order[entry.OrderIndex].UnitId))
                target = order[entry.OrderIndex];
            // ③ 怪物实例键（关卡 CSV 的 InstanceId）：跨场次稳定。
            if (target == null && !string.IsNullOrEmpty(entry.InstanceKey))
                target = order.FirstOrDefault(p => !matched.Contains(p.UnitId) && instanceKeyOf(p.UnitId) == entry.InstanceKey);
            // ④ 名字（同源编组的最后兜底）。
            if (target == null && !string.IsNullOrEmpty(entry.Name))
                target = order.FirstOrDefault(p => !matched.Contains(p.UnitId) && p.Name == entry.Name);
            if (target == null || !matched.Add(target.UnitId)) continue;

            BattlefieldPresence presence = Enum.IsDefined(typeof(BattlefieldPresence), entry.Presence)
                ? (BattlefieldPresence)entry.Presence : BattlefieldPresence.Active;
            layout[target.UnitId] = (new AxialHex(entry.Q, entry.R), presence);
            hp[target.UnitId] = entry.Hp;
        }

        if (layout.Count == 0) { error = "战后快照与重建战场没有可匹配的单位。"; return false; }
        return true;
    }
}
