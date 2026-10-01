using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator.Battlefield;

/// <summary>Fixed-scale battlefield canvas. HUD is outside this clipped Control.</summary>
public partial class BattlefieldView : Control
{
    public BattlefieldSession Session { get; private set; }
    public Vector2 Pan { get; private set; }
    public bool Moving { get; private set; }
    public bool HasPendingPresentation => hasActiveMove || hasActiveAttack || presentationQueue.Count > 0;
    public bool HasCharacterRig(int unitId) => characterRigs.TryGetValue(unitId, out var rig) && rig.IsInsideTree();
    private bool dragging;
    private AxialHex? hover;
    public AxialHex? HoveredCell => hover;
    [Export] public float PlayerMoveCellsPerSecond = 5f;
    [Export] public float MonsterMoveCellsPerSecond = 4f;
    [Export] public float AttackPresentationSeconds = 0.55f;
    private sealed record PresentationStep(BattlefieldEntry Move, BattlefieldAttackEvent Attack);
    private readonly LinkedList<PresentationStep> presentationQueue = new();
    private BattlefieldEntry activeMove;
    private bool hasActiveMove;
    private float activeMoveElapsed;
    private readonly Dictionary<int, Vector2> visualUnitPositions = new();
    private readonly Dictionary<int, CharacterRig2D> characterRigs = new();
    private readonly Dictionary<int, float> defeatedRigSeconds = new();
    private BattlefieldAttackEvent activeAttack;
    private bool hasActiveAttack;
    private float activeAttackElapsed;
    private bool activeAttackImpactApplied;
    private readonly Dictionary<int, (int Hp, int Shield)> presentationStats = new();
    /// <summary>受击滞后条：只有生命伤害才登记（护盾吸收不产生滞后）。Started = 已到冲击帧，此后独立走满 AttackPresentationSeconds。</summary>
    private readonly Dictionary<int, (int FromHp, float Elapsed, bool Started)> damageLag = new();
    private const float BaseCellRadius = 42f;
    private const float HealthBarHeight = 8f;
    private const float HealthBarWidthFactor = 1.10f;
    private const float HealthBarTopOffset = 16f;
    private const float ShieldBadgeWidth = 11f;
    private const float ShieldBadgeHeight = 12f;
    private const float ShieldBadgeOverlap = 4f;
    private const float ShieldBadgeMinScale = .70f;
    private static readonly Color HealthBarBackColor = new("141c24");
    private static readonly Color HealthBarPlayerColor = new("6ee59c");
    private static readonly Color HealthBarEnemyColor = new("e0574f");
    private static readonly Color HealthBarLagColor = new(1f, 1f, 1f, .55f);
    private static readonly Color ShieldBadgeFillColor = new("c7d0d8");
    private static readonly Color ShieldBadgeBorderColor = new("5f6f7d");
    private static readonly Color ShieldBadgeTextColor = new("16202a");
    private Texture2D moveIntentIcon;
    private Texture2D attackIntentIcon;
    public event Action<string, Vector2> HoverDetails;
    public event Action<AxialHex> PointerPressed;
    public event Action<AxialHex> PointerDragged;
    public event Action<AxialHex> PointerReleased;
    /// <summary>外部（如出牌选目标）接管左键点击：返回 true 表示已消费，不再走内置选择/移动。</summary>
    public Func<AxialHex, bool> LeftClickOverride;

