using Godot;
using System;
using System.Linq;
using CardSimulator.Battlefield;

/// <summary>Fixed-scale battlefield canvas. HUD is outside this clipped Control.</summary>
public partial class BattlefieldView : Control
{
    public BattlefieldSession Session { get; private set; }
    public Vector2 Pan { get; private set; }
    public bool Moving { get; private set; }
    private bool dragging;
    private AxialHex? hover;
    public event Action<string, Vector2> HoverDetails;
    public event Action<string> Message;

    public override void _Ready()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop;
        MouseExited += () => { hover = null; HoverDetails?.Invoke("", Vector2.Zero); QueueRedraw(); };
        Resized += () => { ClampPan(); QueueRedraw(); };
    }

    public void Bind(BattlefieldSession session)
    {
        if (Session != null) Session.Changed -= Refresh;
        Session = session; Session.Changed += Refresh;
        CenterSelected();
    }
    public override void _ExitTree()
    { if (Session != null) Session.Changed -= Refresh; }

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
                var coord = CellAt(button.Position); var target = Session.Occupancy.At(coord);
                if (target?.Role == BattlefieldRole.Player)
                { SetMoving(false); Session.Select(target.UnitId); }
                else if (Moving)
                {
                    if (Session.Movement.TryMove(Session.SelectedId, coord, out string error)) SetMoving(false);
                    else Message?.Invoke(error);
                }
                AcceptEvent();
            }
        }
        if (input is InputEventMouseMotion motion)
        {
            if (dragging && motion.ButtonMask.HasFlag(MouseButtonMask.Right))
            {
                Pan += motion.Relative; ClampPan(); HoverDetails?.Invoke("", Vector2.Zero); hover = null; QueueRedraw(); return;
            }
            dragging = false;
            hover = CellAt(motion.Position);
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
        var legal = Session.Movement.LegalDestinations(Session.SelectedId);
        foreach (var cell in Session.Board.Cells.Values)
        {
            var center = CellPosition(cell.Coord);
            if (!new Rect2(-r * 2, -r * 2, Size.X + r * 4, Size.Y + r * 4).HasPoint(center)) continue;
            bool available = Moving && legal.Contains(cell.Coord);
            Color fill = cell.Surface == BattleSurface.Pit ? new Color("070c12") : cell.Kind == BattleCellKind.Obstacle ?
                new Color("515464") : available ? new Color("244e46") : new Color("25323d");
            var points = Enumerable.Range(0, 6).Select(i => center + Vector2.FromAngle(Mathf.DegToRad(60 * i - 30)) * r).ToArray();
            DrawColoredPolygon(points, fill);
            Color border = available ? new Color("6cbf9f") : new Color("40525f");
            if (cell.Coord == Session.Selected.Coord) border = new Color("f5d98c");
            for (int i = 0; i < 6; i++) DrawLine(points[i], points[(i + 1) % 6], border, available || cell.Coord == Session.Selected.Coord ? 2.5f : 1, true);
            if (cell.Kind == BattleCellKind.Obstacle) CenterText(center + new Vector2(0, 5), "障碍", 14, new Color("b4b7c0"));
            if (cell.Items.Count > 0) CenterText(center + new Vector2(0, 29), $"物品 ×{cell.Items.Count}", 12, new Color("e6bd78"));
            if (cell.Trigger != null) CenterText(center + new Vector2(0, 28), cell.Trigger.Kind == GroundObjectKind.Trap ? "陷阱" : "机关", 12, Colors.Orange);
        }
        foreach (var p in Session.Occupancy.Placements.Values.Where(x => x.Presence == BattlefieldPresence.Active))
        {
            var center = CellPosition(p.Coord);
            if (!new Rect2(-60, -60, Size.X + 120, Size.Y + 120).HasPoint(center)) continue;
            Color color = p.Role == BattlefieldRole.Player ? new Color("69bec9") : new Color("d88885");
            DrawCircle(center + new Vector2(0, -7), 16, color);
            CenterText(center + new Vector2(0, -27), p.Name, 14, Colors.White);
            string label = p.Role == BattlefieldRole.Player ? (Session.PlayerIds.IndexOf(p.UnitId) + 1).ToString() : "敌";
            CenterText(center + new Vector2(0, -1), label, 15, new Color("16202a"));
            CenterText(center + new Vector2(0, 15), $"HP {p.Unit.HP}", 12, color);
            CenterText(center + new Vector2(0, 42), p.Unit.States.Count == 0 ? "无状态" :
                string.Join(" ", p.Unit.States.Take(3).Select(x => $"{GetStateDefinition(x.Key).Name[..1]}{x.Value.Stacks}")), 12, new Color("b7c5ce"));
        }
        if (hover.HasValue && Moving && legal.Contains(hover.Value))
            DrawLine(CellPosition(Session.Selected.Coord), CellPosition(hover.Value), Colors.LightGreen, 3, true);
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
