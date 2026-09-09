using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator.Battlefield;

/// <summary>Playable spatial battle: player cards/movement, enemy phase, equipment/items and result overlay.</summary>
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
    private int pendingCardId;
    private AxialHex? lastCastHover;
    private bool movePlanning;
    private HBoxContainer handRow;
    private Label pileLabel;
    private bool victoryShown;
    private Control resultShade;
    private Label resultTitle;
    private Button leftHandButton;
    private Button rightHandButton;
    private readonly Button[] itemButtons = new Button[3];

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
            Session.Finished += ShowResult;
            MapView.Bind(Session);
            MapView.HoverDetails += ShowTooltip;
            MapView.Message += ShowMessage;
            MapView.LeftClickOverride = OnMapClick;
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
        title = new Label { Text = "六边形战斗", SizeFlagsHorizontal = SizeFlags.ExpandFill };
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
                CancelPendingCast();
                MapView.SetMoving(false);
                Session.Select(Session.PlayerIds[index]); MapView.CenterSelected();
            });
            tabs[i].CustomMinimumSize = new Vector2(230, 38);
        }
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); column.AddChild(row);
        leftHandButton = AddEquipmentButton(row, "左手位", BattlefieldSession.HandSlot.Left);
        var middle = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddChild(middle);
        resources = new Label(); resources.AddThemeFontSizeOverride("font_size", 22); middle.AddChild(resources);
        currentItems = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 48) };
        middle.AddChild(currentItems);
        var itemRow = new HBoxContainer(); itemRow.AddThemeConstantOverride("separation", 8); middle.AddChild(itemRow);
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            itemButtons[i] = AddButton(itemRow, $"道具 {i + 1}：空", () => HandleItemSlot(slot));
            itemButtons[i].SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }
        var actionRow = new HBoxContainer(); middle.AddChild(actionRow);
        moveButton = AddButton(actionRow, "移动（1 能量）", () =>
        {
            if (Session == null) return;
            movePlanning = !movePlanning;
            CancelPendingCast();
            MapView.SetMoving(movePlanning);
            moveButton.Text = movePlanning ? "取消移动" : "移动（1 能量）";
            ShowMessage(movePlanning ? "选择一个绿色相邻格：移动一格消耗 1 能量和 1 次。" : "已取消移动。");
        });
        rightHandButton = AddEquipmentButton(row, "右手位", BattlefieldSession.HandSlot.Right);

        // 手牌区：牌堆计数、空间出牌和玩家/怪物回合入口。
        var handArea = new VBoxContainer(); handArea.AddThemeConstantOverride("separation", 4); column.AddChild(handArea);
        var handBar = new HBoxContainer(); handBar.AddThemeConstantOverride("separation", 10); handArea.AddChild(handBar);
        pileLabel = new Label { Text = "手牌：0", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pileLabel.AddThemeFontSizeOverride("font_size", 18); handBar.AddChild(pileLabel);
        AddButton(handBar, "结束回合", () =>
        {
            if (Session == null) return;
            CancelPendingCast();
            MapView.SetMoving(false);
            Session.EndCurrentTurn();
        });
        var handScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, 44),
        };
        handArea.AddChild(handScroll);
        handRow = new HBoxContainer(); handRow.AddThemeConstantOverride("separation", 8); handScroll.AddChild(handRow);

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

        resultShade = new ColorRect { Color = new Color(0, 0, 0, .72f), Visible = false, ZIndex = 40 };
        resultShade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(resultShade);
        var resultCenter = new CenterContainer(); resultCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); resultShade.AddChild(resultCenter);
        var resultPanel = new PanelContainer { CustomMinimumSize = new Vector2(460, 280) }; resultCenter.AddChild(resultPanel);
        var resultBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; resultBox.AddThemeConstantOverride("separation", 16); resultPanel.AddChild(resultBox);
        resultTitle = new Label { HorizontalAlignment = HorizontalAlignment.Center }; resultTitle.AddThemeFontSizeOverride("font_size", 34); resultBox.AddChild(resultTitle);
        AddButton(resultBox, "重新开始本场", () => GetTree().ReloadCurrentScene());
        AddButton(resultBox, "返回主菜单", ReturnToMenu);
    }

    private Button AddEquipmentButton(HBoxContainer row, string text, BattlefieldSession.HandSlot hand)
    {
        var button = new Button { Text = text + "\n\n空", CustomMinimumSize = new Vector2(140, 170) };
        button.Pressed += () => HandleHand(hand); row.AddChild(button); return button;
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
        title.Text = $"六边形战斗    {FormatPhase(Session.Phase)}    回合 {Session.Round}    种子 {Session.Generated.Seed}";
        resources.Text = $"{p.Name}   能量 {p.Unit.Energy}   移动剩余 {p.RemainingMoves}/{p.EffectiveMovesPerTurn}（已用 {p.MovesUsedThisTurn}）   坐标 ({p.Coord.Q},{p.Coord.R})";
        for (int i = 0; i < 3; i++)
        {
            var actor = Session.Occupancy.Placements[Session.PlayerIds[i]];
            tabs[i].Text = $"{(actor.UnitId == p.UnitId ? "▶ " : "")}{i + 1} · {actor.Name}   HP {actor.Unit.HP}";
            tabs[i].Disabled = actor.Presence != BattlefieldPresence.Active;
        }
        var items = Session.Board.Cells[p.Coord].Items;
        currentItems.Text = items.Count == 0 ? "当前格物品：无" : "当前格物品：" + string.Join("、", items.Select(x => $"{x.DefinitionId}（{x.Kind}）"));
        var loadout = Session.SelectedLoadout;
        leftHandButton.Text = FormatHand("左手位", loadout.LeftHand, Session.SelectedHand == BattlefieldSession.HandSlot.Left);
        rightHandButton.Text = FormatHand("右手位", loadout.RightHand, Session.SelectedHand == BattlefieldSession.HandSlot.Right);
        for (int i = 0; i < 3; i++) itemButtons[i].Text = loadout.Items[i] == null ? $"道具 {i + 1}：空" : $"道具 {i + 1}：{loadout.Items[i].DefinitionId}\n点击使用";
        moveButton.Disabled = p.RemainingMoves == 0 || p.Unit.Energy < 1 || p.Presence != BattlefieldPresence.Active;
        if (moveButton.Disabled) MapView.SetMoving(false);
        RefreshHand();
        moveButton.Disabled = Session.Phase != BattlefieldSession.BattlePhase.Player || Session.HasPendingHandChoice ||
            p.RemainingMoves == 0 || p.Unit.Energy < 1 || p.Presence != BattlefieldPresence.Active;
    }

    private static string FormatPhase(BattlefieldSession.BattlePhase phase) => phase switch
    {
        BattlefieldSession.BattlePhase.Player => "玩家回合",
        BattlefieldSession.BattlePhase.Monsters => "怪物回合",
        BattlefieldSession.BattlePhase.Victory => "胜利",
        _ => "失败",
    };

    private static string FormatHand(string label, GroundObject equipment, bool selected) =>
        $"{(selected ? "▶ " : "")}{label}\n\n{(equipment == null ? "空\n点击选手/拾取" : equipment.DefinitionId + $"\n距离 {equipment.AttackRange}\n移动 {equipment.MoveBonus:+#;-#;0}")}";

    private void HandleHand(BattlefieldSession.HandSlot hand)
    {
        if (Session == null || Session.Phase != BattlefieldSession.BattlePhase.Player) return;
        Session.SelectHand(hand);
        GroundObject equipment = Session.Board.Cells[Session.Selected.Coord].Items.FirstOrDefault(x => x.Kind == GroundObjectKind.Equipment);
        if (equipment != null)
        {
            Session.TryEquipFromCurrentCell(equipment.InstanceId, hand, out string error);
            if (error.Length > 0) ShowMessage(error);
        }
        else ShowMessage(hand == BattlefieldSession.HandSlot.Left ? "本次攻击使用左手。" : "本次攻击使用右手。");
    }

    private void HandleItemSlot(int slot)
    {
        if (Session == null || Session.Phase != BattlefieldSession.BattlePhase.Player) return;
        if (Session.SelectedLoadout.Items[slot] != null)
        {
            Session.TryUseItem(slot, out string error);
            if (error.Length > 0) ShowMessage(error);
            return;
        }
        GroundObject item = Session.Board.Cells[Session.Selected.Coord].Items.FirstOrDefault(x => x.Kind == GroundObjectKind.Item);
        if (item == null) { ShowMessage("当前格没有可拾取道具。"); return; }
        Session.TryPickItemFromCurrentCell(item.InstanceId, slot, out string pickError);
        ShowMessage(pickError.Length == 0 ? $"已将 {item.DefinitionId} 放入道具栏 {slot + 1}。" : pickError);
    }

    /// <summary>出牌/移动规划待命时吞掉地图左键按下，避免误切人；实际动作由松开/确认按钮统一触发。</summary>
    private bool OnMapClick(AxialHex coord)
    {
        if (pendingCardId > 0) return TryCastPendingTo(coord);
        if (!movePlanning) return false;
        bool ok = Session.Movement.TryMove(Session.SelectedId, coord, out string error);
        ShowMessage(ok ? "移动成功：消耗 1 能量和 1 次移动。" : "移动失败：" + error);
        movePlanning = false; MapView.SetMoving(false); moveButton.Text = "移动（1 能量）";
        return true;
    }

    private void RefreshHand()
    {
        if (Session == null || handRow == null) return;
        foreach (Node child in handRow.GetChildren()) child.QueueFree();

        BattleUnitPlacement p = Session.Selected;
        pileLabel.Text = $"手牌 {Session.HandCount(p.UnitId)} / 抽 {Session.DrawPileCount(p.UnitId)} / 弃 {Session.DiscardPileCount(p.UnitId)}";
        foreach (Card card in Session.GetHand(p.UnitId))
        {
            Button cardButton = new Button
            {
                Text = card == null ? "?" : $"{card.CardName}（{card.EnergyCost}）",
                CustomMinimumSize = new Vector2(150, 40),
            };
            cardButton.AddThemeFontSizeOverride("font_size", 14);
            cardButton.Pressed += () =>
            {
                if (Session.HasPendingHandChoice)
                {
                    Session.TryChooseHandCard(card, out string selectionMessage);
                    ShowMessage(selectionMessage);
                }
                else StartCastCard(card);
            };
            handRow.AddChild(cardButton);
        }
    }
    private void ShowMessage(string text) => message.Text = text;
    private void ShowTooltip(string text, Vector2 screenPosition)
    {
        tooltip.Visible = text.Length > 0 && !pauseShade.Visible;
        tooltipText.Text = text; tooltip.ResetSize();
        tooltip.Position = new Vector2(Math.Clamp(screenPosition.X + 16, 0, Math.Max(0, Size.X - 330)),
            Math.Clamp(screenPosition.Y + 16, 0, Math.Max(0, Size.Y - Math.Max(tooltip.Size.Y, 200))));
    }
    private void StartCastCard(Card card)
    {
        if (card == null || Session == null) return;
        pendingCardId = card.CardId;
        lastCastHover = null;
        MapView.SetMoving(false);
        CardSpatialSpec spec = Session.GetSpatialSpec(card.CardId);
        if (spec.Shape == CardSpatialShape.None)
        {
            TryCastPendingTo(Session.Selected.Coord);
            return;
        }
        ShowMessage($"为「{card.CardName}」选择目标格；右键可拖图，Esc 取消。");
        UpdateCastPreview(null);
    }

    private void CancelPendingCast()
    {
        if (pendingCardId <= 0) return;
        pendingCardId = 0;
        lastCastHover = null;
        MapView.ClearCastPreview();
        ShowMessage("已取消出牌。");
    }

    private AxialHex? CurrentHoveredCell()
    {
        if (MapView == null) return null;
        Vector2 mouse = GetViewport().GetMousePosition();
        if (!MapView.GetGlobalRect().HasPoint(mouse)) return null;
        return MapView.CellAt(mouse - MapView.GlobalPosition);
    }

    private void UpdateCastPreview(AxialHex? hover)
    {
        lastCastHover = hover;
        if (Session == null || pendingCardId <= 0) return;
        var spec = Session.GetSpatialSpec(pendingCardId);
        AxialHex origin = Session.Selected.Coord;
        var candidates = new HashSet<AxialHex>();
        var affected = new HashSet<AxialHex>();

        switch (spec.Shape)
        {
            case CardSpatialShape.Single:
            case CardSpatialShape.Burst:
            case CardSpatialShape.Line:
                foreach (var cell in Session.GetCastCandidates(pendingCardId)) candidates.Add(cell);
                if (hover.HasValue && candidates.Contains(hover.Value))
                    affected = new HashSet<AxialHex>(Session.GetAffectedCells(pendingCardId, hover.Value));
                break;

            case CardSpatialShape.SelfMove:
                foreach (var cell in Session.GetCastCandidates(pendingCardId)) candidates.Add(cell);
                if (hover.HasValue && candidates.Contains(hover.Value)) affected.Add(hover.Value);
                break;

            case CardSpatialShape.Trap:
                foreach (var cell in Session.GetCastCandidates(pendingCardId)) candidates.Add(cell);
                if (hover.HasValue && candidates.Contains(hover.Value)) affected.Add(hover.Value);
                break;

            default:
                // 无空间形状：自目标卡（抽牌/状态/护盾），无需范围预览
                break;
        }

        MapView.SetCastPreview(candidates, affected, origin, hover);
    }

    private bool TryCastPendingTo(AxialHex? hover)
    {
        if (Session == null || pendingCardId <= 0) return false;
        AxialHex target = hover.HasValue ? hover.Value : Session.Selected.Coord;
        bool ok = Session.TryCastCard(pendingCardId, target, out string error);
        ShowMessage(ok ? "出牌成功。" : "出牌失败：" + error);
        pendingCardId = 0;
        lastCastHover = null;
        MapView.ClearCastPreview();
        return true;
    }

    private void EndMovePlanning(bool showCancelMessage = true)
    {
        movePlanning = false;
        MapView.SetMoving(false);
        MapView.ClearMovePath();
        if (moveButton != null) moveButton.Text = "移动（1 能量）";
        if (showCancelMessage) ShowMessage("已取消移动。");
    }

    private void ShowResult(BattlefieldSession.BattlePhase outcome)
    {
        victoryShown = outcome == BattlefieldSession.BattlePhase.Victory;
        CancelPendingCast(); EndMovePlanning(false);
        resultTitle.Text = victoryShown ? "战斗胜利" : "战斗失败";
        resultTitle.AddThemeColorOverride("font_color", victoryShown ? Colors.LightGreen : Colors.IndianRed);
        resultShade.Visible = true;
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            if (pendingCardId > 0) CancelPendingCast();
            else if (movePlanning) EndMovePlanning();
            else if (MapView.Moving) { MapView.SetMoving(false); ShowMessage("已取消移动。"); }
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
        Session.Finished -= ShowResult;
        MapView.HoverDetails -= ShowTooltip; MapView.Message -= ShowMessage;
        Session.Dispose();
    }

    // Engine-driven smoke checks are implemented in a separate file/class, outside the xUnit runner.
    private void RunSmoke() => BattlefieldSceneSmoke.Run(this);
}
