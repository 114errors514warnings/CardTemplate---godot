using System;
using System.Linq;
using CardSimulator.Battlefield;

/// <summary>Read-only API projection of battle state and player-permitted actions.</summary>
public sealed class BattleApiSnapshot
{
    private readonly BattlefieldSession session;
    public BattleApiSnapshot(BattlefieldSession session) => this.session = session;

    public object State() => new
    {
        phase = session.Phase.ToString(), round = session.Round, selectedUnitId = session.SelectedId,
        units = session.Occupancy.Placements.Values.Select(p => new
        {
            id = p.UnitId, name = p.Name, role = p.Role.ToString(), hp = p.Unit.HP, maxHp = p.Unit.Max_HP,
            shield = p.Unit.Shield, energy = p.Unit.Energy, q = p.Coord.Q, r = p.Coord.R, presence = p.Presence.ToString(),
            intent = p.Role == BattlefieldRole.Enemy ? session.GetEnemyIntentDisplay(p.UnitId) : null,
        }),
        selected = PlayerInventory(session.SelectedId),
        hands = session.PlayerIds.Select(id => new { unitId = id, cards = session.GetHand(id).Select(c => new { instanceId = c.UniqueInGameId, cardId = c.CardId, name = c.CardName, cost = c.EnergyCost }) }),
        groundObjects = session.Board.Cells.Values.Where(c => c.Items.Count > 0 || c.Trigger != null).Select(c => new { q = c.Coord.Q, r = c.Coord.R, items = c.Items.Select(Object), trigger = c.Trigger == null ? null : Object(c.Trigger) }),
    };

    public object LegalActions()
    {
        int unitId = session.SelectedId;
        var actor = session.Selected;
        return new
        {
            selectedUnitId = unitId,
            moveDestinations = session.Board.Cells.Keys.Select(cell => new { cell.Q, cell.R, path = session.Movement.FindPath(unitId, cell).Select(x => new { x.Q, x.R }) }).Where(x => x.path.Any()),
            cards = session.GetHand(unitId).Select(card =>
            {
                CardSpatialSpec spec = session.GetSpatialSpec(card.CardId);
                var targets = session.GetCastCandidates(card.CardId).Select(x => new { x.Q, x.R }).ToArray();
                int cost = card.GetCurrentEnergyCost(actor.Unit);
                bool requiresTarget = card.NeedTarget || spec.HasSpatial;
                string reason = session.Phase != BattlefieldSession.BattlePhase.Player ? "当前不是玩家回合。"
                    : actor.Unit.Energy < cost ? $"能量不足，需要 {cost} 点。"
                    : requiresTarget && targets.Length == 0 ? "当前没有合法目标。" : "";
                return new { cardId = card.CardId, instanceId = card.UniqueInGameId, cost, requiresTarget, canPlay = reason.Length == 0, reason, targets };
            }),
            currentCell = new { q = actor.Coord.Q, r = actor.Coord.R, items = session.Board.Cells[actor.Coord].Items.Select(Object) },
        };
    }

    private object PlayerInventory(int playerId)
    {
        var loadout = session.GetLoadout(playerId);
        return new { unitId = playerId, leftHand = loadout?.LeftHand == null ? null : Object(loadout.LeftHand), rightHand = loadout?.RightHand == null ? null : Object(loadout.RightHand), itemSlots = loadout?.Items.Select(item => item == null ? null : Object(item)) };
    }

    private static object Object(GroundObject item) => new
    {
        instanceId = item.InstanceId, definitionId = item.DefinitionId, kind = item.Kind.ToString(), handsRequired = item.HandsRequired,
        attackRange = item.AttackRange, moveBonus = item.MoveBonus, healAmount = item.HealAmount, damageAmount = item.DamageAmount,
        needsTarget = item.NeedsTarget, spatialShape = item.SpatialShape.ToString(), maxRange = item.ItemMaxRange,
    };
}
