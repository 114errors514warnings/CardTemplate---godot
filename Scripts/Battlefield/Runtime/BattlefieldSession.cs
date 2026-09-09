using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

/// <summary>Runnable spatial battle session using the existing Card, Effect and State pipelines.</summary>
public sealed class BattlefieldSession : IDisposable
{
    public enum BattlePhase { Player, Monsters, Victory, Defeat }
    public BattleMapDefinition Definition { get; }
    public GeneratedBattlefield Generated { get; }
    public BattleBoard Board => Generated.Board;
    public BattleOccupancyService Occupancy { get; }
    public BattleMovementService Movement { get; }
    public List<int> PlayerIds { get; } = new();
    public int SelectedId { get; private set; }
    public int Round { get; private set; } = 1;
    public BattlePhase Phase { get; private set; } = BattlePhase.Player;
    public bool IsFinished => Phase is BattlePhase.Victory or BattlePhase.Defeat;
    /// <summary>当前歼灭战：没有在场敌人即胜利。保护与生存目标由后续目标策略接入。</summary>
    public bool AllEnemiesDefeated => Occupancy != null
        && Occupancy.Placements.Values.All(x => x.Role != BattlefieldRole.Enemy || x.Presence != BattlefieldPresence.Active);
    public event Action Changed;
    public event Action<string> Message;
    public event Action<BattlePhase> Finished;

    // 空间场景持有独立牌堆，但卡牌效果仍统一调用 Card.Apply / EffectSystem / StateSystem。
    private readonly Dictionary<int, List<Card>> drawPiles = new();
    private readonly Dictionary<int, List<Card>> discardPiles = new();
    private readonly Dictionary<int, List<Card>> hands = new();
    private readonly Random random = new();
    private readonly Dictionary<int, int> cardsPlayedThisTurn = new();
    private readonly Dictionary<int, int> hpLostThisBattle = new();
    private readonly Dictionary<int, int> hpLossEvents = new();
    private readonly Dictionary<int, PlayerLoadout> loadouts = new();
    private readonly Dictionary<int, HandSlot> selectedHands = new();
    private PendingHandChoice pendingChoice;

    private sealed class PendingHandChoice
    {
        public int PlayerId;
        public Card SourceCard;
        public readonly List<(EffectType Effect, int[] Params)> Operations = new();
    }

    public bool HasPendingHandChoice => pendingChoice != null;
    public enum HandSlot { Left, Right }
    public sealed class PlayerLoadout
    {
        public GroundObject LeftHand;
        public GroundObject RightHand;
        public GroundObject[] Items { get; } = new GroundObject[3];
    }

    public CardSpatialSpec GetSpatialSpec(int cardId)
    {
        CardSpatialSpec configured = BattleCardSpatialRepository.ForCard(cardId);
        if (configured != null) return configured;
        Card card = hands.Values.SelectMany(x => x).FirstOrDefault(x => x.CardId == cardId)
            ?? LoadingSystem.CardDictionary.GetValueOrDefault(cardId);
        return new CardSpatialSpec
        {
            CardId = cardId,
            Shape = card != null && (card.Category == CardCategory.Attack || card.NeedTarget)
                ? CardSpatialShape.Single : CardSpatialShape.None,
            MaxRange = 1,
        };
    }

    public IReadOnlyCollection<AxialHex> GetCastCandidates(int cardId)
    {
        var spec = GetSpatialSpec(cardId); var origin = Selected.Coord;
        int effectiveRange = spec.Shape is CardSpatialShape.Single or CardSpatialShape.Burst or CardSpatialShape.Line
            ? Math.Max(spec.MaxRange, CurrentAttackRange) : spec.MaxRange;
        if (spec.Shape == CardSpatialShape.SelfMove) return Movement.LegalDestinations(SelectedId);
        if (spec.Shape == CardSpatialShape.Trap)
            return BattleRangeResolver.CellsWithinRange(origin, spec.MaxRange).Where(x => x != origin && Board.Cells.TryGetValue(x, out var c) && c.Walkable && c.Trigger == null && c.Items.Count == 0 && Occupancy.At(x) == null).ToArray();
        if (spec.Shape == CardSpatialShape.None) return new[] { origin };
        if (spec.Shape == CardSpatialShape.Line)
        {
            var cells = new List<AxialHex>();
            foreach (var direction in BattleRangeResolver.SixNeighborOffsets)
            {
                for (int i = 1; i <= effectiveRange; i++)
                {
                    var cell = new AxialHex(origin.Q + direction.Q * i, origin.R + direction.R * i);
                    if (!Board.Cells.ContainsKey(cell)) break;
                    cells.Add(cell);
                    bool blocked = Board.Cells[cell].BlocksSight || Board.Cells[cell].Kind == BattleCellKind.Obstacle || Occupancy.At(cell) != null;
                    if (blocked && !spec.Penetrates) break;
                }
            }
            return cells;
        }
        return BattleRangeResolver.CellsWithinRange(origin, effectiveRange).Where(x => Board.Cells.ContainsKey(x) && HasClearTrace(origin, x, spec.Penetrates)).ToArray();
    }

    public IReadOnlyCollection<AxialHex> GetAffectedCells(int cardId, AxialHex target)
    {
        CardSpatialSpec spec = GetSpatialSpec(cardId);
        if (spec.Shape == CardSpatialShape.Line)
        {
            AxialHex dir = BattleRangeResolver.PickLineDirection(Selected.Coord, target);
            return ResolveLineUntilBlocked(dir, Math.Max(1, spec.Length), spec.Penetrates);
        }
        return BattleRangeResolver.ResolveAffectedCells(Selected.Coord, target, spec).Where(Board.Cells.ContainsKey).ToArray();
    }

