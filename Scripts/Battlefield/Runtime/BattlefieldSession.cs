using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

/// <summary>Isolated foundation playground. Does not modify RunSession or invoke old global-target attacks.</summary>
public sealed class BattlefieldSession : IDisposable
{
    public BattleMapDefinition Definition { get; }
    public GeneratedBattlefield Generated { get; }
    public BattleBoard Board => Generated.Board;
    public BattleOccupancyService Occupancy { get; }
    public BattleMovementService Movement { get; }
    public List<int> PlayerIds { get; } = new();
    public int SelectedId { get; private set; }
    public int Round { get; private set; } = 1;
    public event Action Changed;
    public event Action<string> Message;

    public BattlefieldSession(BattleMapDefinition definition)
    {
        Definition = definition;
        Generated = BattleDeploymentService.Generate(definition);
        Occupancy = new BattleOccupancyService(Board);
        Movement = new BattleMovementService(Board, Occupancy);
        // Validate every template before constructing any live actors.
        foreach (int id in definition.PlayerCharacterIds)
            if (!LoadingSystem.CharacterDictionary.ContainsKey(id)) throw new ArgumentException($"角色 {id} 不存在。");
        foreach (int id in definition.MonsterIds)
            if (!LoadingSystem.MonsterDictionary.ContainsKey(id)) throw new ArgumentException($"怪物 {id} 不存在。");
        for (int i = 0; i < 3; i++)
        {
            var character = new CharacterInstance(LoadingSystem.CharacterDictionary[definition.PlayerCharacterIds[i]]);
            character.Energy = character.Max_costs;
            Register(new BattleUnitPlacement(character, character.Name, BattlefieldRole.Player, character.MovesPerTurn), Generated.PlayerCoords[i]);
            PlayerIds.Add(character.UniqueInGameId);
        }
        for (int i = 0; i < definition.MonsterIds.Count; i++)
        {
            var monster = new MonsterInstance(LoadingSystem.MonsterDictionary[definition.MonsterIds[i]]);
            Register(new BattleUnitPlacement(monster, monster.Name, BattlefieldRole.Enemy, 0), Generated.EnemyCoords[i]);
        }
        SelectedId = PlayerIds[0];
        Occupancy.Changed += Notify;
        Board.CellChanged += OnCellChanged;
        Movement.Entered += OnEntered;
    }

    private void Register(BattleUnitPlacement placement, AxialHex coord)
    {
        if (!Occupancy.TryPlace(placement, coord, out string error)) throw new InvalidOperationException(error);
        placement.Unit.OnDead = () => Occupancy.RemoveFromBoard(placement.UnitId, BattlefieldPresence.Defeated);
    }

    public bool Select(int unitId)
    {
        if (!PlayerIds.Contains(unitId) || Occupancy.Placements[unitId].Presence != BattlefieldPresence.Active) return false;
        SelectedId = unitId; Notify(); return true;
    }

    public BattleUnitPlacement Selected => Occupancy.Placements[SelectedId];

    public void NextTestRound()
    {
        Round++;
        foreach (int id in PlayerIds)
        {
            var p = Occupancy.Placements[id];
            if (p.Presence == BattlefieldPresence.Active && p.Unit is CharacterInstance character)
                character.Energy = character.Max_costs;
        }
        Movement.StartPlayerTurn(); Notify();
        Message?.Invoke("新验证回合：重置能量与移动次数。怪物行动尚未接入。");
    }

    public void SetTestEquipmentBonus(int bonus)
    {
        Selected.SetEquipmentMoveModifier("foundation-test-equipment", bonus); Notify();
    }

    private void OnEntered(BattlefieldEntry entry)
    {
        var p = Occupancy.Placements[entry.UnitId];
        Message?.Invoke($"{p.Name} 移动到 ({entry.To.Q},{entry.To.R})，消耗 1 能量、1 次移动。" +
            (entry.Trigger == null ? "" : $" 进入物件：{entry.Trigger.DefinitionId}（本批仅验证触发事件，效果待接入）。"));
        Notify();
    }
    private void OnCellChanged(AxialHex coord) => Notify();
    private void Notify() => Changed?.Invoke();

    public void Dispose()
    {
        Occupancy.Changed -= Notify; Board.CellChanged -= OnCellChanged; Movement.Entered -= OnEntered;
        foreach (var p in Occupancy.Placements.Values) p.Unit.OnDead = null;
        Changed = null; Message = null;
    }
}
