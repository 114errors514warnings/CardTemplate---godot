using Godot;
using System;
using System.Linq;
using CardSimulator.Battlefield;

/// <summary>Foundation-stage playable map. Explicitly separate from the saved adventure.</summary>
public partial class HexBattleScene : Control
{
    public BattlefieldSession Session { get; private set; }
    public BattlefieldView MapView { get; private set; }
    private Label resources;
    private Label message;
    private Label title;
    private Label currentItems;
    private Button moveButton;
    private readonly Button[] tabs = new Button[3];
    private PanelContainer tooltip;
    private Label tooltipText;
    private PanelContainer pausePanel;
    private Control pauseShade;
    private int equipmentBonus;

    public override void _Ready()
    {
        BuildUi();
        try
        {
            LoadingSystem.EnsureAllDataLoaded();
            string path = LoadingSystem.GetFilePathByKey("Data.Battlefield.Foundation");
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file == null) throw new InvalidOperationException($"无法打开战场 JSON：{path}");
            Session = new BattlefieldSession(BattleMapDefinition.Parse(file.GetAsText()));
            Session.Changed += RefreshHud;
            Session.Message += ShowMessage;
            MapView.Bind(Session);
            MapView.HoverDetails += ShowTooltip;
            MapView.Message += ShowMessage;
            RefreshHud();
            ShowMessage("右键拖动地图；点击角色或角色 Tab 切换；点击移动后选择相邻格。Esc 取消/暂停。");
            if (OS.GetCmdlineUserArgs().Contains("--battlefield-smoke"))
                CallDeferred(nameof(RunSmoke));
        }
        catch (Exception ex)
        {
            ShowMessage("战场初始化失败：" + ex.Message);
            GD.PrintErr(ex);
            if (OS.GetCmdlineUserArgs().Contains("--battlefield-smoke")) GetTree().Quit(1);
        }
    }

    private void BuildUi()
    {
        var background = new ColorRect { Color = new Color("101820"), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);
        var margin = new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 14);
        AddChild(margin);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); margin.AddChild(column);
        var header = new HBoxContainer(); column.AddChild(header);
        title = new Label { Text = "六边形战场 · 基础验证", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 22); header.AddChild(title);
        AddButton(header, "定位当前角色", () => MapView.CenterSelected());
        AddButton(header, "暂停", () => SetPaused(true));
        AddButton(header, "返回主菜单", ReturnToMenu);

        MapView = new BattlefieldView { CustomMinimumSize = new Vector2(0, 220), SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(MapView);
        var tabRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; column.AddChild(tabRow);
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            tabs[i] = AddButton(tabRow, $"角色 {i + 1}", () =>
            {
                if (Session == null) return;
                MapView.SetMoving(false); equipmentBonus = 0;
                Session.Select(Session.PlayerIds[index]); MapView.CenterSelected();
            });
            tabs[i].CustomMinimumSize = new Vector2(230, 38);
        }
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); column.AddChild(row);
        AddEquipmentPanel(row, "左手位");
        var middle = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddChild(middle);
        resources = new Label(); resources.AddThemeFontSizeOverride("font_size", 22); middle.AddChild(resources);
        currentItems = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 48) };
        middle.AddChild(currentItems);
        var actionRow = new HBoxContainer(); middle.AddChild(actionRow);
        moveButton = AddButton(actionRow, "移动（1 能量）", () =>
        {
            if (Session == null) return;
            MapView.SetMoving(!MapView.Moving);
            ShowMessage(MapView.Moving ? "选择绿色相邻格，右键仍可拖图；Esc 取消。" : "已取消移动。");
        });
        AddButton(actionRow, "下一验证回合", () => { MapView.SetMoving(false); Session?.NextTestRound(); });
        AddButton(actionRow, "测试装备：移动 +2", () =>
        { equipmentBonus = 2; Session?.SetTestEquipmentBonus(equipmentBonus); });
        AddButton(actionRow, "卸下测试装备", () =>
        { equipmentBonus = 0; Session?.SetTestEquipmentBonus(equipmentBonus); });
        var scope = new Label { Text = "本批：地图 / 部署 / 占格 / 移动验证。攻击、手牌与物件使用将在后续批次接入。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart };
        scope.AddThemeColorOverride("font_color", new Color("a0afbe")); middle.AddChild(scope);
        AddEquipmentPanel(row, "右手位");
        message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 36) };
        message.AddThemeColorOverride("font_color", new Color("eed9aa")); column.AddChild(message);

        tooltip = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 20 };
        tooltipText = new Label { MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(300, 0) };
        tooltip.AddChild(tooltipText); AddChild(tooltip);
        pauseShade = new ColorRect { Color = new Color(0, 0, 0, .65f), Visible = false, ProcessMode = ProcessModeEnum.Always, ZIndex = 30 };
        pauseShade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(pauseShade);
        var pauseCenter = new CenterContainer { ProcessMode = ProcessModeEnum.Always };
        pauseCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); pauseShade.AddChild(pauseCenter);
        pausePanel = new PanelContainer { CustomMinimumSize = new Vector2(320, 230) }; pauseCenter.AddChild(pausePanel);
        var pauseButtons = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; pausePanel.AddChild(pauseButtons);
        pauseButtons.AddChild(new Label { Text = "已暂停", HorizontalAlignment = HorizontalAlignment.Center });
        AddButton(pauseButtons, "继续", () => SetPaused(false));
        AddButton(pauseButtons, "返回主菜单", ReturnToMenu);
        AddButton(pauseButtons, "退出游戏", () => { GetTree().Paused = false; GetTree().Quit(); });
    }

    private static void AddEquipmentPanel(HBoxContainer row, string text)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(140, 170) }; row.AddChild(panel);
        panel.AddChild(new Label { Text = text + "\n\n空\n\n装备系统待接入", HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center });
    }
    private static Button AddButton(Node parent, string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38) };
        button.Pressed += pressed; parent.AddChild(button); return button;
    }
    private void RefreshHud()
    {
        if (Session == null) return;
        var p = Session.Selected;
        title.Text = $"六边形战场 · 基础验证    回合 {Session.Round}    种子 {Session.Generated.Seed}";
        resources.Text = $"{p.Name}   能量 {p.Unit.Energy}   移动剩余 {p.RemainingMoves}/{p.EffectiveMovesPerTurn}（已用 {p.MovesUsedThisTurn}）   坐标 ({p.Coord.Q},{p.Coord.R})";
        for (int i = 0; i < 3; i++)
        {
            var actor = Session.Occupancy.Placements[Session.PlayerIds[i]];
            tabs[i].Text = $"{(actor.UnitId == p.UnitId ? "▶ " : "")}{i + 1} · {actor.Name}   HP {actor.Unit.HP}";
            tabs[i].Disabled = actor.Presence != BattlefieldPresence.Active;
        }
        var items = Session.Board.Cells[p.Coord].Items;
        currentItems.Text = items.Count == 0 ? "当前格物品：无" : "当前格物品：" + string.Join("、", items.Select(x => $"{x.DefinitionId}（{x.Kind}）"));
        moveButton.Disabled = p.RemainingMoves == 0 || p.Unit.Energy < 1 || p.Presence != BattlefieldPresence.Active;
        if (moveButton.Disabled) MapView.SetMoving(false);
    }
    private void ShowMessage(string text) => message.Text = text;
    private void ShowTooltip(string text, Vector2 screenPosition)
    {
        tooltip.Visible = text.Length > 0 && !pauseShade.Visible;
        tooltipText.Text = text; tooltip.ResetSize();
        tooltip.Position = new Vector2(Math.Clamp(screenPosition.X + 16, 0, Math.Max(0, Size.X - 330)),
            Math.Clamp(screenPosition.Y + 16, 0, Math.Max(0, Size.Y - Math.Max(tooltip.Size.Y, 200))));
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            if (MapView.Moving) { MapView.SetMoving(false); ShowMessage("已取消移动。"); }
            else SetPaused(true);
            GetViewport().SetInputAsHandled();
        }
    }
    private void SetPaused(bool paused)
    { tooltip.Visible = false; pauseShade.Visible = paused; GetTree().Paused = paused; }
    private void ReturnToMenu()
    { GetTree().Paused = false; GetTree().ChangeSceneToFile("res://Scenes/MainMenu/MainMenuScene.tscn"); }
    public override void _ExitTree()
    {
        if (GetTree() != null) GetTree().Paused = false;
        if (Session == null) return;
        Session.Changed -= RefreshHud; Session.Message -= ShowMessage;
        MapView.HoverDetails -= ShowTooltip; MapView.Message -= ShowMessage;
        Session.Dispose();
    }

    // Engine-driven smoke checks are implemented in a separate file/class, outside the xUnit runner.
    private void RunSmoke() => BattlefieldSceneSmoke.Run(this);
}