    private bool HasClearTrace(AxialHex origin, AxialHex target, bool penetrates)
    {
        if (penetrates || AxialHex.Distance(origin, target) <= 1) return true;
        AxialHex direction = BattleRangeResolver.PickLineDirection(origin, target);
        int distance = AxialHex.Distance(origin, target);
        for (int i = 1; i < distance; i++)
        {
            var cell = new AxialHex(origin.Q + direction.Q * i, origin.R + direction.R * i);
            if (!Board.Cells.TryGetValue(cell, out var data) || data.BlocksSight || data.Kind == BattleCellKind.Obstacle || Occupancy.At(cell) != null) return false;
        }
        return true;
    }

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
            loadouts[character.UniqueInGameId] = new PlayerLoadout();
            selectedHands[character.UniqueInGameId] = HandSlot.Left;
        }
        for (int i = 0; i < definition.MonsterIds.Count; i++)
        {
            var monster = new MonsterInstance(LoadingSystem.MonsterDictionary[definition.MonsterIds[i]]);
            Register(new BattleUnitPlacement(monster, monster.Name, BattlefieldRole.Enemy, 0), Generated.EnemyCoords[i]);
        }
        SelectedId = PlayerIds[0];
        InitializeDecks();
        PrepareEnemyIntentions();
        Occupancy.Changed += Notify;
        Board.CellChanged += OnCellChanged;
        Movement.Entered += OnEntered;
    }

    private void Register(BattleUnitPlacement placement, AxialHex coord)
    {
        if (!Occupancy.TryPlace(placement, coord, out string error)) throw new InvalidOperationException(error);
        placement.Unit.OnDead = () =>
        {
            Occupancy.RemoveFromBoard(placement.UnitId, BattlefieldPresence.Defeated);
            EvaluateOutcome();
        };
    }

    public bool Select(int unitId)
    {
        if (!PlayerIds.Contains(unitId) || Occupancy.Placements[unitId].Presence != BattlefieldPresence.Active) return false;
        SelectedId = unitId; Notify(); return true;
    }

    public BattleUnitPlacement Selected => Occupancy.Placements[SelectedId];
    public PlayerLoadout SelectedLoadout => loadouts[SelectedId];
    public HandSlot SelectedHand => selectedHands[SelectedId];
    public int CurrentAttackRange
    {
        get
        {
            GroundObject equipment = SelectedHand == HandSlot.Left ? SelectedLoadout.LeftHand : SelectedLoadout.RightHand;
            return equipment?.Kind == GroundObjectKind.Equipment ? equipment.AttackRange : 1;
        }
    }

    public void SelectHand(HandSlot hand) { selectedHands[SelectedId] = hand; Notify(); }

    public bool TryEquipFromCurrentCell(string instanceId, HandSlot hand, out string error)
    {
        error = "";
        GroundObject equipment = Board.Cells[Selected.Coord].Items.FirstOrDefault(x => x.InstanceId == instanceId && x.Kind == GroundObjectKind.Equipment);
        if (equipment == null) { error = "当前格没有该装备。"; return false; }
        var loadout = SelectedLoadout;
        var replaced = new List<GroundObject>();
        if (equipment.HandsRequired == 2)
        {
            if (loadout.LeftHand != null) replaced.Add(loadout.LeftHand);
            if (loadout.RightHand != null && !ReferenceEquals(loadout.RightHand, loadout.LeftHand)) replaced.Add(loadout.RightHand);
        }
        else
        {
            GroundObject old = hand == HandSlot.Left ? loadout.LeftHand : loadout.RightHand;
            if (old != null) replaced.Add(old);
            if (old?.HandsRequired == 2) { loadout.LeftHand = null; loadout.RightHand = null; }
        }
        if (!Board.TryRemoveObject(Selected.Coord, equipment.InstanceId, out _)) { error = "装备已被移动。"; return false; }
        foreach (var old in replaced)
        {
            Selected.SetEquipmentMoveModifier(old.InstanceId, 0);
            if (!Board.TryAddObject(Selected.Coord, old, out error)) throw new InvalidOperationException("换装事务失败：" + error);
        }
        if (equipment.HandsRequired == 2) loadout.LeftHand = loadout.RightHand = equipment;
        else if (hand == HandSlot.Left) loadout.LeftHand = equipment;
        else loadout.RightHand = equipment;
        Selected.SetEquipmentMoveModifier(equipment.InstanceId, equipment.MoveBonus);
        selectedHands[SelectedId] = hand; Notify();
        Message?.Invoke($"{Selected.Name} 装备 {equipment.DefinitionId}：攻击距离 {equipment.AttackRange}，移动额度 {equipment.MoveBonus:+#;-#;0}。");
        return true;
    }

    public bool TryPickItemFromCurrentCell(string instanceId, int slot, out string error)
    {
        error = "";
        if (slot < 0 || slot >= 3) { error = "道具栏位不可用。"; return false; }
        GroundObject item = Board.Cells[Selected.Coord].Items.FirstOrDefault(x => x.InstanceId == instanceId && x.Kind == GroundObjectKind.Item);
        if (item == null || !Board.TryRemoveObject(Selected.Coord, instanceId, out _)) { error = "当前格没有该道具。"; return false; }
        // 目标栏已有道具时交换：把原栏道具放回当前格。
        if (SelectedLoadout.Items[slot] != null) Board.TryAddObject(Selected.Coord, SelectedLoadout.Items[slot], out _);
        SelectedLoadout.Items[slot] = item; Notify(); return true;
    }

    public bool TryUseItem(int slot, out string error)
    {
        error = "";
        if (slot < 0 || slot >= 3 || SelectedLoadout.Items[slot] == null) { error = "该道具栏为空。"; return false; }
        GroundObject item = SelectedLoadout.Items[slot]; int before = Selected.Unit.HP;
        Selected.Unit.HP = Math.Min(Selected.Unit.Max_HP, Selected.Unit.HP + item.HealAmount);
        SelectedLoadout.Items[slot] = null; Notify();
        Message?.Invoke($"{Selected.Name} 使用 {item.DefinitionId}，生命 {before}→{Selected.Unit.HP}。");
        return true;
    }

    // ── 投掷型道具：需要选定目标，从道具栏或当前格直接使用 ──

    public IReadOnlyCollection<AxialHex> GetItemCastCandidates(GroundObject item)
    {
        if (item == null) return Array.Empty<AxialHex>();
        AxialHex origin = Selected.Coord;

        // 直线投掷：仅允许瞄准 6 个方向直线上的格子（沿 ItemLength 延伸，遇单位/障碍即停）。
        if (item.SpatialShape == ItemSpatialShape.Line)
        {
            var lineCells = new List<AxialHex>();
            int length = Math.Max(1, item.ItemLength);
            foreach (var dir in BattleRangeResolver.SixNeighborOffsets)
            {
                for (int i = 1; i <= length; i++)
                {
                    var cell = new AxialHex(origin.Q + dir.Q * i, origin.R + dir.R * i);
                    if (!Board.Cells.ContainsKey(cell)) break;
                    lineCells.Add(cell);
                    bool blocked = Board.Cells[cell].Kind == BattleCellKind.Obstacle || Board.Cells[cell].BlocksSight || Occupancy.At(cell) != null;
                    if (blocked) break; // 首个目标之后不可再瞄准（不能隔墙/隔人瞄准）
                }
            }
            return lineCells.ToArray();
        }

        int range = Math.Max(1, item.ItemMaxRange);
        IEnumerable<AxialHex> cells = BattleRangeResolver.CellsWithinRange(origin, range).Where(Board.Cells.ContainsKey);
        if (item.ItemTrapId.Length > 0)
            cells = cells.Where(x => x != origin && Board.Cells[x].Walkable && Board.Cells[x].Trigger == null
                && Board.Cells[x].Items.Count == 0 && Occupancy.At(x) == null);
        return cells.ToArray();
    }

    public bool IsValidItemTarget(GroundObject item, AxialHex target) =>
        item != null && BattleRangeResolver.Distance(Selected.Coord, target) <= Math.Max(1, item.ItemMaxRange)
        && GetItemCastCandidates(item).Contains(target);

    /// <summary>
    /// 投掷型道具的“实际受影响格”。直线攻击默认会被方向上的第一个单位/障碍挡住，
    /// 只命中该格（后续不再穿透），除非道具被标记为穿透（暂未提供穿透开关，后续可扩展）。
    /// </summary>
    public IReadOnlyCollection<AxialHex> GetItemAffectedCells(GroundObject item, AxialHex target)
    {
        if (item == null) return Array.Empty<AxialHex>();
        if (item.SpatialShape != ItemSpatialShape.Line)
            return BattleRangeResolver.ResolveItemAffectedCells(Selected.Coord, target, item).ToArray();

        // 直线：从施法者 origin 沿投掷方向走 length 格，遇第一个单位或障碍即停（首个目标）。
        AxialHex dir = BattleRangeResolver.PickLineDirection(Selected.Coord, target);
        return ResolveLineUntilBlocked(dir, Math.Max(1, item.ItemLength), penetrates: false);
    }

    /// <summary>直线攻击专用：从施法者沿 dir 走 length 格，命中方向上的第一个单位/障碍即停下（非穿透时）。</summary>
    private IReadOnlyCollection<AxialHex> ResolveLineUntilBlocked(AxialHex dir, int length, bool penetrates)
    {
        var affected = new List<AxialHex>();
        AxialHex origin = Selected.Coord;
        for (int i = 1; i <= length; i++)
        {
            var cell = new AxialHex(origin.Q + dir.Q * i, origin.R + dir.R * i);
            if (!Board.Cells.ContainsKey(cell)) break;
            affected.Add(cell);
            bool blocked = Board.Cells[cell].Kind == BattleCellKind.Obstacle || Board.Cells[cell].BlocksSight || Occupancy.At(cell) != null;
            if (blocked && !penetrates) break;
        }
        return affected;
    }

    public bool TryUseItemAt(int slot, AxialHex? target, out string error)
    {
        error = "";
        if (Phase != BattlePhase.Player || IsFinished) { error = "当前不是玩家出牌阶段。"; return false; }
        if (slot < 0 || slot >= 3 || SelectedLoadout.Items[slot] == null) { error = "该道具栏为空。"; return false; }
        GroundObject item = SelectedLoadout.Items[slot];
        if (item.NeedsTarget)
        {
            if (!target.HasValue) { error = "该道具需要选定目标。"; return false; }
            if (!IsValidItemTarget(item, target.Value)) { error = "超出投掷射程或目标不可用。"; return false; }
        }
        ApplyItemEffect(Selected, item, target, out error);
        if (error.Length > 0) return false;
        SelectedLoadout.Items[slot] = null;
        Notify();
        return true;
    }

    public bool TryUseItemFromCurrentCell(string instanceId, AxialHex? target, out string error)
    {
        error = "";
        if (Phase != BattlePhase.Player || IsFinished) { error = "当前不是玩家出牌阶段。"; return false; }
        if (HasPendingHandChoice) { error = "请先完成当前选牌效果。"; return false; }
        GroundObject item = Board.Cells[Selected.Coord].Items.FirstOrDefault(x => x.InstanceId == instanceId && x.Kind == GroundObjectKind.Item);
        if (item == null) { error = "当前格没有该道具。"; return false; }
        if (item.NeedsTarget)
        {
            if (!target.HasValue) { error = "该道具需要选定目标。"; return false; }
            if (!IsValidItemTarget(item, target.Value)) { error = "超出投掷射程或目标不可用。"; return false; }
        }
        ApplyItemEffect(Selected, item, target, out error);
        if (error.Length > 0) return false;
        Board.TryRemoveObject(Selected.Coord, instanceId, out _);
        Notify();
        return true;
    }

    public bool TryMoveItemBetweenSlots(int from, int to, out string error)
    {
        error = "";
        if (from < 0 || from >= 3 || to < 0 || to >= 3) { error = "道具栏位置无效。"; return false; }
        if (SelectedLoadout.Items[from] == null) { error = "源道具栏为空。"; return false; }
        if (to == from) return true;
        // 目标栏已有道具时直接交换，便于自由挪动。
        if (SelectedLoadout.Items[to] != null)
        {
            (SelectedLoadout.Items[from], SelectedLoadout.Items[to]) = (SelectedLoadout.Items[to], SelectedLoadout.Items[from]);
        }
        else
        {
            SelectedLoadout.Items[to] = SelectedLoadout.Items[from];
            SelectedLoadout.Items[from] = null;
        }
        Notify();
        return true;
    }

    public bool TryDropItemOnCurrentCell(int slot, out string error)
    {
        error = "";
        if (slot < 0 || slot >= 3 || SelectedLoadout.Items[slot] == null) { error = "该道具栏为空。"; return false; }
        if (!Board.TryAddObject(Selected.Coord, SelectedLoadout.Items[slot], out error))
        {
            if (error.Length == 0) error = "当前格无法放下该道具。";
            return false;
        }
        SelectedLoadout.Items[slot] = null;
        Notify();
        return true;
    }

    private void ApplyItemEffect(BattleUnitPlacement source, GroundObject item, AxialHex? target, out string error)
    {
        error = "";
        if (item.NeedsTarget && target.HasValue)
        {
            var affected = GetItemAffectedCells(item, target.Value);
            var tracked = Occupancy.Placements.Values.Where(x => x.Presence == BattlefieldPresence.Active).ToArray();
            var beforeHp = tracked.ToDictionary(x => x.UnitId, x => x.Unit.HP);

            if (item.ItemTrapId.Length > 0)
            {
                var trap = new GroundObject($"trap-{source.UnitId}-{Guid.NewGuid():N}".Substring(0, 20), item.ItemTrapId,
                    GroundObjectKind.Trap, EntryTriggerMode.EveryEntry);
                if (!Board.TryAddObject(target.Value, trap, out error))
                {
                    if (error.Length == 0) error = "陷阱目标必须没有单位或物品。";
                    return;
                }
                Message?.Invoke($"{source.Name} 在 ({target.Value.Q},{target.Value.R}) 投放了陷阱：{trap.DefinitionId}。");
                return;
            }

            foreach (var cell in affected)
            {
                var u = Occupancy.At(cell);
                if (u == null || u.Presence != BattlefieldPresence.Active) continue;
                if (u.Role == BattlefieldRole.Enemy && item.DamageAmount > 0)
                    u.Unit.HP = Math.Max(0, u.Unit.HP - item.DamageAmount);
                else if (u.Role == BattlefieldRole.Player && item.HealAmount > 0)
                    u.Unit.HP = Math.Min(u.Unit.Max_HP, u.Unit.HP + item.HealAmount);
            }
            foreach (var unit in tracked) TrackHpLoss(unit.Unit, beforeHp[unit.UnitId]);
            Message?.Invoke($"{source.Name} 使用 {item.DefinitionId}。");
            Occupancy.SyncDeaths(); EvaluateOutcome();
            return;
        }

        int before = source.Unit.HP;
        source.Unit.HP = Math.Min(source.Unit.Max_HP, source.Unit.HP + item.HealAmount);
        Message?.Invoke($"{source.Name} 使用 {item.DefinitionId}，生命 {before}→{source.Unit.HP}。");
    }

    internal void NextTestRound()
    {
        Round++;
        foreach (int id in PlayerIds)
        {
            var p = Occupancy.Placements[id];
            if (p.Presence == BattlefieldPresence.Active && p.Unit is CharacterInstance character)
                character.Energy = character.Max_costs;
        }
        Movement.StartPlayerTurn(); Notify();
        Message?.Invoke("烟测辅助回合：重置能量与移动次数。");
    }

    public void SetTestEquipmentBonus(int bonus)
    {
        Selected.SetEquipmentMoveModifier("foundation-test-equipment", bonus); Notify();
    }

    // ── 手牌、抽牌和完整回合循环 ──

    private void InitializeDecks()
    {
        foreach (int playerId in PlayerIds)
        {
            var placement = Occupancy.Placements[playerId];
            if (placement.Unit is not CharacterInstance character) continue;
            List<int> defaultCardIds = LoadingSystem.GetCharacterDefaultCardIdListByKey(
                character.id, LoadingSystem.CharacterDefaultDeckCsvPathKey, true);
            List<Card> draw = new();
            foreach (int cardId in defaultCardIds)
            {
                if (LoadingSystem.CardDictionary.TryGetValue(cardId, out Card template))
                {
                    Card deckCard = template.CreateDeckInstance();
                    character.DefaultDeck.Add(deckCard);
                    draw.Add(deckCard.CreateBattleInstanceFromDeckCard());
                }
            }
            character.drawpile = draw;
            character.discardpile = new List<Card>();
            character.handcards = new List<Card>();
            drawPiles[playerId] = character.drawpile;
            discardPiles[playerId] = character.discardpile;
            hands[playerId] = character.handcards;
            DrawCards(playerId, drawCardCount(placement));
        }
    }

    private static int drawCardCount(BattleUnitPlacement placement)
        => placement.Unit is CharacterInstance c ? Math.Max(1, c.drawCardNum) : 5;

    public IReadOnlyList<Card> GetHand(int playerId)
        => hands.TryGetValue(playerId, out var hand) ? hand : Array.Empty<Card>();
    public IReadOnlyList<Card> GetDrawPile(int playerId)
        => drawPiles.TryGetValue(playerId, out var pile) ? pile : Array.Empty<Card>();
    public IReadOnlyList<Card> GetDiscardPile(int playerId)
        => discardPiles.TryGetValue(playerId, out var pile) ? pile : Array.Empty<Card>();

    public int HandCount(int playerId) => hands.TryGetValue(playerId, out var hand) ? hand.Count : 0;
    public int DrawPileCount(int playerId) => drawPiles.TryGetValue(playerId, out var pile) ? pile.Count : 0;
    public int DiscardPileCount(int playerId) => discardPiles.TryGetValue(playerId, out var pile) ? pile.Count : 0;

    public void DrawCards(int playerId, int count)
    {
        if (!drawPiles.ContainsKey(playerId)) return;
        var draw = drawPiles[playerId];
        var hand = hands[playerId];
        for (int i = 0; i < count; i++)
        {
            if (draw.Count == 0) RecycleDiscardIntoDraw(playerId);
            if (draw.Count == 0) return;
            int index = random.Next(draw.Count);
            hand.Add(draw[index]);
            draw.RemoveAt(index);
        }
        Notify();
    }

    private void RecycleDiscardIntoDraw(int playerId)
    {
        var draw = drawPiles[playerId];
        var discard = discardPiles[playerId];
        if (discard.Count == 0) return;
        draw.AddRange(discard);
        discard.Clear();
        for (int i = draw.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (draw[i], draw[j]) = (draw[j], draw[i]);
        }
    }

    public void EndCurrentTurn()
    {
        if (Phase != BattlePhase.Player || HasPendingHandChoice || IsFinished) return;
        Phase = BattlePhase.Monsters;
        foreach (int playerId in PlayerIds)
        {
            var p = Occupancy.Placements[playerId];
            if (p.Presence != BattlefieldPresence.Active || p.Unit is not CharacterInstance character) continue;
            var hand = hands[playerId];
            var discard = discardPiles[playerId];
            for (int i = hand.Count - 1; i >= 0; i--)
            {
                Card card = hand[i];
                card.AppliedKeywords.RemoveAll(x => x.Flags.HasFlag(KeywordFlag.RemoveAtTurnEnd));
                if (card.HasKeyWord(CardKeyWord.Retain)) continue;
                hand.RemoveAt(i); discard.Add(card);
            }
            StateDecayProcessor.ProcessDecayAtTiming(character, DecayTrigger.OnTurnEnd);
        }
        ExecuteMonsterTurn();
        if (!IsFinished) StartNextPlayerRound();
    }

    private void ExecuteMonsterTurn()
    {
        Message?.Invoke("怪物回合开始。");
        foreach (var enemy in Occupancy.Placements.Values.Where(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active).OrderBy(x => x.UnitId).ToArray())
        {
            if (IsFinished) break;
            StateSystem.OnTurnStart(enemy.Unit);
            StateDecayProcessor.ProcessDecayAtTiming(enemy.Unit, DecayTrigger.OnTurnStart);
            var target = NearestAlivePlayer(enemy.Coord);
            if (target == null) { SetOutcome(BattlePhase.Defeat, "没有存活玩家。"); break; }
            bool needsTargetInRange = (enemy.Unit as MonsterInstance)?.SelectedIntention?.Any(effect => effect != null && effect.Length > 0 && (EffectType)effect[0] == EffectType.Damage) != false;
            if (needsTargetInRange && AxialHex.Distance(enemy.Coord, target.Coord) > 1)
            {
                AxialHex? step = BestStepToward(enemy, target);
                if (step.HasValue) Movement.TryMoveWithoutPlayerCost(enemy.UnitId, step.Value, out _);
            }
            if (enemy.Presence == BattlefieldPresence.Active && target.Presence == BattlefieldPresence.Active &&
                (!needsTargetInRange || AxialHex.Distance(enemy.Coord, target.Coord) == 1))
            {
                ExecuteEnemyIntention(enemy, target);
            }
            StateDecayProcessor.ProcessDecayAtTiming(enemy.Unit, DecayTrigger.OnTurnEnd);
            Occupancy.SyncDeaths(); EvaluateOutcome();
        }
        if (!IsFinished) PrepareEnemyIntentions();
    }

    private void PrepareEnemyIntentions()
    {
        foreach (var enemy in Occupancy.Placements.Values.Where(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active))
        {
            if (enemy.Unit is not MonsterInstance monster || monster.Table == null || monster.Table.Length == 0) continue;
            int index = random.Next(monster.Table.Length);
            monster.SetSelectedIntention(index, monster.Table[index]);
        }
        Notify();
    }

    public string GetEnemyIntentionText(int unitId)
    {
        if (!Occupancy.Placements.TryGetValue(unitId, out var placement) || placement.Unit is not MonsterInstance monster || monster.SelectedIntention == null)
            return "无意图";
        var parts = new List<string>();
        foreach (int[] effect in monster.SelectedIntention)
        {
            if (effect == null || effect.Length == 0) continue;
            EffectType type = (EffectType)effect[0];
            parts.Add(type switch
            {
                EffectType.Damage => $"攻击 +{(effect.Length > 2 ? effect[2] : 0)}",
                EffectType.Shield => $"防御 +{(effect.Length > 1 ? effect[1] : 0)}",
                EffectType.AddState => "施加状态",
                _ => type.ToString(),
            });
        }
        return parts.Count == 0 ? "无意图" : string.Join(" / ", parts);
    }

    private void ExecuteEnemyIntention(BattleUnitPlacement enemy, BattleUnitPlacement target)
    {
        MonsterInstance monster = enemy.Unit as MonsterInstance;
        int[][] intention = monster?.SelectedIntention;
        if (intention == null || intention.Length == 0) intention = new[] { new[] { (int)EffectType.Damage, 1, 0 } };
        IUnitInstance lastTarget = target.Unit;
        foreach (int[] effect in intention)
        {
            if (effect == null || effect.Length == 0 || target.Presence != BattlefieldPresence.Active) continue;
            EffectType type = (EffectType)effect[0];
            if (type == EffectType.Damage)
            {
                int[] args = effect.Length > 2 ? effect.Skip(2).ToArray() : Array.Empty<int>();
                int before = target.Unit.HP;
                using (new BattlefieldEffectTargetScope(enemy.Unit, target.Unit, new[] { target.Unit }, Array.Empty<IUnitInstance>(),
                    playerTurn: false, canAttack: CanDefaultAttack))
                {
                    EffectResult result = EffectSystem.ApplyAttack(enemy.Unit, target.Unit, args);
                    Message?.Invoke($"{enemy.Name} 执行 {GetEnemyIntentionText(enemy.UnitId)}，攻击 {target.Name}：{result.TotalValue} 伤害，HP {result.TargetHpBefore}→{result.TargetHpAfter}。");
                }
                TrackHpLoss(target.Unit, before); lastTarget = target.Unit;
            }
            else if (type == EffectType.Shield)
            {
                EffectSystem.ApplyShield(enemy.Unit, effect.Skip(1).ToArray());
            }
            else if (type == EffectType.AddState && effect.Length > 2 && Enum.IsDefined(typeof(StateType), effect[2]))
            {
                int stacks = effect.Length > 3 ? effect[3] : 1;
                StateSystem.AddOrUpdateState(lastTarget, (StateType)effect[2], stacks, ownerUnit: enemy.Unit);
            }
        }
    }

    private void StartNextPlayerRound()
    {
        Round++; cardsPlayedThisTurn.Clear();
        foreach (int playerId in PlayerIds)
        {
            var p = Occupancy.Placements[playerId];
            if (p.Presence != BattlefieldPresence.Active || p.Unit is not CharacterInstance character) continue;
            if (character.Shield > 0 && !StateSystem.TryGetStateStacks(character, StateType.ShieldCapEqualsHP, out _)) character.Shield = 0;
            StateSystem.OnTurnStart(character);
            StateDecayProcessor.ProcessDecayAtTiming(character, DecayTrigger.OnTurnStart);
            character.Energy = character.Max_costs;
            DrawCards(playerId, drawCardCount(p));
        }
        Movement.StartPlayerTurn(); Phase = BattlePhase.Player; Notify();
        Message?.Invoke($"玩家回合 {Round}：状态已结算，能量、移动次数与手牌已刷新。");
    }

    private BattleUnitPlacement NearestAlivePlayer(AxialHex from) => PlayerIds.Select(x => Occupancy.Placements[x])
        .Where(x => x.Presence == BattlefieldPresence.Active && x.Unit.HP > 0)
        .OrderBy(x => AxialHex.Distance(from, x.Coord)).ThenBy(x => x.UnitId).FirstOrDefault();

    private AxialHex? BestStepToward(BattleUnitPlacement enemy, BattleUnitPlacement target)
    {
        var queue = new Queue<AxialHex>();
        var firstStep = new Dictionary<AxialHex, AxialHex>();
        foreach (var next in BattleHexLayout.Neighbors(enemy.Coord).Where(Occupancy.CanEnter).OrderBy(x => x.Q).ThenBy(x => x.R))
        { queue.Enqueue(next); firstStep[next] = next; }
        while (queue.Count > 0)
        {
            AxialHex current = queue.Dequeue();
            if (AxialHex.Distance(current, target.Coord) == 1) return firstStep[current];
            foreach (var next in BattleHexLayout.Neighbors(current).Where(Occupancy.CanEnter).OrderBy(x => x.Q).ThenBy(x => x.R))
            {
                if (firstStep.ContainsKey(next)) continue;
                firstStep[next] = firstStep[current]; queue.Enqueue(next);
            }
        }
        return null;
    }

    // ── 空间出牌路由：空间层筛目标，既有 Card/Effect/State 管线结算 ──

    public bool TryCastCard(int cardId, AxialHex target, out string error)
    {
        error = string.Empty;
        var p = Selected;
        if (Phase != BattlePhase.Player || IsFinished) { error = "当前不是玩家出牌阶段。"; return false; }
        if (HasPendingHandChoice) { error = "请先完成当前选牌效果。"; return false; }
        if (p.Presence != BattlefieldPresence.Active || p.Unit.HP <= 0) { error = "当前角色无法出牌。"; return false; }

        Card handCard = null;
        foreach (Card c in GetHand(p.UnitId)) if (c != null && c.CardId == cardId) { handCard = c; break; }
        if (handCard == null) { error = "手牌中没有这张卡。"; return false; }

        var spec = GetSpatialSpec(cardId);

        int actualCost = handCard.GetCurrentEnergyCost(p.Unit);
        if (handCard.CardId == 11002010) actualCost = Math.Max(0, handCard.EnergyCost - hpLossEvents.GetValueOrDefault(p.UnitId));
        if (StateSystem.TryGetStateStacks(p.Unit, StateType.NextBattleCardFree, out _) && handCard.Category != CardCategory.State) actualCost = 0;
        if (p.Unit.Energy < actualCost) { error = $"能量不足，需要 {actualCost} 点能量。"; return false; }
        if (handCard.Category != CardCategory.State && StateSystem.TryGetStateStacks(p.Unit, StateType.BattleCardBlocked, out _))
        { error = "当前状态禁止打出战斗牌。"; return false; }
        if (handCard.ConditionParams.Contains(CardConditionType.NoBattleCardPlayedThisTurn) && cardsPlayedThisTurn.GetValueOrDefault(p.UnitId) > 0)
        { error = "本回合已经打出过战斗牌。"; return false; }

        switch (spec.Shape)
        {
            case CardSpatialShape.SelfMove:
                if (actualCost != 1) { error = "空间移动牌当前要求费用为 1，以免重复扣费。"; return false; }
                if (!Movement.TryMove(p.UnitId, target, out error)) return false;
                CompleteCardLifecycle(p, handCard);
                Notify();
                return true;

            case CardSpatialShape.Trap:
                if (BattleRangeResolver.Distance(p.Coord, target) > spec.MaxRange || !Board.Cells.ContainsKey(target)) { error = "超出陷阱射程或目标不存在。"; return false; }
                if (!Board.IsWalkable(target)) { error = "陷阱只能放在可通行的格子上。"; return false; }
                var trap = new GroundObject($"trap-{p.UnitId}-{Guid.NewGuid():N}".Substring(0, 20),
                    string.IsNullOrEmpty(spec.TrapId) ? "test_trap" : spec.TrapId, GroundObjectKind.Trap, EntryTriggerMode.EveryEntry);
                if (Occupancy.At(target) != null || Board.Cells[target].Items.Count > 0 || !Board.TryAddObject(target, trap, out error))
                { if (error.Length == 0) error = "陷阱目标必须没有单位或物品。"; return false; }
                p.Unit.Energy -= actualCost;
                CompleteCardLifecycle(p, handCard);
                Notify();
                Message?.Invoke($"在 ({target.Q},{target.R}) 埋设了陷阱：{trap.DefinitionId}（效果待接入）。");
                return true;

            case CardSpatialShape.Single:
            case CardSpatialShape.Burst:
            case CardSpatialShape.Line:
            {
                if (!GetCastCandidates(cardId).Contains(target)) { error = "超出卡牌射程或被单位/障碍阻挡。"; return false; }
                var affected = spec.Shape == CardSpatialShape.Line
                    ? ResolveLineUntilBlocked(BattleRangeResolver.PickLineDirection(p.Coord, target), Math.Max(1, spec.Length), spec.Penetrates)
                    : BattleRangeResolver.ResolveAffectedCells(p.Coord, target, spec).Where(Board.Cells.ContainsKey).ToArray();
                var enemies = new List<BattleUnitPlacement>();
                var seen = new HashSet<int>();
                foreach (var cell in affected)
                {
                    var unit = Occupancy.At(cell);
                    if (unit != null && unit.Role == BattlefieldRole.Enemy && seen.Add(unit.UnitId)) enemies.Add(unit);
                }
                // 允许“对空格/无敌人区域”出牌（打空）：卡牌照常消耗，伤害落空，其余效果仍结算。
                // 目标格只需在射程内即可（无论其中是否存在单位）；无敌人时传入 null 目标并让效果层跳过空目标。
                BattleUnitPlacement selected = enemies.Count > 0 ? enemies[0] : null;

                return ApplyCardThroughExistingPipeline(p, handCard, selected, enemies, actualCost, out error);
            }

            default:
            {
                // 无空间形状：自身目标卡（抽牌 / 护盾 / 自身状态）
                var allies = ActiveAlliesWithin(p, 1);
                return ApplyCardThroughExistingPipeline(p, handCard, p, Array.Empty<BattleUnitPlacement>(), actualCost, out error, allies);
            }
        }
    }

    private bool ApplyCardThroughExistingPipeline(BattleUnitPlacement source, Card card, BattleUnitPlacement selected,
        IReadOnlyList<BattleUnitPlacement> enemies, int cost, out string error, IReadOnlyList<BattleUnitPlacement> allies = null)
    {
        error = "";
        var allyList = allies ?? ActiveAlliesWithin(source, 1);
        var tracked = Occupancy.Placements.Values.Where(x => x.Presence == BattlefieldPresence.Active).ToArray();
        var beforeHp = tracked.ToDictionary(x => x.UnitId, x => x.Unit.HP);
        Card.CardApplyResult result;
        using (new BattlefieldEffectTargetScope(source.Unit, selected?.Unit,
            enemies.Select(x => x.Unit).ToArray(), allyList.Select(x => x.Unit).ToArray(), hpLostThisBattle.GetValueOrDefault(source.UnitId),
            playerTurn: true, canAttack: CanDefaultAttack))
        {
            result = card.Apply(source.Unit, selected?.Unit);
        }
        if (!result.Success) { error = result.ErrorMessage; return false; }
        foreach (var unit in tracked) TrackHpLoss(unit.Unit, beforeHp[unit.UnitId]);
        source.Unit.Energy -= cost;
        cardsPlayedThisTurn[source.UnitId] = cardsPlayedThisTurn.GetValueOrDefault(source.UnitId) + (card.Category == CardCategory.State ? 0 : 1);
        StateSystem.OnCardPlayed(source.Unit, card);
        if (card.Category == CardCategory.Attack) StateDecayProcessor.ProcessDecayAtTiming(source.Unit, DecayTrigger.OnAttackPlayed);
        BuildPendingOperations(source, card, result);
        CompleteCardLifecycle(source, card);
        if (cost == 0 && card.Category != CardCategory.State && StateSystem.TryGetStateStacks(source.Unit, StateType.NextBattleCardFree, out _))
            StateSystem.RemoveState(source.Unit, StateType.NextBattleCardFree);
        Occupancy.SyncDeaths(); EvaluateOutcome(); Notify();
        Message?.Invoke($"{source.Name} 打出 {card.CardName}，消耗 {cost} 能量。" + (result.EffectResult == null ? "" : " " + result.EffectResult.BuildSummary()));
        return true;
    }

    private IReadOnlyList<BattleUnitPlacement> ActiveAlliesWithin(BattleUnitPlacement source, int range) =>
        Occupancy.Placements.Values.Where(x => x.Role == source.Role && x.Presence == BattlefieldPresence.Active &&
            AxialHex.Distance(source.Coord, x.Coord) <= range).ToArray();

    private void BuildPendingOperations(BattleUnitPlacement source, Card card, Card.CardApplyResult result)
    {
        PendingHandChoice choice = null;
        for (int i = 0; i < card.EffectTypes.Length; i++)
        {
            EffectType type = card.EffectTypes[i];
            int[] raw = card.Params != null && i < card.Params.Length ? card.Params[i] : Array.Empty<int>();
            if (type == EffectType.UpgradePermanentCard)
            {
                bool requireKill = raw.Length > 2 && raw[2] > 0;
                if (!requireKill || CardPlayController.DidApplyResultKillTarget(result))
                {
                    var deck = (source.Unit as CharacterInstance)?.DefaultDeck;
                    if (deck != null && deck.Count > 0) deck[random.Next(deck.Count)].PermanentUpgradeLevel++;
                }
            }
            else if (type is EffectType.UpgradeBattleCard or EffectType.AddKeyword)
            {
                choice ??= new PendingHandChoice { PlayerId = source.UnitId, SourceCard = card };
                choice.Operations.Add((type, raw));
            }
        }
        if (choice != null && hands[choice.PlayerId].Count > 0)
        {
            pendingChoice = choice;
            Message?.Invoke("请选择一张手牌完成卡牌效果。");
        }
    }

    public bool TryChooseHandCard(Card target, out string message)
    {
        message = "";
        if (pendingChoice == null || target == null || !hands[pendingChoice.PlayerId].Contains(target)) { message = "该牌不能用于当前选择。"; return false; }
        foreach (var operation in pendingChoice.Operations)
        {
            if (operation.Effect == EffectType.UpgradeBattleCard) target.BattleUpgradeLevel++;
            else if (operation.Effect == EffectType.AddKeyword && operation.Params.Length >= 2)
            {
                var keyword = Enum.IsDefined(typeof(CardKeyWord), operation.Params[1]) ? (CardKeyWord)operation.Params[1] : CardKeyWord.None;
                var flags = operation.Params.Length > 2 ? (KeywordFlag)operation.Params[2] : KeywordFlag.None;
                if (keyword != CardKeyWord.None) target.AppliedKeywords.Add(new AppliedKeywordEntry { Keyword = keyword, Flags = flags });
            }
        }
        message = $"已对 {target.CardName} 完成 {pendingChoice.Operations.Count} 项卡牌操作。";
        pendingChoice = null; Notify(); return true;
    }

    private void CompleteCardLifecycle(BattleUnitPlacement source, Card card)
    {
        if (card == null || !hands.TryGetValue(source.UnitId, out var hand)) return;
        hand.Remove(card);
        if (card.Category == CardCategory.State) source.Unit.StatePile.Add(card);
        else if (card.HasKeyWord(CardKeyWord.Exhaust)) source.Unit.ExhaustPile.Add(card);
        else discardPiles[source.UnitId].Add(card);
    }

    private void TrackHpLoss(IUnitInstance unit, int before)
    {
        if (unit == null || unit.HP >= before) return;
        int loss = before - unit.HP;
        hpLostThisBattle[unit.UniqueInGameId] = hpLostThisBattle.GetValueOrDefault(unit.UniqueInGameId) + loss;
        hpLossEvents[unit.UniqueInGameId] = hpLossEvents.GetValueOrDefault(unit.UniqueInGameId) + 1;
        StateSystem.OnHpLost(unit, loss);
    }

    private bool CanDefaultAttack(IUnitInstance attacker, IUnitInstance target)
    {
        if (attacker == null || target == null || !Occupancy.Placements.TryGetValue(attacker.UniqueInGameId, out var a) ||
            !Occupancy.Placements.TryGetValue(target.UniqueInGameId, out var b)) return false;
        return a.Presence == BattlefieldPresence.Active && b.Presence == BattlefieldPresence.Active && AxialHex.Distance(a.Coord, b.Coord) == 1;
    }

    private void EvaluateOutcome()
    {
        Occupancy.SyncDeaths();
        if (!PlayerIds.Any(id => Occupancy.Placements[id].Presence == BattlefieldPresence.Active && Occupancy.Placements[id].Unit.HP > 0))
            SetOutcome(BattlePhase.Defeat, "全体玩家均已死亡。");
        else if (AllEnemiesDefeated) SetOutcome(BattlePhase.Victory, "所有敌人已被击败。");
        else if (Occupancy.Placements[SelectedId].Presence != BattlefieldPresence.Active)
            SelectedId = PlayerIds.First(id => Occupancy.Placements[id].Presence == BattlefieldPresence.Active);
    }

    private void SetOutcome(BattlePhase outcome, string reason)
    {
        if (IsFinished) return;
        Phase = outcome; Movement.PlayerTurn = false; Notify(); Message?.Invoke(reason); Finished?.Invoke(outcome);
    }

    private void OnEntered(BattlefieldEntry entry)
    {
        var p = Occupancy.Placements[entry.UnitId];
        string text = $"{p.Name} 移动到 ({entry.To.Q},{entry.To.R})，消耗 1 能量、1 次移动。";
        if (entry.Trigger != null)
        {
            if (entry.Trigger.Kind == GroundObjectKind.Trap)
            {
                int before = p.Unit.HP;
                int trapDamage = 3;
                int absorbed = Math.Min(p.Unit.Shield, trapDamage);
                p.Unit.Shield -= absorbed;
                int hpLoss = trapDamage - absorbed;
                if (hpLoss > 0) p.Unit.HP = Math.Max(0, p.Unit.HP - hpLoss);
                TrackHpLoss(p.Unit, before);
                text += $" 触发陷阱 {entry.Trigger.DefinitionId}，受到 {hpLoss + absorbed} 点伤害（护盾吸收 {absorbed}）。";
            }
            else
            {
                text += $" 进入物件：{entry.Trigger.DefinitionId}（机关效果后续批次）。";
            }
        }
        Message?.Invoke(text);
        Occupancy.SyncDeaths(); EvaluateOutcome(); Notify();
    }
    private void OnCellChanged(AxialHex coord) => Notify();
    private void Notify() => Changed?.Invoke();

    public void Dispose()
    {
        Occupancy.Changed -= Notify; Board.CellChanged -= OnCellChanged; Movement.Entered -= OnEntered;
        foreach (var p in Occupancy.Placements.Values) p.Unit.OnDead = null;
        Changed = null; Message = null;
        Finished = null;
    }
}
