using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CardSimulator.Battlefield;

public enum BattlefieldRole { Player, Enemy, Protected }
public enum BattlefieldPresence { Active, Defeated, Departed }

public sealed class BattleUnitPlacement
{
    public int UnitId => Unit.UniqueInGameId;
    public IUnitInstance Unit { get; }
    public string Name { get; }
    public BattlefieldRole Role { get; }
    public AxialHex Coord { get; internal set; }
    public BattlefieldPresence Presence { get; internal set; }
    public int BaseMovesPerTurn { get; }
    public int MovesUsedThisTurn { get; internal set; }
    private readonly Dictionary<string, int> equipmentMoveModifiers = new(StringComparer.Ordinal);
    public int EffectiveMovesPerTurn => (int)Math.Clamp((long)BaseMovesPerTurn + equipmentMoveModifiers.Values.Sum(x => (long)x), 0, int.MaxValue);
    public int RemainingMoves => Math.Max(0, EffectiveMovesPerTurn - MovesUsedThisTurn);

    public BattleUnitPlacement(IUnitInstance unit, string name, BattlefieldRole role, int movesPerTurn)
    {
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        if (movesPerTurn < 0 || !Enum.IsDefined(role)) throw new ArgumentException("角色类型或移动次数无效。");
        Name = name; Role = role; BaseMovesPerTurn = movesPerTurn;
    }

    /// <summary>Equipment instance IDs deduplicate two-handed equipment. Does not reset spent moves.</summary>
    public void SetEquipmentMoveModifier(string equipmentInstanceId, int value)
    {
        if (string.IsNullOrWhiteSpace(equipmentInstanceId)) throw new ArgumentException("装备实例 ID 为空。");
        if (value == 0) equipmentMoveModifiers.Remove(equipmentInstanceId);
        else equipmentMoveModifiers[equipmentInstanceId] = value;
    }
}

public sealed class BattleOccupancyService
{
    private readonly BattleBoard board;
    private readonly Dictionary<int, BattleUnitPlacement> placements = new();
    private readonly Dictionary<AxialHex, int> occupants = new();
    public ReadOnlyDictionary<int, BattleUnitPlacement> Placements { get; }
    public ReadOnlyDictionary<AxialHex, int> Occupants { get; }
    public event Action Changed;

    public BattleOccupancyService(BattleBoard board)
    {
        this.board = board;
        Placements = new(placements); Occupants = new(occupants);
    }
    public bool TryPlace(BattleUnitPlacement placement, AxialHex coord, out string error)
    {
        error = "";
        if (placement == null || placement.Unit.HP <= 0) { error = "单位无效或已死亡。"; return false; }
        if (placements.ContainsKey(placement.UnitId)) { error = "单位已注册。"; return false; }
        if (!board.IsWalkable(coord) || occupants.ContainsKey(coord)) { error = "出生格不可通行或已有单位。"; return false; }
        placement.Coord = coord; placement.Presence = BattlefieldPresence.Active;
        placements.Add(placement.UnitId, placement); occupants.Add(coord, placement.UnitId); Changed?.Invoke(); return true;
    }

    public BattleUnitPlacement At(AxialHex coord) => occupants.TryGetValue(coord, out int id) ? placements[id] : null;

    public bool CanEnter(AxialHex coord) => board.IsWalkable(coord) && !occupants.ContainsKey(coord);

    internal void CommitMove(BattleUnitPlacement placement, AxialHex coord)
    {
        // Only the movement service commits validated steps; indices change before observers run.
        occupants.Remove(placement.Coord); placement.Coord = coord; occupants.Add(coord, placement.UnitId);
        Changed?.Invoke();
    }

    public void RemoveFromBoard(int unitId, BattlefieldPresence reason)
    {
        if (reason == BattlefieldPresence.Active) throw new ArgumentException("离场原因无效。");
        if (!placements.TryGetValue(unitId, out var p) || p.Presence != BattlefieldPresence.Active) return;
        occupants.Remove(p.Coord); p.Presence = reason; Changed?.Invoke();
    }

    public void SyncDeaths()
    {
        foreach (var p in placements.Values.Where(x => x.Presence == BattlefieldPresence.Active && x.Unit.HP <= 0).ToArray())
            RemoveFromBoard(p.UnitId, BattlefieldPresence.Defeated);
    }
}
