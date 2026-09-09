using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CardSimulator.Battlefield;

public enum BattleCellKind { Normal, Obstacle }
public enum BattleSurface { Ground, Pit }
public enum GroundObjectKind { Item, Equipment, Trap, Mechanism }
public enum EntryTriggerMode { Once, EveryEntry }
/// <summary>投掷型道具的生效形状：单体 / 直线 / 扇形 / 环形（菱形暂未实现，枚举值保留）。</summary>
public enum ItemSpatialShape { None = 0, Single = 1, Line = 2, Fan = 3, Ring = 4, Diamond = 5 }

public sealed class GroundObject
{
    public string InstanceId { get; }
    public string DefinitionId { get; }
    public GroundObjectKind Kind { get; }
    public EntryTriggerMode TriggerMode { get; }
    public int HandsRequired { get; }
    public int AttackRange { get; }
    public int MoveBonus { get; }
    public int HealAmount { get; }
    // ── 投掷型道具（需要选定目标）的空间规格 ──
    public ItemSpatialShape SpatialShape { get; set; } = ItemSpatialShape.None;
    public int ItemMaxRange { get; set; } = 1;
    public int ItemRadius { get; set; } = 1;
    public int ItemLength { get; set; } = 1;
    public string ItemTrapId { get; set; } = "";
    public int DamageAmount { get; set; }
    public GroundObject(string instanceId, string definitionId, GroundObjectKind kind,
        EntryTriggerMode triggerMode = EntryTriggerMode.Once, int handsRequired = 1,
        int attackRange = 1, int moveBonus = 0, int healAmount = 0)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException("物件实例 ID 和定义 ID 不可为空。");
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(triggerMode)) throw new ArgumentException("物件类型无效。");
        if (handsRequired is < 1 or > 2 || attackRange < 1 || healAmount < 0) throw new ArgumentException("物件属性无效。");
        InstanceId = instanceId; DefinitionId = definitionId; Kind = kind; TriggerMode = triggerMode;
        HandsRequired = handsRequired; AttackRange = attackRange; MoveBonus = moveBonus; HealAmount = healAmount;
    }
    public bool IsTrigger => Kind is GroundObjectKind.Trap or GroundObjectKind.Mechanism;
    public bool NeedsTarget => SpatialShape != ItemSpatialShape.None;
}

/// <summary>Terrain states are independent of unit states. Trigger effects are wired in a later batch.</summary>
public sealed record BattleTerrainState(string InstanceId, string DefinitionId, int SourceUnitId,
    int Stacks, int RemainingTriggers);

public sealed class BattleCell
{
    public AxialHex Coord { get; }
    public BattleCellKind Kind { get; internal set; }
    public BattleSurface Surface { get; internal set; }
    public bool BlocksSight { get; internal set; }
    public bool Walkable => Kind != BattleCellKind.Obstacle && Surface != BattleSurface.Pit;
    public int Revision { get; internal set; }
    internal List<GroundObject> MutableItems { get; } = new();
    public ReadOnlyCollection<GroundObject> Items { get; }
    public GroundObject Trigger { get; internal set; }
    internal List<BattleTerrainState> MutableStates { get; } = new();
    public ReadOnlyCollection<BattleTerrainState> TerrainStates { get; }
    public BattleCell(AxialHex coord, BattleCellKind kind = BattleCellKind.Normal,
        BattleSurface surface = BattleSurface.Ground, bool blocksSight = false)
    {
        Coord = coord; Kind = kind; Surface = surface; BlocksSight = blocksSight;
        Items = MutableItems.AsReadOnly(); TerrainStates = MutableStates.AsReadOnly();
    }
}

public sealed class BattleBoard
{
    private readonly Dictionary<AxialHex, BattleCell> cells = new();
    private readonly HashSet<string> objectIds = new(StringComparer.Ordinal);
    public ReadOnlyDictionary<AxialHex, BattleCell> Cells { get; }
    public long Revision { get; private set; }
    public event Action<AxialHex> CellChanged;

    public BattleBoard(IEnumerable<BattleCell> initialCells)
    {
        Cells = new(cells);
        foreach (var cell in initialCells)
        {
            if (cell == null || !cells.TryAdd(cell.Coord, cell)) throw new ArgumentException("格点为空或坐标重复。");
        }
        if (cells.Count == 0) throw new ArgumentException("战场没有格点。");
    }

    public bool IsWalkable(AxialHex coord) => cells.TryGetValue(coord, out var cell) && cell.Walkable;

    public void ChangeTerrain(AxialHex coord, BattleCellKind kind, BattleSurface surface, bool blocksSight)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(surface)) throw new ArgumentException("地形类型无效。");
        var cell = cells[coord]; cell.Kind = kind; cell.Surface = surface; cell.BlocksSight = blocksSight;
        Changed(cell);
    }

    public bool TryAddObject(AxialHex coord, GroundObject item, out string error)
    {
        error = "";
        if (item == null || objectIds.Contains(item.InstanceId)) { error = "物件为空或实例已在地图中。"; return false; }
        if (!cells.TryGetValue(coord, out var cell) || !cell.Walkable) { error = "格点不可接收物件。"; return false; }
        if (cell.Trigger != null || (item.IsTrigger && cell.Items.Count > 0))
        { error = "陷阱、机关与物品堆互斥。"; return false; }
        if (item.IsTrigger) cell.Trigger = item;
        else cell.MutableItems.Add(item);
        objectIds.Add(item.InstanceId); Changed(cell); return true;
    }

    public bool TryRemoveObject(AxialHex coord, string instanceId, out GroundObject removed)
    {
        removed = null;
        if (!cells.TryGetValue(coord, out var cell)) return false;
        if (cell.Trigger?.InstanceId == instanceId) { removed = cell.Trigger; cell.Trigger = null; }
        else
        {
            int index = cell.MutableItems.FindIndex(x => x.InstanceId == instanceId);
            if (index < 0) return false;
            removed = cell.MutableItems[index]; cell.MutableItems.RemoveAt(index);
        }
        objectIds.Remove(instanceId); Changed(cell); return true;
    }

    private void Changed(BattleCell cell)
    {
        cell.Revision++; Revision++; CellChanged?.Invoke(cell.Coord);
    }
}
