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
    public bool HasPendingPresentation => hasActiveMove || hasActiveAttack || moveAnimationQueue.Count > 0 || attackAnimationQueue.Count > 0;
    private bool dragging;
    private AxialHex? hover;
    public AxialHex? HoveredCell => hover;
    [Export] public float PlayerMoveCellsPerSecond = 5f;
    [Export] public float MonsterMoveCellsPerSecond = 4f;
    [Export] public float AttackPresentationSeconds = 0.55f;
    private readonly Queue<BattlefieldEntry> moveAnimationQueue = new();
    private BattlefieldEntry activeMove;
    private bool hasActiveMove;
    private float activeMoveElapsed;
    private readonly Dictionary<int, Vector2> visualUnitPositions = new();
    private readonly Queue<BattlefieldAttackEvent> attackAnimationQueue = new();
    private BattlefieldAttackEvent activeAttack;
    private bool hasActiveAttack;
    private float activeAttackElapsed;
    private bool activeAttackImpactApplied;
    private readonly Dictionary<int, (int Hp, int Shield)> presentationStats = new();
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
        visualUnitPositions.Clear(); moveAnimationQueue.Clear(); attackAnimationQueue.Clear();
        moveIntentIcon = ResourceLoader.Load<Texture2D>("res://Resources/UI/IntentIcons/intent_move.png");
        attackIntentIcon = ResourceLoader.Load<Texture2D>("res://Resources/UI/IntentIcons/intent_attack.png");
        Session = session; Session.Changed += Refresh; Session.UnitEntered += OnUnitEntered; Session.AttackResolved += OnAttackResolved;
        CenterSelected();
    }
    public override void _ExitTree()
    { if (Session != null) { Session.Changed -= Refresh; Session.UnitEntered -= OnUnitEntered; Session.AttackResolved -= OnAttackResolved; } }

    private void OnUnitEntered(BattlefieldEntry entry) { moveAnimationQueue.Enqueue(entry); QueueRedraw(); }
    private void OnAttackResolved(BattlefieldAttackEvent attack)
    {
        if (attack.TargetUnitId.HasValue && attack.TargetHpBefore >= 0)
            presentationStats[attack.TargetUnitId.Value] = (attack.TargetHpBefore, attack.TargetShieldBefore);
        attackAnimationQueue.Enqueue(attack); QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (Session == null) return;
        foreach (int unitId in visualUnitPositions.Keys.Where(id => !Session.Occupancy.Placements.TryGetValue(id, out var p) || p.Presence != BattlefieldPresence.Active).ToArray())
            visualUnitPositions.Remove(unitId);
        if (!hasActiveMove && moveAnimationQueue.Count > 0) { activeMove = moveAnimationQueue.Dequeue(); hasActiveMove = true; activeMoveElapsed = 0; visualUnitPositions[activeMove.UnitId] = CellPosition(activeMove.From); }
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
            }
        }
        if (!hasActiveAttack && !hasActiveMove && moveAnimationQueue.Count == 0 && attackAnimationQueue.Count > 0)
        { activeAttack = attackAnimationQueue.Dequeue(); hasActiveAttack = true; activeAttackElapsed = 0; activeAttackImpactApplied = false; }
        if (hasActiveAttack)
        {
            activeAttackElapsed += (float)delta;
            if (!activeAttackImpactApplied && activeAttackElapsed >= AttackPresentationSeconds * .48f)
            {
                activeAttackImpactApplied = true;
                if (activeAttack.TargetUnitId.HasValue && activeAttack.TargetHpAfter >= 0)
                    presentationStats[activeAttack.TargetUnitId.Value] = (activeAttack.TargetHpAfter, activeAttack.TargetShieldAfter);
            }
            if (activeAttackElapsed >= AttackPresentationSeconds)
            {
                if (activeAttack.TargetUnitId.HasValue) presentationStats.Remove(activeAttack.TargetUnitId.Value);
                hasActiveAttack = false;
            }
        }
        QueueRedraw();
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
    private void Refresh() => QueueRedraw();

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
            text += $"\n{p.Name} · {p.Role}\n生命 {p.Unit.HP}/{p.Unit.Max_HP}  护盾 {p.Unit.Shield}\n攻击 {p.Unit.Attack}  防御 {p.Unit.Defend}";
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
                if (p.UnitId == activeAttack.SourceUnitId && activeAttack.Mode is not WeaponAttackMode.RangedLine and not WeaponAttackMode.ThrowSingle)
                    center += direction * (Mathf.Sin(Mathf.Pi * Math.Min(attackT, .55f) / .55f) * 12f);
                if (p.UnitId == activeAttack.TargetUnitId && attackT is > .44f and < .78f)
                    center += direction * (Mathf.Sin((attackT - .44f) / .34f * Mathf.Pi) * 7f);
            }
            if (!new Rect2(-60, -60, Size.X + 120, Size.Y + 120).HasPoint(center)) continue;
            Color color = p.Role == BattlefieldRole.Player ? new Color("69bec9") : new Color("d88885");
            DrawCircle(center + new Vector2(0, -7), 16, color);
            CenterText(center + new Vector2(0, -27), p.Name, 14, Colors.White);
            string label = p.Role == BattlefieldRole.Player ? (Session.PlayerIds.IndexOf(p.UnitId) + 1).ToString() : "敌";
            CenterText(center + new Vector2(0, -1), label, 15, new Color("16202a"));
            var shownStats = presentationStats.TryGetValue(p.UnitId, out var delayed) ? delayed : (p.Unit.HP, p.Unit.Shield);
            CenterText(center + new Vector2(0, 15), $"HP {shownStats.Item1}", 12, color);
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
            CenterText(center + new Vector2(0, 42), p.Unit.States.Count == 0 ? "无状态" :
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
        bool isThrow = activeAttack.Mode == WeaponAttackMode.ThrowSingle;
        bool ranged = activeAttack.Mode == WeaponAttackMode.RangedLine;
        if (isThrow)
        {
            // Throwing is a true screen-space parabola, not a straight projectile line.
            float travel = Mathf.Clamp(t / .62f, 0f, 1f);
            float height = Math.Max(28f, from.DistanceTo(to) * .22f);
            Vector2 last = from;
            for (int i = 1; i <= 16; i++)
            {
                float u = travel * i / 16f;
                Vector2 point = from.Lerp(to, u) + Vector2.Up * (Mathf.Sin(Mathf.Pi * u) * height);
                DrawLine(last, point, color.Darkened(.32f), 2f, true);
                last = point;
            }
            Vector2 projectile = from.Lerp(to, travel) + Vector2.Up * (Mathf.Sin(Mathf.Pi * travel) * height);
            DrawCircle(projectile, 8, color);
        }
        else if (ranged)
        {
            Vector2 projectile = from.Lerp(to, Math.Min(1f, t * 1.7f));
            DrawLine(from, projectile, color.Darkened(.25f), 2.5f, true);
            DrawCircle(projectile, 7, color);
        }
        else
        {
            Vector2 direction = (to - from).Normalized();
            Vector2 normal = new Vector2(-direction.Y, direction.X);
            float strike = Mathf.Clamp((t - .22f) / .30f, 0f, 1f);
            Vector2 slashCenter = from.Lerp(to, strike * .72f);
            float angle = direction.Angle();
            DrawArc(from + direction * 15f, 30f, angle - 1.15f, angle + 1.15f, 18, color, 3.5f, true);
            if (t is > .20f and < .68f)
                DrawLine(slashCenter - normal * 20f, slashCenter + normal * 20f, color, 4f, true);
        }
        float impactStart = isThrow ? .62f : .48f;
        if (t > impactStart)
        {
            float alpha = 1f - (t - impactStart) / (1f - impactStart);
            DrawCircle(to, 20 + t * 14, new Color(color, .24f * alpha));
            DrawLine(to + new Vector2(-13, -13), to + new Vector2(13, 13), color, 3f, true);
            DrawLine(to + new Vector2(13, -13), to + new Vector2(-13, 13), color, 3f, true);
            string damage = activeAttack.ShieldAbsorbed > 0
                ? $"-{activeAttack.Damage} 盾-{activeAttack.ShieldAbsorbed}" : $"-{activeAttack.Damage}";
            CenterText(to + new Vector2(0, -48 - t * 18), activeAttack.Defeated ? damage + " 击败" : damage, 15, color);
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

    private static StateDefinition GetStateDefinition(CardSimulator.StateType type) =>
        LoadingSystem.StateDictionary.TryGetValue(type, out var definition) && !string.IsNullOrEmpty(definition.Name)
            ? definition : new StateDefinition(type, type.ToString(), false);
}
