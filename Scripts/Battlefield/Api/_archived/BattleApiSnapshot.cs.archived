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

    public object Summary() => new
    {
        phase = session.Phase.ToString(), round = session.Round, selectedUnitId = session.SelectedId,
        units = session.Occupancy.Placements.Values.Select(p => new { id = p.UnitId, role = p.Role.ToString(), hp = p.Unit.HP, shield = p.Unit.Shield, q = p.Coord.Q, r = p.Coord.R, presence = p.Presence.ToString() }),
        selected = PlayerInventory(session.SelectedId),
    };

    public object Hand(int playerId) => new { unitId = playerId, cards = session.GetHand(playerId).Select(c => new { instanceId = c.UniqueInGameId, cardId = c.CardId, name = c.CardName, cost = c.EnergyCost }) };
    public object Inventory(int playerId) => PlayerInventory(playerId);
    public object Unit(int unitId)
    {
        if (!session.Occupancy.Placements.TryGetValue(unitId, out var p)) return null;
        return new { id = p.UnitId, name = p.Name, role = p.Role.ToString(), hp = p.Unit.HP, maxHp = p.Unit.Max_HP, shield = p.Unit.Shield, energy = p.Unit.Energy, q = p.Coord.Q, r = p.Coord.R, presence = p.Presence.ToString(), intent = p.Role == BattlefieldRole.Enemy ? session.GetEnemyIntentDisplay(p.UnitId) : null };
    }
    public object Board(AxialHex center, int radius)
    {
        int actualRadius = Math.Clamp(radius, 0, 8);
        return session.Board.Cells.Values.Where(c => BattleRangeResolver.Distance(center, c.Coord) <= actualRadius).Select(c => new { q = c.Coord.Q, r = c.Coord.R, kind = c.Kind.ToString(), surface = c.Surface.ToString(), blocked = c.BlocksSight, items = c.Items.Select(Object), trigger = c.Trigger == null ? null : Object(c.Trigger) });
    }

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

    public object LegalMove()
    {
        int unitId = session.SelectedId;
        return session.Board.Cells.Keys.Select(cell => new { q = cell.Q, r = cell.R, path = session.Movement.FindPath(unitId, cell) }).Where(x => x.path.Count > 0).Select(x => new { x.q, x.r, steps = x.path.Count });
    }
    public object PreviewMove(AxialHex target)
    {
        var path = session.Movement.FindPath(session.SelectedId, target);
        string reason = path.Count == 0 ? "目标不可达。" : session.Movement.ValidatePath(session.SelectedId, path);
        return new { q = target.Q, r = target.R, canMove = reason.Length == 0, reason, path = path.Select(x => new { x.Q, x.R }) };
    }
    public object LegalCard(int cardId)
    {
        var card = session.GetHand(session.SelectedId).FirstOrDefault(x => x.CardId == cardId);
        if (card == null) return new { cardId, canPlay = false, reason = "当前手牌没有该卡。", targets = Array.Empty<object>() };
        var actor = session.Selected;
        var targets = session.GetCastCandidates(cardId).Select(x => new { x.Q, x.R }).ToArray();
        int cost = card.GetCurrentEnergyCost(actor.Unit);
        CardSpatialSpec spec = session.GetSpatialSpec(cardId);
        bool requiresTarget = card.NeedTarget || spec.HasSpatial;
        string reason = session.Phase != BattlefieldSession.BattlePhase.Player ? "当前不是玩家回合。" : actor.Unit.Energy < cost ? $"能量不足，需要 {cost} 点。" : requiresTarget && targets.Length == 0 ? "当前没有合法目标。" : "";
        return new { cardId, instanceId = card.UniqueInGameId, cost, requiresTarget, canPlay = reason.Length == 0, reason, targets };
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