    public override void _Ready()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop;
        MouseExited += () => { hover = null; HoverDetails?.Invoke("", Vector2.Zero); QueueRedraw(); };
        Resized += () => { ClampPan(); QueueRedraw(); };
    }

    public void Bind(BattlefieldSession session)
    {
        if (Session != null) { Session.Changed -= Refresh; Session.UnitEntered -= OnUnitEntered; Session.AttackResolved -= OnAttackResolved; }
        foreach (CharacterRig2D rig in characterRigs.Values) rig.QueueFree();
        characterRigs.Clear(); defeatedRigSeconds.Clear();
        visualUnitPositions.Clear(); presentationQueue.Clear(); damageLag.Clear();
        moveIntentIcon = ResourceLoader.Load<Texture2D>("res://Resources/Images/UI/IntentIcons/intent_move.png");
        attackIntentIcon = ResourceLoader.Load<Texture2D>("res://Resources/Images/UI/IntentIcons/intent_attack.png");
        Session = session; Session.Changed += Refresh; Session.UnitEntered += OnUnitEntered; Session.AttackResolved += OnAttackResolved;
        SyncCharacterRigs(0);
        CenterSelected();
    }
    public override void _ExitTree()
    { if (Session != null) { Session.Changed -= Refresh; Session.UnitEntered -= OnUnitEntered; Session.AttackResolved -= OnAttackResolved; } }

    private void OnUnitEntered(BattlefieldEntry entry) { presentationQueue.AddLast(new PresentationStep(entry, null)); QueueRedraw(); }
    private void OnAttackResolved(BattlefieldAttackEvent attack)
    {
        foreach (int unitId in AffectedUnitIds(attack))
        {
            if (Session.Occupancy.Placements.TryGetValue(unitId, out var target))
                presentationStats[unitId] = (attack.TargetUnitId == unitId && attack.TargetHpBefore >= 0
                    ? attack.TargetHpBefore : target.Unit.HP,
                    attack.TargetUnitId == unitId && attack.TargetShieldBefore >= 0 ? attack.TargetShieldBefore : target.Unit.Shield);
        }
        // 只有掉血才留滞后条：护盾吸收（生命不变）不登记，治疗 / 最大生命变化也不经过此事件。
        if (attack.TargetUnitId.HasValue && attack.TargetHpBefore >= 0 && attack.TargetHpAfter >= 0
            && attack.TargetHpAfter < attack.TargetHpBefore)
            damageLag[attack.TargetUnitId.Value] = (attack.TargetHpBefore, 0f, false);
        if (!hasActiveAttack && presentationQueue.Last?.Value.Attack != null && CanMergePresentation(presentationQueue.Last.Value.Attack, attack))
        {
            BattlefieldAttackEvent prior = presentationQueue.Last.Value.Attack;
            presentationQueue.Last.Value = presentationQueue.Last.Value with { Attack = prior with
            {
                AffectedCells = CellsFor(prior).Concat(CellsFor(attack)).Distinct().ToArray(),
                AffectedUnitIds = AffectedUnitIds(prior).Concat(AffectedUnitIds(attack)).Distinct().ToArray(),
                IsExplosion = prior.IsExplosion || attack.IsExplosion,
            }};
        }
        else presentationQueue.AddLast(new PresentationStep(null, attack));
        if (!hasActiveMove && !hasActiveAttack && presentationQueue.Count == 0) visualUnitPositions.Clear();
        QueueRedraw();
    }

    private static bool CanMergePresentation(BattlefieldAttackEvent prior, BattlefieldAttackEvent next) =>
        next.EventId == prior.EventId + 1 && prior.SourceUnitId == next.SourceUnitId && prior.Mode == next.Mode && prior.From == next.From;
    private static IEnumerable<AxialHex> CellsFor(BattlefieldAttackEvent attack) => attack.AffectedCells ?? new[] { attack.To };
    private static IEnumerable<int> AffectedUnitIds(BattlefieldAttackEvent attack) => attack.AffectedUnitIds
        ?? (attack.TargetUnitId.HasValue ? new[] { attack.TargetUnitId.Value } : Array.Empty<int>());
    public override void _Process(double delta)
    {
        if (Session == null) return;
        foreach (int unitId in visualUnitPositions.Keys.Where(id => !Session.Occupancy.Placements.TryGetValue(id, out var p) || p.Presence != BattlefieldPresence.Active).ToArray())
            visualUnitPositions.Remove(unitId);
        if (!hasActiveMove && !hasActiveAttack && presentationQueue.First != null)
        {
            PresentationStep step = presentationQueue.First.Value; presentationQueue.RemoveFirst();
            if (step.Move != null)
            {
                activeMove = step.Move; hasActiveMove = true; activeMoveElapsed = 0;
                visualUnitPositions[activeMove.UnitId] = CellPosition(activeMove.From);
                if (characterRigs.TryGetValue(activeMove.UnitId, out var movingRig)) movingRig.PlayMove();
            }
            else
            {
                activeAttack = step.Attack; hasActiveAttack = true; activeAttackElapsed = 0; activeAttackImpactApplied = false;
                if (characterRigs.TryGetValue(activeAttack.SourceUnitId, out var attackingRig)) attackingRig.PlayAttack();
            }
        }
        if (hasActiveMove)
        {
            var step = activeMove; float speed = Session.Occupancy.Placements.TryGetValue(step.UnitId, out var p) && p.Role == BattlefieldRole.Enemy ? MonsterMoveCellsPerSecond : PlayerMoveCellsPerSecond;
            float duration = 1f / Math.Max(.1f, speed); activeMoveElapsed += (float)delta;
            visualUnitPositions[step.UnitId] = CellPosition(step.From).Lerp(CellPosition(step.To), Math.Min(1, activeMoveElapsed / duration));
            if (activeMoveElapsed >= duration)
            {
                // Keep the visual endpoint until the next movement event takes over.
                // Logic may already have committed the next cell before that event is rendered.
                visualUnitPositions[step.UnitId] = CellPosition(step.To);
                hasActiveMove = false;
                if (characterRigs.TryGetValue(step.UnitId, out var stoppedRig)) stoppedRig.PlayIdle();
            }
        }
        if (hasActiveAttack)
        {
            activeAttackElapsed += (float)delta;
            if (!activeAttackImpactApplied && activeAttackElapsed >= AttackPresentationSeconds * .94f)
            {
                activeAttackImpactApplied = true;
                foreach (int unitId in AffectedUnitIds(activeAttack))
                {
                    if (Session.Occupancy.Placements.TryGetValue(unitId, out var target))
                        presentationStats[unitId] = (target.Unit.HP, target.Unit.Shield);
                    if (characterRigs.TryGetValue(unitId, out var hitRig) && !hitRig.IsDead) hitRig.PlayHurt();
                }
            }
            if (activeAttackElapsed >= AttackPresentationSeconds)
            {
                foreach (int unitId in AffectedUnitIds(activeAttack)) presentationStats.Remove(unitId);
                hasActiveAttack = false;
            }
        }
        UpdateDamageLag((float)delta);
        if (!hasActiveMove && !hasActiveAttack && presentationQueue.Count == 0) visualUnitPositions.Clear();
        SyncCharacterRigs((float)delta);
        QueueRedraw();
    }

    private void SyncCharacterRigs(float delta)
    {
        if (Session == null) return;
        for (int slot = 0; slot < Session.PlayerIds.Count; slot++)
        {
            int id = Session.PlayerIds[slot];
            int characterId = Session.Definition.PlayerCharacterIds[slot];
            if (characterId is not (1002 or 1003) || !Session.Occupancy.Placements.TryGetValue(id, out var placement)) continue;
            if (!characterRigs.TryGetValue(id, out CharacterRig2D rig))
            {
                string path = characterId == 1003 ? "res://Scenes/Characters/IseraRig.tscn" : "res://Scenes/Characters/SwordmasterRig.tscn";
                rig = ResourceLoader.Load<PackedScene>(path).Instantiate<CharacterRig2D>();
                rig.Name = $"CharacterRig_{id}";
                rig.Scale = Vector2.One * .8f;
                AddChild(rig);
                characterRigs.Add(id, rig);
            }
            if (placement.Presence != BattlefieldPresence.Active)
            {
                if (!defeatedRigSeconds.ContainsKey(id) && delta > 0 && !HasPendingPresentation)
                { rig.PlayDeath(); defeatedRigSeconds[id] = 0; }
                if (defeatedRigSeconds.TryGetValue(id, out float seconds))
                { defeatedRigSeconds[id] = seconds + delta; rig.Visible = seconds < .85f; }
                continue;
            }
            defeatedRigSeconds.Remove(id);
            rig.Visible = true;
            CharacterRig2D.RigLoadout nextLoadout = ResolveRigLoadout(Session.GetLoadout(id));
            if (rig.Loadout != nextLoadout) rig.SetLoadout(nextLoadout);
            rig.Position = visualUnitPositions.TryGetValue(id, out Vector2 visual) ? visual : CellPosition(placement.Coord);
        }
    }

    private static CharacterRig2D.RigLoadout ResolveRigLoadout(BattlefieldSession.PlayerLoadout equipped)
    {
        GroundObject left = equipped?.LeftHand;
        GroundObject right = equipped?.RightHand;
        GroundObject twoHand = left?.HandsRequired == 2 ? left : right?.HandsRequired == 2 ? right : null;
        if (twoHand != null)
        {
            if (twoHand.DefinitionId.Contains("弓")) return CharacterRig2D.RigLoadout.Bow;
            if (twoHand.DefinitionId.Contains("典") || twoHand.DefinitionId.Contains("书")) return CharacterRig2D.RigLoadout.Tome;
            return CharacterRig2D.RigLoadout.TwoHandedWeapon;
        }
        bool shield = left?.DefinitionId.Contains("盾") == true;
        bool leftWeapon = left != null && !shield;
        bool rightWeapon = right != null;
        if (shield && rightWeapon) return CharacterRig2D.RigLoadout.SwordAndShield;
        if (shield) return CharacterRig2D.RigLoadout.LeftShield;
        if (leftWeapon && rightWeapon) return CharacterRig2D.RigLoadout.DualSwords;
        if (leftWeapon || rightWeapon) return CharacterRig2D.RigLoadout.RightSword;
        return CharacterRig2D.RigLoadout.None;
    }

    public Vector2 CellPosition(AxialHex coord)
    {
        var p = BattleHexLayout.Center(coord, Session.Definition.CellRadius);
        return new Vector2((float)p.X, (float)p.Y) + Size / 2 + Pan;
    }
    public AxialHex CellAt(Vector2 local)
    {
        var p = local - Size / 2 - Pan;
        return BattleHexLayout.Pick(p.X, p.Y, Session.Definition.CellRadius);
    }
    public void CenterSelected()
    {
        if (Session == null) return;
        var p = BattleHexLayout.Center(Session.Selected.Coord, Session.Definition.CellRadius);
        Pan = new Vector2(-(float)p.X, -(float)p.Y); ClampPan(); QueueRedraw();
    }
    public void SetMoving(bool value)
    { Moving = value; QueueRedraw(); }

    // ── 施法预览（拖卡/选卡时） ──
    private readonly HashSet<AxialHex> castCandidates = new();
    private readonly HashSet<AxialHex> castAffected = new();
    public bool HasCastPreview => castCandidates.Count > 0;

    public void SetCastPreview(IEnumerable<AxialHex> candidates, IEnumerable<AxialHex> affected,
        AxialHex origin, AxialHex? hover)
    {
        castCandidates.Clear(); castAffected.Clear();
        if (candidates != null) foreach (var c in candidates) castCandidates.Add(c);
        if (affected != null) foreach (var c in affected) castAffected.Add(c);
        QueueRedraw();
    }

    public void ClearCastPreview()
    {
        castCandidates.Clear(); castAffected.Clear();
        QueueRedraw();
    }

    // ── 移动路线规划 ──
    private readonly List<AxialHex> movePath = new();
    public void SetMovePath(IEnumerable<AxialHex> path)
    {
        movePath.Clear();
        if (path != null) foreach (var p in path) movePath.Add(p);
        QueueRedraw();
    }
    public void ClearMovePath() { movePath.Clear(); QueueRedraw(); }
    private void Refresh()
    {
        foreach (int id in visualUnitPositions.Keys.Where(id => !Session.Occupancy.Placements.TryGetValue(id, out var p) || p.Presence != BattlefieldPresence.Active).ToArray())
            visualUnitPositions.Remove(id);
        foreach (int id in presentationStats.Keys.Where(id => !Session.Occupancy.Placements.TryGetValue(id, out var p) || p.Presence != BattlefieldPresence.Active).ToArray())
            presentationStats.Remove(id);
        SyncCharacterRigs(0);
        QueueRedraw();
    }

    private void ClampPan()
    {
        if (Session == null || Size.X <= 0 || Size.Y <= 0) return;
        var centers = Session.Board.Cells.Keys.Select(x => BattleHexLayout.Center(x, Session.Definition.CellRadius)).ToArray();
        float r = (float)Session.Definition.CellRadius;
        // Retain a visible edge even when looking beyond a small board; do not alter the scale.
        Pan = new Vector2(Math.Clamp(Pan.X, -Size.X / 2 + 80 - (float)centers.Max(x => x.X) - r,
            Size.X / 2 - 80 - (float)centers.Min(x => x.X) + r),
            Math.Clamp(Pan.Y, -Size.Y / 2 + 80 - (float)centers.Max(x => x.Y) - r,
            Size.Y / 2 - 80 - (float)centers.Min(x => x.Y) + r));
    }

    public override void _GuiInput(InputEvent input)
    {
        if (Session == null) return;
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Right) { dragging = button.Pressed; AcceptEvent(); return; }
            if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) { AcceptEvent(); return; }
            if (button.ButtonIndex == MouseButton.Left && button.Pressed)
            {
                var coord = CellAt(button.Position);
                if (LeftClickOverride?.Invoke(coord) == true) { AcceptEvent(); return; }
                PointerPressed?.Invoke(coord);
                var target = Session.Occupancy.At(coord);
                if (!Moving && target?.Role == BattlefieldRole.Player)
                { SetMoving(false); Session.Select(target.UnitId); }
                AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                var coord = CellAt(button.Position);
                PointerReleased?.Invoke(coord);
                AcceptEvent();
            }
        }
        if (input is InputEventMouseMotion motion)
        {
            if (dragging && motion.ButtonMask.HasFlag(MouseButtonMask.Right))
            {
                Pan += motion.Relative;
                // Cached movement endpoints are screen-space positions; pan them with the board.
                foreach (int unitId in visualUnitPositions.Keys.ToArray()) visualUnitPositions[unitId] += motion.Relative;
                ClampPan(); HoverDetails?.Invoke("", Vector2.Zero); hover = null; QueueRedraw(); return;
            }
            dragging = false;
            hover = CellAt(motion.Position);
            if (motion.ButtonMask.HasFlag(MouseButtonMask.Left)) PointerDragged?.Invoke(hover.Value);
            HoverDetails?.Invoke(Describe(hover.Value), GlobalPosition + motion.Position);
            QueueRedraw();
        }
    }

    public string Describe(AxialHex coord)
    {
        if (!Session.Board.Cells.TryGetValue(coord, out var cell)) return "";
        string text = $"格点 ({coord.Q}, {coord.R})";
        var p = Session.Occupancy.At(coord);
        if (p != null)
        {
            var hoverStats = presentationStats.TryGetValue(p.UnitId, out var delayed) ? delayed : (p.Unit.HP, p.Unit.Shield);
            text += $"\n{p.Name} · {p.Role}\n生命 {hoverStats.Item1}/{p.Unit.Max_HP}  护盾 {hoverStats.Item2}\n攻击 {p.Unit.Attack}  防御 {p.Unit.Defend}";
            foreach (var state in p.Unit.States)
            {
                var definition = GetStateDefinition(state.Key);
                text += $"\n{definition.Name} ×{state.Value.Stacks}\n{definition.EffectDescription}\n衰减：{definition.DecayTiming} / {definition.DecayMode}";
            }
            if (p.Unit.States.Count == 0) text += "\n无状态";
            if (p.Role == BattlefieldRole.Enemy) text += "\n意图：" + Session.GetEnemyIntentDisplay(p.UnitId).Tooltip;
        }
        if (cell.Kind == BattleCellKind.Obstacle) text += "\n障碍：不可通行";
        if (cell.Surface == BattleSurface.Pit) text += "\n坑洞：不可通行";
        if (cell.Items.Count > 0) text += "\n地面物品：" + string.Join("、", cell.Items.Select(x => x.DefinitionId));
        if (cell.Trigger != null) text += $"\n{cell.Trigger.DefinitionId} · {cell.Trigger.TriggerMode}";
        if (Moving) { string error = Session.Movement.Validate(Session.SelectedId, coord); text += error.Length == 0 ? "\n可移动：1 能量 / 1 次" : "\n" + error; }
        return text;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("151f28"));
        if (Session == null) return;
        float r = (float)Session.Definition.CellRadius;
        foreach (var cell in Session.Board.Cells.Values)
        {
            var center = CellPosition(cell.Coord);
            if (!new Rect2(-r * 2, -r * 2, Size.X + r * 4, Size.Y + r * 4).HasPoint(center)) continue;
            Color fill = cell.Surface == BattleSurface.Pit ? new Color("070c12") : cell.Kind == BattleCellKind.Obstacle ?
                new Color("515464") : new Color("25323d");
            var points = Enumerable.Range(0, 6).Select(i => center + Vector2.FromAngle(Mathf.DegToRad(60 * i - 30)) * r).ToArray();
            DrawColoredPolygon(points, fill);
            Color border = new Color("40525f");
            if (cell.Coord == Session.Selected.Coord) border = new Color("f5d98c");
            for (int i = 0; i < 6; i++) DrawLine(points[i], points[(i + 1) % 6], border, cell.Coord == Session.Selected.Coord ? 2.5f : 1, true);
            if (cell.Kind == BattleCellKind.Obstacle) CenterText(center + new Vector2(0, 5), "障碍", 14, new Color("b4b7c0"));
            if (cell.Items.Count > 0) CenterText(center + new Vector2(0, 29), $"物品 ×{cell.Items.Count}", 12, new Color("e6bd78"));
            if (cell.Trigger != null) CenterText(center + new Vector2(0, 28), cell.Trigger.Kind == GroundObjectKind.Trap ? "陷阱" : "机关", 12, Colors.Orange);
        }
        foreach (var p in Session.Occupancy.Placements.Values.Where(x => x.Presence == BattlefieldPresence.Active))
        {
            var center = visualUnitPositions.TryGetValue(p.UnitId, out var visualCenter) ? visualCenter : CellPosition(p.Coord);
            if (hasActiveAttack)
            {
                Vector2 attackFrom = CellPosition(activeAttack.From);
                Vector2 attackTo = CellPosition(activeAttack.To);
                Vector2 direction = (attackTo - attackFrom).Normalized();
                float attackT = Math.Min(1f, activeAttackElapsed / AttackPresentationSeconds);
                if (p.UnitId == activeAttack.SourceUnitId && activeAttack.Mode == WeaponAttackMode.Thrust && attackT >= .5f)
                    center = attackFrom.Lerp(attackTo, Mathf.Clamp((attackT - .5f) / .44f, 0f, 1f));
                if (AffectedUnitIds(activeAttack).Contains(p.UnitId) && activeAttack.Mode is WeaponAttackMode.AdjacentSingle or WeaponAttackMode.Fan or WeaponAttackMode.Ring && attackT >= .84f)
                {
                    if (activeAttack.Mode is WeaponAttackMode.Fan or WeaponAttackMode.Ring)
                        direction = new Vector2(-direction.Y, direction.X);
                    center += direction * (Mathf.Sin(Mathf.Pi * Mathf.Clamp((attackT - .84f) / .16f, 0f, 1f)) * 10f);
                }
            }
            if (!new Rect2(-60, -60, Size.X + 120, Size.Y + 120).HasPoint(center)) continue;
            Color color = p.Role == BattlefieldRole.Player ? new Color("69bec9") : new Color("d88885");
            if (hasActiveAttack && activeAttackImpactApplied && AffectedUnitIds(activeAttack).Contains(p.UnitId)) color = Colors.Red;
            bool hasRig = characterRigs.TryGetValue(p.UnitId, out var rig) && rig.Visible;
            if (!hasRig) DrawCircle(center + new Vector2(0, -7), 16, color);
            if (!hasRig) CenterText(center + new Vector2(0, -27), p.Name, 14, Colors.White);
            string label = p.Role == BattlefieldRole.Player ? (Session.PlayerIds.IndexOf(p.UnitId) + 1).ToString() : "敌";
            if (!hasRig) CenterText(center + new Vector2(0, -1), label, 15, new Color("16202a"));
            var shownStats = presentationStats.TryGetValue(p.UnitId, out var delayed) ? delayed : (p.Unit.HP, p.Unit.Shield);
            DrawUnitHealthBar(center, p, shownStats, color, (float)Session.Definition.CellRadius / BaseCellRadius);
            if (p.Role == BattlefieldRole.Enemy) CenterText(center + new Vector2(0, -46), Session.GetEnemyIntentionText(p.UnitId), 11, new Color("f0b27a"));
            if (p.Role == BattlefieldRole.Enemy)
            {
                EnemyIntentDisplay intent = Session.GetEnemyIntentDisplay(p.UnitId);
                if (intent.Certainty == EnemyIntentPreviewCertainty.UnknownNumbers)
                {
                    if (moveIntentIcon != null) DrawTextureRect(moveIntentIcon, new Rect2(center + new Vector2(-24, -66), new Vector2(16, 16)), false);
                    if (attackIntentIcon != null) DrawTextureRect(attackIntentIcon, new Rect2(center + new Vector2(8, -66), new Vector2(16, 16)), false);
                }
                else if (attackIntentIcon != null)
                    DrawTextureRect(attackIntentIcon, new Rect2(center + new Vector2(-31, -66), new Vector2(16, 16)), false);
            }
            CenterText(center + new Vector2(0, 48), p.Unit.States.Count == 0 ? "无状态" :
                string.Join(" ", p.Unit.States.Take(3).Select(x => $"{GetStateDefinition(x.Key).Name[..1]}{x.Value.Stacks}")), 12, new Color("b7c5ce"));
        }
        DrawCastPreview();
        DrawMovePath();
        DrawKnownEnemyIntentPreview();
        DrawAttackPresentation();
    }

    private void DrawKnownEnemyIntentPreview()
    {
        if (!hover.HasValue || Session.Occupancy.At(hover.Value)?.Role != BattlefieldRole.Enemy) return;
        int enemyId = Session.Occupancy.At(hover.Value).UnitId;
        foreach (AxialHex cell in Session.GetKnownEnemyIntentPreviewCells(enemyId))
        {
            Vector2 center = CellPosition(cell); float r = (float)Session.Definition.CellRadius;
            Vector2[] hex = Enumerable.Range(0, 6).Select(i => center + Vector2.FromAngle(Mathf.DegToRad(60 * i - 30)) * r).ToArray();
            DrawColoredPolygon(hex, new Color(1f, .20f, .18f, .25f));
            for (int i = 0; i < 6; i++) DrawLine(hex[i], hex[(i + 1) % 6], new Color("ff7165"), 2f, true);
        }
    }

    private void DrawAttackPresentation()
    {
        if (!hasActiveAttack) return;
        float t = Math.Min(1f, activeAttackElapsed / AttackPresentationSeconds);
        Vector2 from = CellPosition(activeAttack.From);
        Vector2 to = CellPosition(activeAttack.To);
        Color color = Session.Occupancy.Placements.TryGetValue(activeAttack.SourceUnitId, out var source) && source.Role == BattlefieldRole.Enemy
            ? new Color("ff9c78") : new Color("ffe18a");
        if (activeAttack.Mode == WeaponAttackMode.ThrowSingle) DrawParabolaAttack(from, to, t, color);
        else if (activeAttack.Mode == WeaponAttackMode.Fan) DrawSwing(from, to, t, color, 120f);
        else if (activeAttack.Mode == WeaponAttackMode.Ring) DrawSwing(from, to, t, color, 360f);
        else DrawLineStrike(from, to, t, color);

        if (activeAttackImpactApplied)
        {
            DrawAreaMarker(color);
            foreach (int unitId in AffectedUnitIds(activeAttack))
            {
                if (!Session.Occupancy.Placements.TryGetValue(unitId, out var target)) continue;
                Vector2 impact = CellPosition(target.Coord);
                DrawCircle(impact, 24, new Color(Colors.Red, .35f));
                DrawLine(impact + new Vector2(-13, -13), impact + new Vector2(13, 13), Colors.Red, 3f, true);
                DrawLine(impact + new Vector2(13, -13), impact + new Vector2(-13, 13), Colors.Red, 3f, true);
            }
        }
    }

    private void DrawLineStrike(Vector2 from, Vector2 to, float t, Color color)
    {
        float phase = Mathf.Clamp(t / .88f, 0f, 1f);
        Vector2 start = phase <= .5f ? from : from.Lerp(to, (phase - .5f) * 2f);
        Vector2 end = phase <= .5f ? from.Lerp(to, phase * 2f) : to;
        DrawLine(start, end, color, 4f, true);
    }

    private void DrawParabolaAttack(Vector2 from, Vector2 to, float t, Color color)
    {
        float reveal = t <= .5f ? t * 2f : 1f;
        float eraseStart = t <= .5f ? 0f : (t - .5f) * 2f;
        float height = Math.Max(28f, from.DistanceTo(to) * .22f);
        Vector2? last = null;
        for (int i = 0; i <= 24; i++)
        {
            float u = i / 24f;
            if (u < eraseStart || u > reveal) continue;
            Vector2 point = from.Lerp(to, u) + Vector2.Up * (Mathf.Sin(Mathf.Pi * u) * height);
            if (last.HasValue) DrawLine(last.Value, point, color, 3f, true);
            last = point;
        }
    }

    private void DrawSwing(Vector2 from, Vector2 to, float t, Color color, float degrees)
    {
        Vector2 direction = activeAttack.Direction.HasValue
            ? CellPosition(new AxialHex(activeAttack.From.Q + activeAttack.Direction.Value.Q, activeAttack.From.R + activeAttack.Direction.Value.R)) - from
            : to - from;
        if (direction.LengthSquared() < .01f) direction = Vector2.Right;
        float length = Math.Max(32f, from.DistanceTo(to));
        float extend = Mathf.Clamp(t / .18f, 0f, 1f);
        float angle = direction.Angle() - Mathf.DegToRad(degrees / 2f) + Mathf.DegToRad(degrees) * Mathf.Clamp((t - .18f) / .70f, 0f, 1f);
        DrawLine(from, from + Vector2.FromAngle(angle) * length * extend, color, 4f, true);
    }

    private void DrawAreaMarker(Color color)
    {
        bool isArea = activeAttack.Mode is WeaponAttackMode.Fan or WeaponAttackMode.Ring
            || activeAttack.Mode == WeaponAttackMode.ThrowSingle && CellsFor(activeAttack).Skip(1).Any()
            || activeAttack.IsExplosion;
        if (!isArea) return;
        float r = (float)Session.Definition.CellRadius;
        foreach (AxialHex cell in CellsFor(activeAttack).Distinct())
        {
            Vector2 center = CellPosition(cell);
            Vector2[] hex = Enumerable.Range(0, 6).Select(i => center + Vector2.FromAngle(Mathf.DegToRad(60 * i - 30)) * r).ToArray();
            DrawColoredPolygon(hex, new Color(color, .20f));
            for (int i = 0; i < 6; i++) DrawLine(hex[i], hex[(i + 1) % 6], color, 2f, true);
        }
    }

    private void DrawMovePath()
    {
        if (movePath.Count == 0) return;
        Vector2[] points = new Vector2[movePath.Count];
        for (int i = 0; i < movePath.Count; i++) points[i] = CellPosition(movePath[i]);
        Vector2 last = CellPosition(Session.Selected.Coord);
        float radius = (float)Session.Definition.CellRadius;
        foreach (var cell in movePath)
        {
            Vector2 center = CellPosition(cell);
            var hex = Enumerable.Range(0, 6).Select(i => center + Vector2.FromAngle(Mathf.DegToRad(60 * i - 30)) * radius).ToArray();
            DrawColoredPolygon(hex, new Color(0.18f, 0.78f, 0.43f, 0.30f));
            for (int i = 0; i < 6; i++) DrawLine(hex[i], hex[(i + 1) % 6], new Color("6ee59c"), 2.5f, true);
        }
        foreach (var point in points) { DrawLine(last, point, new Color("6ee59c"), 3f, true); last = point; }
        DrawCircle(points[^1] + new Vector2(0, -7), 7, new Color("6ee59c"));
    }

    private void DrawCastPreview()
    {
        if (castCandidates.Count == 0) return;
        float r = (float)Session.Definition.CellRadius;
        foreach (var cell in Session.Board.Cells.Values)
        {
            AxialHex coord = cell.Coord;
            if (!castCandidates.Contains(coord)) continue;
            bool red = castAffected.Contains(coord);
            Color fill = red ? new Color(0.95f, 0.25f, 0.22f, 0.42f) : new Color(1f, 0.85f, 0.25f, 0.28f);
            var center = CellPosition(coord);
            var points = Enumerable.Range(0, 6).Select(i => center + Vector2.FromAngle(Mathf.DegToRad(60 * i - 30)) * r).ToArray();
            DrawColoredPolygon(points, fill);
            for (int i = 0; i < 6; i++)
                DrawLine(points[i], points[(i + 1) % 6], red ? Colors.Red : Colors.Yellow, red ? 3f : 1.5f, true);
        }

    }

    private void CenterText(Vector2 baseline, string text, int size, Color color)
    {
        var font = ThemeDB.FallbackFont;
        DrawString(font, baseline - new Vector2(font.GetStringSize(text, fontSize: size).X / 2, 0), text, fontSize: size, modulate: color);
    }

    /// <summary>滞后条推进：到冲击帧才开始累加，之后独立走满 AttackPresentationSeconds（表现结束也继续走完），不引入独立计时器组件。</summary>
    private void UpdateDamageLag(float delta)
    {
        if (damageLag.Count == 0) return;
        bool counting = hasActiveAttack && activeAttackImpactApplied;
        foreach (int unitId in damageLag.Keys.ToArray())
        {
            var lag = damageLag[unitId];
            bool started = lag.Started || (counting && AffectedUnitIds(activeAttack).Contains(unitId));
            float elapsed = started ? lag.Elapsed + delta : 0f;
            if (elapsed >= AttackPresentationSeconds || (!started && !hasActiveAttack && presentationQueue.Count == 0)
                || !Session.Occupancy.Placements.TryGetValue(unitId, out var placement)
                || placement.Presence != BattlefieldPresence.Active)
                damageLag.Remove(unitId);
            else damageLag[unitId] = (lag.FromHp, elapsed, started);
        }
    }

    /// <summary>底条 + 生命填充 + 滞后条 + 左端护盾徽标。护盾不占条宽：条只表达生命，护盾另以徽标数值显示。</summary>
    private void DrawUnitHealthBar(Vector2 center, BattleUnitPlacement unit, (int, int Shield) shownStats, Color textColor, float scale)
    {
        if (unit.Unit.Max_HP <= 0) return;
        float height = HealthBarHeight * scale;
        float width = BaseCellRadius * HealthBarWidthFactor * scale;
        float left = center.X - width / 2f;
        float top = center.Y + HealthBarTopOffset * scale;
        DrawRect(new Rect2(left, top, width, height), HealthBarBackColor);
        float liveRatio = Mathf.Clamp((float)unit.Unit.HP / unit.Unit.Max_HP, 0f, 1f);
        if (damageLag.TryGetValue(unit.UnitId, out var lag))
        {
            float fromRatio = Mathf.Clamp((float)lag.FromHp / unit.Unit.Max_HP, 0f, 1f);
            float ghostRatio = Mathf.Lerp(fromRatio, liveRatio, Mathf.Clamp(lag.Elapsed / AttackPresentationSeconds, 0f, 1f));
            if (ghostRatio > 0f) DrawRect(new Rect2(left, top, width * ghostRatio, height), HealthBarLagColor);
        }
        float shownRatio = Mathf.Clamp((float)shownStats.Item1 / unit.Unit.Max_HP, 0f, 1f);
        if (shownRatio > 0f)
            DrawRect(new Rect2(left, top, width * shownRatio, height),
                unit.Role == BattlefieldRole.Player ? HealthBarPlayerColor : HealthBarEnemyColor);
        string text = unit.Role == BattlefieldRole.Player ? $"{shownStats.Item1}/{unit.Unit.Max_HP}" : shownStats.Item1.ToString();
        CenterText(new Vector2(center.X, top + height - scale), text, Mathf.Max(8, Mathf.RoundToInt(11f * scale)), textColor);
        if (shownStats.Item2 > 0)
            DrawShieldBadge(new Vector2(left - ShieldBadgeWidth / 2f + ShieldBadgeOverlap * scale, top + height / 2f), shownStats.Item2, scale);
    }

    /// <summary>护盾徽标：银色纯色盾形（代码绘制，不占图片资源），数值写在盾形内；过小时只保留盾形。</summary>
    private void DrawShieldBadge(Vector2 center, int shield, float scale)
    {
        float w = ShieldBadgeWidth / 2f * scale;
        float h = ShieldBadgeHeight / 2f * scale;
        Vector2[] points =
        {
            center + new Vector2(-w, -h), center + new Vector2(w, -h), center + new Vector2(w, h * .35f),
            center + new Vector2(0, h), center + new Vector2(-w, h * .35f),
        };
        DrawColoredPolygon(points, ShieldBadgeFillColor);
        for (int i = 0; i < points.Length; i++)
            DrawLine(points[i], points[(i + 1) % points.Length], ShieldBadgeBorderColor, Mathf.Max(1f, scale), true);
        if (scale < ShieldBadgeMinScale) return;
        string value = shield.ToString();
        int size = Mathf.Clamp(Mathf.RoundToInt(10f * scale) - (value.Length >= 3 ? 1 : 0), 7, 10);
        CenterText(center + new Vector2(0, h * .3f), value, size, ShieldBadgeTextColor);
    }

    private static StateDefinition GetStateDefinition(CardSimulator.StateType type) =>
        LoadingSystem.StateDictionary.TryGetValue(type, out var definition) && !string.IsNullOrEmpty(definition.Name)
            ? definition : new StateDefinition(type, type.ToString(), false);
}
