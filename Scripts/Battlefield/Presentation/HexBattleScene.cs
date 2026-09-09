using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;
using CardSimulator.Battlefield;

/// <summary>Playable spatial battle: player cards/movement, enemy phase, equipment/items and result overlay.</summary>
public partial class HexBattleScene : Control
{
    public BattlefieldSession Session { get; private set; }
    public BattlefieldView MapView { get; private set; }
    private Label resources;
    private Label title;
    private Button moveButton;
    private readonly Button[] tabs = new Button[3];
    private PanelContainer tooltip;
    private Label tooltipText;
    private PanelContainer pausePanel;
    private Control pauseShade;
    private int pendingCardId;
    private AxialHex? lastCastHover;
    private bool movePlanning;
    private Control handRow;
    private Label moveInfo;
    private Button drawPileButton;
    private Button discardPileButton;
    private Button exhaustPileButton;
    private Control pileOverlay;
    private Label pileTitle;
    private GridContainer pileList;
    private Label pileDetail;
    private readonly ColorRect[] energyPips = new ColorRect[3];
    private bool victoryShown;
    private Control resultShade;
    private Label resultTitle;
    private Button leftHandButton;
    private Button rightHandButton;

    // ── 出牌拖拽状态 ──
    private Card draggedCard;
    private Control draggedCardNode;
    private int draggedCardOriginalIndex;
    private bool dragExitedHandArea;
    private Control handAreaNode;
    private Control dragLayer;
    private Line2D dragLine;
    private readonly Dictionary<ulong, Card> handCardMap = new();
    private readonly Dictionary<ulong, Vector2> handCardBasePositions = new();
    private readonly Dictionary<ulong, Tween> handHoverTweens = new();
    private Control hoveredHandCard;
    // ── 道具拖拽状态 ──
    private GroundObject draggedItem;
    private Control draggedItemNode;
    private bool draggedItemFromCell;
    private int draggedItemSlot = -1;
    private bool itemDragExited;
    // ── 道具 UI ──
    private PanelContainer curItemPanel;
    private HBoxContainer curItemHost;
    private PanelContainer carryPanelNode;
    private readonly PanelContainer[] itemSlotHosts = new PanelContainer[3];
    private readonly StyleBoxFlat[] itemSlotDefaultBox = new StyleBoxFlat[3];
    private readonly StyleBoxFlat[] itemSlotHoverBox = new StyleBoxFlat[3];
    private StyleBoxFlat curItemDefaultBox;
    private StyleBoxFlat curItemHoverBox;
    private readonly Dictionary<ulong, GroundObject> itemNodeMap = new();
    // —— 调试面板（EnableDebugPanel 为真时才创建）——
    private HexBattleDebugPanel debugPanel;
    [Export] public bool EnableDebugPanel = true;

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
            SetupDebugPanel();
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

    private void SetupDebugPanel()
    {
        if (!EnableDebugPanel) return;
        var packed = (PackedScene)ResourceLoader.Load("res://Scenes/UI/HexBattleDebugPanel.tscn");
        if (packed == null) { GD.PrintErr("HexBattleDebugPanel.tscn 加载失败。"); return; }
        debugPanel = (HexBattleDebugPanel)packed.Instantiate();
        debugPanel.Setup(Session, MapView, ShowMessage);
        AddChild(debugPanel);
        debugPanel.Visible = false;
    }

    private void BuildUi()
    {
        var background = new ColorRect { Color = new Color("101820"), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);

        // 全屏地图层（置于所有 UI 面板之下，中间区域可交互）。
        MapView = new BattlefieldView();
        MapView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(MapView);

        // ── 左上：关卡目标 ──
        var goal = MakePanel();
        Place(goal, 0.02f, 0.02f, 0.17f, 0.22f);
        AddChild(goal);
        var goalBox = new VBoxContainer(); goalBox.AddThemeConstantOverride("separation", 6); goal.AddChild(goalBox);
        goalBox.AddChild(Label("关卡目标", 18, Colors.White));
        goalBox.AddChild(Label("◆ 击败所有敌人", 15, new Color("e8d9a0")));
        title = Label("", 14, new Color("bfc8d0")); goalBox.AddChild(title);

        // ── 右上：定位当前角色 + 暂停 ──
        var topRight = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = MouseFilterEnum.Ignore };
        topRight.AddThemeConstantOverride("separation", 8);
        Place(topRight, 0.78f, 0.02f, 0.985f, 0.075f);
        AddChild(topRight);
        AddButton(topRight, "定位当前角色", () => MapView.CenterSelected()).CustomMinimumSize = new Vector2(120, 0);
        if (EnableDebugPanel) AddButton(topRight, "调试", () => { if (debugPanel != null) debugPanel.ToggleVisible(); }).CustomMinimumSize = new Vector2(88, 0);
        AddButton(topRight, "暂停", () => SetPaused(true)).CustomMinimumSize = new Vector2(88, 0);

        // ── 右侧：当前格道具（可拖拽 prefab）──
        curItemPanel = MakePanel();
        curItemDefaultBox = WellBox(new Color(0.07f, 0.09f, 0.12f, 0.94f), new Color(0.30f, 0.34f, 0.42f), 1, 10);
        curItemHoverBox = WellBox(new Color(0.16f, 0.22f, 0.28f, 0.96f), new Color(0.95f, 0.83f, 0.4f), 2, 10);
        curItemPanel.AddThemeStyleboxOverride("panel", curItemDefaultBox);
        Place(curItemPanel, 0.84f, 0.30f, 0.98f, 0.44f);
        AddChild(curItemPanel);
        var curBox = new VBoxContainer(); curBox.AddThemeConstantOverride("separation", 4); curItemPanel.AddChild(curBox);
        curBox.AddChild(Label("当前格道具", 13, new Color("e8d9a0")));
        curItemHost = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; curItemHost.AddThemeConstantOverride("separation", 6); curBox.AddChild(curItemHost);

        // ── 右侧：随身道具 3 格（可拖拽 prefab）──
        carryPanelNode = MakePanel();
        Place(carryPanelNode, 0.84f, 0.48f, 0.98f, 0.62f);
        AddChild(carryPanelNode);
        var carryBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; carryBox.AddThemeConstantOverride("separation", 4); carryPanelNode.AddChild(carryBox);
        carryBox.AddChild(Label("随身道具", 13, new Color("e8d9a0")));
        var carryRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; carryRow.AddThemeConstantOverride("separation", 8); carryBox.AddChild(carryRow);
        for (int i = 0; i < 3; i++)
        {
            var defaultBox = WellBox(new Color(0.09f, 0.12f, 0.16f, 0.9f), new Color(0.32f, 0.38f, 0.46f), 1, 8);
            var hoverBox = WellBox(new Color(0.20f, 0.28f, 0.34f, 0.95f), new Color(0.95f, 0.83f, 0.4f), 2, 8);
            var slotHost = new PanelContainer { CustomMinimumSize = new Vector2(60, 64), MouseFilter = MouseFilterEnum.Ignore };
            slotHost.AddThemeStyleboxOverride("panel", defaultBox);
            carryRow.AddChild(slotHost);
            itemSlotHosts[i] = slotHost;
            itemSlotDefaultBox[i] = defaultBox;
            itemSlotHoverBox[i] = hoverBox;
        }

        // ── 底部：左手装备（左下）──
        var leftPanel = MakePanel();
        Place(leftPanel, 0.028f, 0.71f, 0.13f, 0.87f);
        AddChild(leftPanel);
        var leftBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; leftBox.AddThemeConstantOverride("separation", 6); leftPanel.AddChild(leftBox);
        leftBox.AddChild(Label("左手", 15, new Color("e8d9a0")));
        leftHandButton = AddSlotButton(BattlefieldSession.HandSlot.Left); leftBox.AddChild(leftHandButton);

        // ── 底部：右手装备（右下）──
        var rightPanel = MakePanel();
        Place(rightPanel, 0.87f, 0.71f, 0.972f, 0.87f);
        AddChild(rightPanel);
        var rightBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; rightBox.AddThemeConstantOverride("separation", 6); rightPanel.AddChild(rightBox);
        rightBox.AddChild(Label("右手", 15, new Color("e8d9a0")));
        rightHandButton = AddSlotButton(BattlefieldSession.HandSlot.Right); rightBox.AddChild(rightHandButton);

        // ── 底部：能量 / 牌堆（手牌左侧）──
        var resPanel = MakePanel();
        Place(resPanel, 0.135f, 0.69f, 0.23f, 0.92f);
        AddChild(resPanel);
        var resBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; resBox.AddThemeConstantOverride("separation", 6); resPanel.AddChild(resBox);
        resources = Label("", 16, Colors.White); resBox.AddChild(resources);
        var pips = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; pips.AddThemeConstantOverride("separation", 4); resBox.AddChild(pips);
        for (int i = 0; i < 3; i++)
        {
            energyPips[i] = new ColorRect { Color = new Color(0.35f, 0.75f, 0.95f), CustomMinimumSize = new Vector2(13, 13) };
            pips.AddChild(energyPips[i]);
        }
        moveInfo = Label("", 12, new Color("c8d0d8")); resBox.AddChild(moveInfo);
        var pileButtons = new VBoxContainer(); pileButtons.AddThemeConstantOverride("separation", 4); resBox.AddChild(pileButtons);
        drawPileButton = MakePileButton("抽牌堆", () => ShowPile("抽牌堆", Session?.GetDrawPile(Session.SelectedId))); pileButtons.AddChild(drawPileButton);
        discardPileButton = MakePileButton("弃牌堆", () => ShowPile("弃牌堆", Session?.GetDiscardPile(Session.SelectedId))); pileButtons.AddChild(discardPileButton);
        exhaustPileButton = MakePileButton("消耗牌", () => { var u = Session?.Selected.Unit; ShowPile("消耗牌", u?.ExhaustPile); }); pileButtons.AddChild(exhaustPileButton);

        // ── 底部中央：角色 Tab + 手牌卡面 ──
        var handPanel = new VBoxContainer(); handPanel.AddThemeConstantOverride("separation", 6);
        // 手牌与左右装备、操作列同处底部带状区域，不侵占中部战场。
        // 适当缩短手牌区（右边界 0.84→0.72），为右侧“结束回合/移动”按钮腾出空间。
        Place(handPanel, 0.23f, 0.65f, 0.72f, 0.89f);
        AddChild(handPanel);
        var tabRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; tabRow.AddThemeConstantOverride("separation", 6); handPanel.AddChild(tabRow);
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
            tabs[i].CustomMinimumSize = new Vector2(130, 28);
        }
        // 手牌区底板：深色半透明圆角面板 + 细描边（参考效果图），衬托居中手牌。
        var handArea = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = false };
        handArea.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.09f, 0.86f),
            BorderColor = new Color(0.78f, 0.70f, 0.48f, 0.60f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            ContentMarginLeft = 4, ContentMarginTop = 12, ContentMarginRight = 4, ContentMarginBottom = 12,
        });
        handPanel.AddChild(handArea);
        handAreaNode = handArea;
        handRow = new Control { MouseFilter = MouseFilterEnum.Ignore, ClipContents = false }; handRow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); handArea.AddChild(handRow);

        // ── 出牌拖拽层：承载拖出的卡牌本体，及卡牌中心→鼠标的引导线 ──
        dragLayer = new Control { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 25 };
        dragLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dragLayer);
        dragLine = new Line2D { Visible = false, Width = 3f, ZIndex = 26 };
        dragLayer.AddChild(dragLine);

        // ── 底部（手牌右侧）：结束回合 / 移动 竖排 ──
        // 两个按钮整体左移（0.845~0.895 → 0.73~0.865），并拓宽，避免与右侧“右手”装备栏重叠。
        var actionCol = new VBoxContainer(); actionCol.AddThemeConstantOverride("separation", 8);
        Place(actionCol, 0.73f, 0.68f, 0.865f, 0.89f);
        AddChild(actionCol);
        AddButton(actionCol, "结束回合", () =>
        {
            if (Session == null) return;
            CancelPendingCast();
            MapView.SetMoving(false);
            Session.EndCurrentTurn();
        }).CustomMinimumSize = new Vector2(0, 44);
        moveButton = AddButton(actionCol, "移动（1 能量）", () =>
        {
            if (Session == null) return;
            movePlanning = !movePlanning;
            CancelPendingCast();
            MapView.SetMoving(movePlanning);
            moveButton.Text = movePlanning ? "取消移动" : "移动（1 能量）";
            ShowMessage(movePlanning ? "选择一个绿色相邻格：移动一格消耗 1 能量和 1 次。" : "已取消移动。");
        });
        moveButton.CustomMinimumSize = new Vector2(0, 44);
        moveButton.SizeFlagsVertical = SizeFlags.ExpandFill;

        // （底部信息栏已移除：ShowMessage 统一走 GD.Print 控制台日志。）

        tooltip = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 20 };
        tooltipText = new Label { MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(300, 0) };
        tooltip.AddChild(tooltipText); AddChild(tooltip);
        pauseShade = new ColorRect { Color = new Color(0, 0, 0, .65f), Visible = false, ProcessMode = ProcessModeEnum.Always, ZIndex = 30 };
        pauseShade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(pauseShade);
        var pauseCenter = new CenterContainer { ProcessMode = ProcessModeEnum.Always };
        pauseCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); pauseShade.AddChild(pauseCenter);
        pausePanel = MakePanel(); pausePanel.CustomMinimumSize = new Vector2(320, 230); pauseCenter.AddChild(pausePanel);
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
        // ── 牌堆弹窗 ──
        var pileShade = new ColorRect { Color = new Color(0, 0, 0, .55f), Visible = false, ZIndex = 35, ProcessMode = ProcessModeEnum.Always };
        pileShade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(pileShade);
        pileOverlay = pileShade;
        var pileCenter = new CenterContainer(); pileCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); pileShade.AddChild(pileCenter);
        var pilePanel = MakePanel(); pilePanel.CustomMinimumSize = new Vector2(1120, 720); pileCenter.AddChild(pilePanel);
        var pileBox = new VBoxContainer(); pileBox.AddThemeConstantOverride("separation", 10); pilePanel.AddChild(pileBox);
        var pileHeader = new HBoxContainer(); pileBox.AddChild(pileHeader);
        pileTitle = new Label { Text = "", SizeFlagsHorizontal = SizeFlags.ExpandFill }; pileTitle.AddThemeFontSizeOverride("font_size", 18); pileHeader.AddChild(pileTitle);
        AddButton(pileHeader, "返回", () => pileShade.Visible = false);
        pileDetail = new Label { Text = "点击卡面查看完整描述", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 58) };
        pileDetail.AddThemeColorOverride("font_color", new Color("d6dce2")); pileBox.AddChild(pileDetail);
        var pileScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 540) }; pileBox.AddChild(pileScroll);
        pileList = new GridContainer { Columns = 3 }; pileList.AddThemeConstantOverride("h_separation", 18); pileList.AddThemeConstantOverride("v_separation", 18); pileScroll.AddChild(pileList);

    }

    private Button AddSlotButton(BattlefieldSession.HandSlot hand)
    {
        var button = new Button { Text = "空", CustomMinimumSize = new Vector2(120, 108), SizeFlagsVertical = SizeFlags.ExpandFill };
        button.Pressed += () => HandleHand(hand);
        return button;
    }
    private static PanelContainer MakePanel()
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.09f, 0.12f, 0.94f),
            BorderColor = new Color(0.30f, 0.34f, 0.42f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 10, ContentMarginTop = 8, ContentMarginRight = 10, ContentMarginBottom = 10,
        });
        return p;
    }
    private static Label Label(string text, int size, Color color)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
    private static StyleBoxFlat WellBox(Color bg, Color border, int bw, int radius)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = bw, BorderWidthTop = bw, BorderWidthRight = bw, BorderWidthBottom = bw,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = 3, ContentMarginTop = 3, ContentMarginRight = 3, ContentMarginBottom = 3,
        };
    }
    private static void Place(Control c, float l, float t, float r, float b)
    {
        c.AnchorLeft = l; c.AnchorTop = t; c.AnchorRight = r; c.AnchorBottom = b;
        c.OffsetLeft = 0; c.OffsetTop = 0; c.OffsetRight = 0; c.OffsetBottom = 0;
    }
    private static void SetDescendantsIgnore(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control c) c.MouseFilter = MouseFilterEnum.Ignore;
            SetDescendantsIgnore(child);
        }
    }
    private static Button MakePileButton(string text, Action onPressed)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        b.Pressed += () => onPressed();
        return b;
    }
    private void ShowPile(string title, IReadOnlyList<Card> cards)
    {
        if (pileOverlay == null) return;
        pileTitle.Text = title + (cards == null ? "" : $"（{cards.Count}）");
        foreach (Node child in pileList.GetChildren()) child.QueueFree();
        pileDetail.Text = "点击卡面查看完整描述";
        if (cards == null || cards.Count == 0)
        {
            pileList.AddChild(Label("（空）", 14, new Color("c8d0d8")));
        }
        else
        {
            foreach (Card c in cards)
            {
                Control cardNode = MakeReadonlyPileCard(c);
                cardNode.ZIndex = 0;
                cardNode.GuiInput += input =>
                {
                    if (input is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
                    {
                        pileDetail.Text = $"{c.CardName}　费用 {c.EnergyCost}　{CardTypeText(c.Category)}\n{c.EffectDescription}";
                        GetViewport().SetInputAsHandled();
                    }
                };
                pileList.AddChild(cardNode);
            }
        }
        pileOverlay.Visible = true;
    }
    private Control MakeHandCard(Card card)
    {
        return MakeBattleCard(card);
    }

    private Control MakeReadonlyPileCard(Card card)
    {
        return MakeBattleCard(card);
    }

    // 六边形战斗专用的小型卡面；不使用旧战斗系统的 CardDisplayPrefab。
    private static Control MakeBattleCard(Card card)
    {
        const int width = 108, height = 156;
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(width, height),
            Size = new Vector2(width, height),
            MouseFilter = MouseFilterEnum.Stop,
            ClipContents = true,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.93f, 0.91f, 0.86f),
            BorderColor = new Color(0.25f, 0.25f, 0.30f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 8, ContentMarginTop = 6, ContentMarginRight = 8, ContentMarginBottom = 8,
        });
        var body = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        body.AddThemeConstantOverride("separation", 3);
        var meta = new HBoxContainer(); body.AddChild(meta);
        var cost = new Label { Text = card.EnergyCost.ToString(), HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(22, 22) };
        cost.AddThemeFontSizeOverride("font_size", 13); cost.AddThemeColorOverride("font_color", Colors.White);
        var costBox = new PanelContainer(); costBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.18f, 0.22f, 0.35f), CornerRadiusTopLeft = 6, CornerRadiusBottomRight = 6 }); costBox.AddChild(cost); meta.AddChild(costBox);
        var type = new Label { Text = CardTypeText(card.Category), HorizontalAlignment = HorizontalAlignment.Right, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        type.AddThemeFontSizeOverride("font_size", 9); type.AddThemeColorOverride("font_color", new Color(0.35f, 0.35f, 0.35f)); meta.AddChild(type);
        var name = new Label { Text = card.CardName, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        name.AddThemeFontSizeOverride("font_size", 12); name.AddThemeColorOverride("font_color", new Color(0.05f, 0.05f, 0.06f)); body.AddChild(name);
        var desc = new RichTextLabel { Text = card.EffectDescription, ScrollActive = false, FitContent = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        desc.AddThemeFontSizeOverride("normal_font_size", 9); desc.AddThemeColorOverride("default_color", new Color(0.12f, 0.12f, 0.14f)); body.AddChild(desc);
        panel.AddChild(body); SetDescendantsIgnore(panel);
        return panel;
    }
    private static string CardTypeText(CardCategory category) => category switch
    {
        CardCategory.Attack => "攻击",
        CardCategory.Skill => "技能",
        CardCategory.State => "状态",
        _ => category.ToString(),
    };
    private static Button AddButton(Node parent, string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38) };
        button.Pressed += pressed; parent.AddChild(button); return button;
    }
    private void RefreshHud()
    {
        if (Session == null) return;
        var p = Session.Selected;
        title.Text = $"{FormatPhase(Session.Phase)} · 回合 {Session.Round}";
        int maxEnergy = Math.Max(p.Unit.Energy, 3);
        resources.Text = $"{p.Name}\n能量 {p.Unit.Energy}/{maxEnergy}";
        moveInfo.Text = $"移动 {p.RemainingMoves}/{p.EffectiveMovesPerTurn}";
        for (int i = 0; i < 3; i++)
            energyPips[i].Modulate = i < Math.Min(p.Unit.Energy, 3) ? new Color(0.35f, 0.75f, 0.95f) : new Color(0.25f, 0.3f, 0.4f);
        if (drawPileButton != null) drawPileButton.Text = $"抽牌堆\n{Session.DrawPileCount(p.UnitId)}";
        if (discardPileButton != null) discardPileButton.Text = $"弃牌堆\n{Session.DiscardPileCount(p.UnitId)}";
        if (exhaustPileButton != null) exhaustPileButton.Text = $"消耗牌\n{p.Unit.ExhaustPile.Count}";
        for (int i = 0; i < 3; i++)
        {
            var actor = Session.Occupancy.Placements[Session.PlayerIds[i]];
            tabs[i].Text = $"{(actor.UnitId == p.UnitId ? "▶ " : "")}{i + 1} · {actor.Name}   HP {actor.Unit.HP}";
            tabs[i].Disabled = actor.Presence != BattlefieldPresence.Active;
        }
        RefreshItems();
        var loadout = Session.SelectedLoadout;
        leftHandButton.Text = FormatHand(loadout.LeftHand, Session.SelectedHand == BattlefieldSession.HandSlot.Left);
        rightHandButton.Text = FormatHand(loadout.RightHand, Session.SelectedHand == BattlefieldSession.HandSlot.Right);
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

    private static string FormatHand(GroundObject equipment, bool selected) =>
        $"{(selected ? "▶ " : "")}{(equipment == null ? "空\n点击选手/拾取" : equipment.DefinitionId + $"\n距离 {equipment.AttackRange}\n移动 {equipment.MoveBonus:+#;-#;0}")}";

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
        if (!movePlanning) return false;
        bool ok = Session.Movement.TryMove(Session.SelectedId, coord, out string error);
        ShowMessage(ok ? "移动成功：消耗 1 能量和 1 次移动。" : "移动失败：" + error);
        movePlanning = false; MapView.SetMoving(false); moveButton.Text = "移动（1 能量）";
        return true;
    }

    private void RefreshHand()
    {
        if (Session == null || handRow == null) return;
        // 初始 _Ready 中 Control 尚未完成容器布局时尺寸约为 (8,24)。
        // 此时计算重叠间距会错误地把所有卡压到一起；等布局完成后只重排一次。
        if (handRow.Size.X < 300f)
        {
            CallDeferred(nameof(RefreshHand));
            return;
        }
        // 拖拽进行中不重建手牌：避免 Notify 刷新打断拖拽、也避免出牌结算时引发递归。
        // 拖拽结束后统一由 CleanupAfterDrag / CancelCardDrag 调用 RefreshHand 重排。
        if (draggedCard != null) return;
        foreach (Node child in handRow.GetChildren()) child.QueueFree();
        handCardMap.Clear();
        handCardBasePositions.Clear();
        foreach (Tween tween in handHoverTweens.Values) tween?.Kill();
        handHoverTweens.Clear();
        hoveredHandCard = null;

        BattleUnitPlacement p = Session.Selected;
        List<Card> cards = Session.GetHand(p.UnitId).Where(card => card != null).Take(10).ToList();
        const float cardWidth = 108f;
        const float cardHeight = 156f;
        float availableWidth = handRow.Size.X > 0 ? handRow.Size.X : GetViewportRect().Size.X * 0.55f;
        const float normalGap = 8f;
        float normalWidth = cards.Count * cardWidth + Math.Max(0, cards.Count - 1) * normalGap;
        float step = cards.Count <= 1 ? 0f : normalWidth <= availableWidth
            ? cardWidth + normalGap
            : Math.Max(38f, (availableWidth - cardWidth) / (cards.Count - 1));
        float totalWidth = cardWidth + Math.Max(0, cards.Count - 1) * step;
        float startX = Math.Max(0, (availableWidth - totalWidth) * 0.5f);
        for (int index = 0; index < cards.Count; index++)
        {
            Card card = cards[index];
            var cardNode = MakeHandCard(card);
            cardNode.MouseEntered += () => SetHoveredHandCard(cardNode);
            cardNode.MouseExited += () =>
            {
                if (hoveredHandCard == cardNode) SetHoveredHandCard(null);
            };
            handCardMap[cardNode.GetInstanceId()] = card;
            handRow.AddChild(cardNode);
            // Control 加入父节点后再写位置，避免 Container 的首次布局把绝对定位重置为 (0,0)。
            cardNode.Position = new Vector2(startX + index * step, 0);
            cardNode.Size = new Vector2(cardWidth, cardHeight);
            cardNode.PivotOffset = new Vector2(cardWidth * 0.5f, cardHeight * 0.5f);
            cardNode.ZIndex = index;
            handCardBasePositions[cardNode.GetInstanceId()] = cardNode.Position;
        }
    }

    private void SetHoveredHandCard(Control hovered)
    {
        if (hoveredHandCard == hovered || draggedCard != null) return;
        hoveredHandCard = hovered;
        foreach (Node child in handRow.GetChildren())
        {
            if (child is not Control card || !handCardBasePositions.TryGetValue(card.GetInstanceId(), out Vector2 basePosition)) continue;
            int index = card.GetIndex();
            card.Position = basePosition;
            card.ZIndex = index;
            AnimateHandCardScale(card, card == hovered ? new Vector2(1.08f, 1.08f) : Vector2.One);
        }
        if (hovered != null && handCardBasePositions.TryGetValue(hovered.GetInstanceId(), out Vector2 selectedBase))
        {
            hovered.Position = selectedBase;
            hovered.ZIndex = 100;
        }
    }

    private void AnimateHandCardScale(Control card, Vector2 targetScale)
    {
        ulong id = card.GetInstanceId();
        if (handHoverTweens.TryGetValue(id, out Tween oldTween)) oldTween?.Kill();
        Tween tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Quad);
        tween.SetEase(Tween.EaseType.Out);
        tween.TweenProperty(card, "scale", targetScale, 0.12f);
        handHoverTweens[id] = tween;
    }
    // 底部信息栏已移除：所有操作反馈统一打印到 Godot 控制台（Output 面板），便于日志排查。
    private void ShowMessage(string text) => GD.Print($"[战场] {text}");
    private void ShowTooltip(string text, Vector2 screenPosition)
    {
        tooltip.Visible = text.Length > 0 && !pauseShade.Visible;
        tooltipText.Text = text; tooltip.ResetSize();
        tooltip.Position = new Vector2(Math.Clamp(screenPosition.X + 16, 0, Math.Max(0, Size.X - 330)),
            Math.Clamp(screenPosition.Y + 16, 0, Math.Max(0, Size.Y - Math.Max(tooltip.Size.Y, 200))));
    }
    private void CancelPendingCast()
    {
        if (draggedItem != null) { CancelItemDrag(); ShowMessage("已取消使用道具。"); return; }
        if (draggedCard != null) { CancelCardDrag(); ShowMessage("已取消出牌。"); return; }
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
        return ok; // 返回真实结果：失败时由调用方 CancelCardDrag 归还手牌。
    }

    // ── 出牌拖拽（对齐旧版战斗系统）：按住卡牌→跟手，拖出手牌区/指向目标格后松开施放 ──
    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
        {
            if (draggedCard == null && draggedItem == null)
            {
                TryStartDragFromPosition(mb.Position);
                if (draggedCard == null) TryStartItemDragFromPosition(mb.Position);
            }
            if (draggedCard != null || draggedItem != null) GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventMouseMotion && (draggedCard != null || draggedItem != null))
        {
            if (draggedCard != null) UpdateDragState();
            else UpdateItemDrag();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventMouseButton mu && mu.ButtonIndex == MouseButton.Left && !mu.Pressed && (draggedCard != null || draggedItem != null))
        {
            if (draggedCard != null) FinishCardDrag();
            else FinishItemDrag();
            GetViewport().SetInputAsHandled();
        }
    }

    private bool CanStartDrag() =>
        Session != null && Session.Phase == BattlefieldSession.BattlePhase.Player && draggedCard == null && handRow != null;

    private void TryStartDragFromPosition(Vector2 position)
    {
        if (!CanStartDrag()) return;
        foreach (Node child in handRow.GetChildren())
        {
            if (child is Control ctrl && ctrl.GetGlobalRect().HasPoint(position) && handCardMap.TryGetValue(ctrl.GetInstanceId(), out Card card))
            {
                if (Session.HasPendingHandChoice)
                {
                    Session.TryChooseHandCard(card, out string selectionMessage);
                    ShowMessage(selectionMessage);
                }
                else StartCardDrag(card, ctrl);
                return;
            }
        }
    }

    private void StartCardDrag(Card card, Control cardNode)
    {
        SetHoveredHandCard(null);
        draggedCard = card;
        draggedCardNode = cardNode;
        draggedCardOriginalIndex = cardNode.GetIndex();
        dragExitedHandArea = false;
        if (cardNode.GetParent() != null) cardNode.GetParent().RemoveChild(cardNode); // Godot 不允许直接 add_child 已有父节点的节点
        dragLayer.AddChild(cardNode);
        Vector2 mouse = GetGlobalMousePosition();
        cardNode.Position = mouse - cardNode.Size / 2;
        cardNode.Modulate = new Color(1f, 1f, 1f, 0.92f);
        pendingCardId = card.CardId;
        lastCastHover = null;
        MapView.ClearCastPreview();
        EndMovePlanning(false);
        CardSpatialSpec spec = Session.GetSpatialSpec(card.CardId);
        ShowMessage(spec.HasSpatial
            ? $"拖出「{card.CardName}」指向目标格，松左键施放；拖回手牌或 Esc 取消。"
            : $"拖出「{card.CardName}」离开手牌区即施放。");
    }

    private void UpdateDragState()
    {
        if (draggedCard == null || draggedCardNode == null || Session == null) return;
        Vector2 mouse = GetGlobalMousePosition();
        dragExitedHandArea = handAreaNode == null ? true : !handAreaNode.GetGlobalRect().HasPoint(mouse);
        bool hasSpatial = Session.GetSpatialSpec(draggedCard.CardId).HasSpatial;

        if (hasSpatial && dragExitedHandArea)
        {
            draggedCardNode.Position = new Vector2(GetViewportRect().Size.X / 2 - draggedCardNode.Size.X / 2, 22);
            UpdateCastPreview(CurrentHoveredCell());
            UpdateDragLine();
        }
        else
        {
            draggedCardNode.Position = mouse - draggedCardNode.Size / 2;
            if (hasSpatial) { MapView.ClearCastPreview(); HideDragLine(); }
        }
    }

    private void UpdateDragLine()
    {
        if (dragLine == null || draggedCardNode == null) return;
        Vector2 cardCenter = draggedCardNode.Position + draggedCardNode.Size / 2;
        Vector2 mouse = GetGlobalMousePosition();
        bool valid = CurrentHoveredCell() is AxialHex h && Session.GetCastCandidates(pendingCardId).Contains(h);
        dragLine.Points = new Vector2[] { cardCenter, mouse };
        dragLine.DefaultColor = valid ? new Color(1f, 0.42f, 0.18f) : new Color(0.62f, 0.66f, 0.72f, 0.7f);
        dragLine.Visible = true;
    }

    private void HideDragLine()
    {
        if (dragLine != null) dragLine.Visible = false;
    }

    private void FinishCardDrag()
    {
        if (draggedCard == null) return;
        Vector2 mouse = GetGlobalMousePosition();
        bool exited = handAreaNode == null ? true : !handAreaNode.GetGlobalRect().HasPoint(mouse);
        bool hasSpatial = Session.GetSpatialSpec(draggedCard.CardId).HasSpatial;

        if (hasSpatial)
        {
            AxialHex? hover = CurrentHoveredCell();
            bool valid = exited && hover.HasValue && Session.GetCastCandidates(draggedCard.CardId).Contains(hover.Value);
            if (valid)
            {
                if (TryCastPendingTo(hover.Value)) CleanupAfterDrag();
                else CancelCardDrag();
            }
            else { CancelCardDrag(); ShowMessage("未选中有效目标格，已取消出牌。"); }
        }
        else
        {
            if (exited)
            {
                if (TryCastPendingTo(null)) CleanupAfterDrag();
                else CancelCardDrag();
            }
            else { CancelCardDrag(); ShowMessage("未拖出手牌区，已取消出牌。"); }
        }
    }

    private void CleanupAfterDrag()
    {
        if (draggedCardNode != null) { draggedCardNode.QueueFree(); draggedCardNode = null; }
        draggedCard = null;
        dragExitedHandArea = false;
        pendingCardId = 0;
        lastCastHover = null;
        HideDragLine();
        MapView.ClearCastPreview();
        RefreshHand(); // 出牌成功：弃牌堆已收走该卡，重排剩余手牌。
    }

    private void CancelCardDrag()
    {
        if (draggedCardNode != null) { draggedCardNode.QueueFree(); draggedCardNode = null; }
        draggedCard = null;
        dragExitedHandArea = false;
        pendingCardId = 0;
        lastCastHover = null;
        HideDragLine();
        MapView.ClearCastPreview();
        RefreshHand(); // 出牌失败/取消：卡牌数据仍留在手牌，重排后回到手牌区。
    }

    // ── 道具拖拽（对齐卡牌逻辑）：无目标→跟手/拖出即用；需目标→直线选目标；丢到道具栏→移动 ──
    private void TryStartItemDragFromPosition(Vector2 position)
    {
        if (Session == null || Session.Phase != BattlefieldSession.BattlePhase.Player) return;
        foreach (Control host in AllItemHosts())
        {
            foreach (Node child in host.GetChildren())
            {
                if (child is Control ctrl && ctrl.GetGlobalRect().HasPoint(position) && itemNodeMap.TryGetValue(ctrl.GetInstanceId(), out GroundObject item))
                {
                    StartItemDrag(item, ctrl);
                    return;
                }
            }
        }
    }

    private IEnumerable<Control> AllItemHosts()
    {
        if (curItemHost != null) yield return curItemHost;
        for (int i = 0; i < 3; i++) if (itemSlotHosts[i] != null) yield return itemSlotHosts[i];
    }

    private void StartItemDrag(GroundObject item, Control node)
    {
        draggedItem = item;
        draggedItemNode = node;
        draggedItemFromCell = node.GetParent() == curItemHost;
        draggedItemSlot = -1;
        for (int i = 0; i < 3; i++) if (itemSlotHosts[i] != null && node.GetParent() == itemSlotHosts[i]) { draggedItemSlot = i; break; }
        itemDragExited = false;
        if (node.GetParent() != null) node.GetParent().RemoveChild(node); // Godot 不允许直接 add_child 已有父节点的节点
        dragLayer.AddChild(node);
        Vector2 mouse = GetGlobalMousePosition();
        node.Position = mouse - node.Size / 2;
        node.Modulate = new Color(1f, 1f, 1f, 0.92f);
        MapView.ClearCastPreview();
        ShowMessage(item.NeedsTarget
            ? $"拖出「{item.DefinitionId}」指向目标格，松左键使用；拖回道具栏或 Esc 取消。"
            : $"拖出「{item.DefinitionId}」离开道具栏即使用。");
    }

    private void UpdateItemDrag()
    {
        if (draggedItem == null || draggedItemNode == null || Session == null) return;
        Vector2 mouse = GetGlobalMousePosition();
        UpdateItemDropHighlights(mouse);
        bool overItemPanels = (curItemPanel != null && curItemPanel.GetGlobalRect().HasPoint(mouse))
            || (carryPanelNode != null && carryPanelNode.GetGlobalRect().HasPoint(mouse));
        itemDragExited = !overItemPanels;

        if (draggedItem.NeedsTarget && !overItemPanels)
        {
            draggedItemNode.Position = new Vector2(GetViewportRect().Size.X / 2 - draggedItemNode.Size.X / 2, 22);
            UpdateItemPreview(CurrentHoveredCell());
            UpdateItemDragLine();
        }
        else
        {
            draggedItemNode.Position = mouse - draggedItemNode.Size / 2;
            if (draggedItem.NeedsTarget) { MapView.ClearCastPreview(); HideDragLine(); }
        }
    }

    private void UpdateItemPreview(AxialHex? hover)
    {
        if (Session == null || draggedItem == null) return;
        var candidates = new HashSet<AxialHex>(Session.GetItemCastCandidates(draggedItem));
        var affected = new HashSet<AxialHex>();
        if (hover.HasValue && candidates.Contains(hover.Value))
            affected = new HashSet<AxialHex>(Session.GetItemAffectedCells(draggedItem, hover.Value));
        MapView.SetCastPreview(candidates, affected, Session.Selected.Coord, hover);
    }

    private void UpdateItemDragLine()
    {
        if (dragLine == null || draggedItemNode == null) return;
        Vector2 cardCenter = draggedItemNode.Position + draggedItemNode.Size / 2;
        Vector2 mouse = GetGlobalMousePosition();
        bool valid = CurrentHoveredCell() is AxialHex h && Session.IsValidItemTarget(draggedItem, h);
        dragLine.Points = new Vector2[] { cardCenter, mouse };
        dragLine.DefaultColor = valid ? new Color(1f, 0.42f, 0.18f) : new Color(0.62f, 0.66f, 0.72f, 0.7f);
        dragLine.Visible = true;
    }

    private int ItemSlotUnder(Vector2 mouse)
    {
        for (int i = 0; i < 3; i++)
        {
            if (itemSlotHosts[i] != null && itemSlotHosts[i].GetGlobalRect().HasPoint(mouse)) return i;
        }

        return -1;
    }

    private void UpdateItemDropHighlights(Vector2 mouse)
    {
        int hovered = ItemSlotUnder(mouse);
        for (int i = 0; i < 3; i++)
        {
            if (itemSlotHosts[i] == null || itemSlotDefaultBox[i] == null) continue;
            itemSlotHosts[i].AddThemeStyleboxOverride("panel", i == hovered ? itemSlotHoverBox[i] : itemSlotDefaultBox[i]);
        }
        if (curItemPanel != null && curItemDefaultBox != null && curItemHoverBox != null)
        {
            bool overCur = curItemPanel.GetGlobalRect().HasPoint(mouse);
            curItemPanel.AddThemeStyleboxOverride("panel", overCur ? curItemHoverBox : curItemDefaultBox);
        }
    }

    private void ClearItemDropHighlights()
    {
        for (int i = 0; i < 3; i++)
        {
            if (itemSlotHosts[i] != null && itemSlotDefaultBox[i] != null)
                itemSlotHosts[i].AddThemeStyleboxOverride("panel", itemSlotDefaultBox[i]);
        }
        if (curItemPanel != null && curItemDefaultBox != null)
            curItemPanel.AddThemeStyleboxOverride("panel", curItemDefaultBox);
    }

    private void FinishItemDrag()
    {
        if (draggedItem == null) return;
        Vector2 mouse = GetGlobalMousePosition();

        int slot = ItemSlotUnder(mouse);
        bool sameSlot = !draggedItemFromCell && slot == draggedItemSlot;
        if (slot >= 0 && !sameSlot)
        {
            string err = "";
            bool ok = draggedItemFromCell
                ? Session.TryPickItemFromCurrentCell(draggedItem.InstanceId, slot, out err)
                : Session.TryMoveItemBetweenSlots(draggedItemSlot, slot, out err);
            if (ok) CleanupItemDrag();
            else { CancelItemDrag(); ShowMessage(err); }
            return;
        }
        if (sameSlot) { CancelItemDrag(); return; }

        if (curItemPanel != null && curItemPanel.GetGlobalRect().HasPoint(mouse))
        {
            if (draggedItemFromCell) CancelItemDrag();
            else if (Session.TryDropItemOnCurrentCell(draggedItemSlot, out string err)) CleanupItemDrag();
            else { CancelItemDrag(); ShowMessage(err); }
            return;
        }

        // 道具栏之外：使用。
        if (draggedItem.NeedsTarget)
        {
            AxialHex? hover = CurrentHoveredCell();
            bool valid = hover.HasValue && Session.IsValidItemTarget(draggedItem, hover.Value);
            if (!valid) { CancelItemDrag(); ShowMessage("未选中有效目标，已取消使用。"); return; }
            string err = "";
            bool ok = draggedItemFromCell
                ? Session.TryUseItemFromCurrentCell(draggedItem.InstanceId, hover.Value, out err)
                : Session.TryUseItemAt(draggedItemSlot, hover.Value, out err);
            if (ok) CleanupItemDrag(); else { CancelItemDrag(); ShowMessage(err); }
        }
        else
        {
            string err = "";
            bool ok = draggedItemFromCell
                ? Session.TryUseItemFromCurrentCell(draggedItem.InstanceId, null, out err)
                : Session.TryUseItem(draggedItemSlot, out err);
            if (ok) CleanupItemDrag(); else { CancelItemDrag(); ShowMessage(err); }
        }
    }

    private void CleanupItemDrag()
    {
        if (draggedItemNode != null) { draggedItemNode.QueueFree(); draggedItemNode = null; }
        draggedItem = null;
        itemDragExited = false;
        draggedItemFromCell = false;
        draggedItemSlot = -1;
        ClearItemDropHighlights();
        HideDragLine();
        MapView.ClearCastPreview();
    }

    private void CancelItemDrag()
    {
        if (draggedItemNode != null)
        {
            Control host = draggedItemFromCell ? (Control)curItemHost
                : (draggedItemSlot >= 0 && draggedItemSlot < 3 ? (Control)itemSlotHosts[draggedItemSlot] : null);
            if (host != null)
            {
                if (draggedItemNode.GetParent() == dragLayer) dragLayer.RemoveChild(draggedItemNode);
                draggedItemNode.Modulate = Colors.White;
                host.AddChild(draggedItemNode);
            }
        }
        draggedItemNode = null;
        draggedItem = null;
        itemDragExited = false;
        draggedItemFromCell = false;
        draggedItemSlot = -1;
        ClearItemDropHighlights();
        HideDragLine();
        MapView.ClearCastPreview();
    }

    private static string ItemEffectText(GroundObject item)
    {
        var parts = new List<string>();
        if (item.HealAmount > 0) parts.Add($"治疗+{item.HealAmount}");
        if (item.DamageAmount > 0) parts.Add($"伤害{item.DamageAmount}");
        if (item.NeedsTarget)
        {
            string shape = item.SpatialShape switch
            {
                ItemSpatialShape.Single => "单体",
                ItemSpatialShape.Line => $"直线{item.ItemLength}",
                ItemSpatialShape.Fan => $"扇形{item.ItemRadius}",
                ItemSpatialShape.Ring => $"环形{item.ItemRadius}",
                ItemSpatialShape.Diamond => "菱形",
                _ => "范围",
            };
            parts.Add(shape);
        }
        if (item.ItemTrapId.Length > 0) parts.Add("陷阱");
        return parts.Count == 0 ? "道具" : string.Join(" ", parts.Take(3));
    }

    private static Control MakeItemPrefab(GroundObject item)
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(54, 58),
            SizeFlagsHorizontal = SizeFlags.Fill,
            MouseFilter = MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", WellBox(new Color(0.16f, 0.20f, 0.30f), new Color(0.55f, 0.62f, 0.72f), 1, 7));
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; box.AddThemeConstantOverride("separation", 2);
        var name = new Label { Text = item.DefinitionId, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        name.AddThemeFontSizeOverride("font_size", 10);
        name.AddThemeColorOverride("font_color", Colors.White);
        box.AddChild(name);
        var sub = new Label { Text = ItemEffectText(item), HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        sub.AddThemeFontSizeOverride("font_size", 8);
        sub.AddThemeColorOverride("font_color", new Color("c8d0d8"));
        box.AddChild(sub);
        panel.AddChild(box);
        SetDescendantsIgnore(panel);
        return panel;
    }

    private void RefreshItems()
    {
        if (Session == null || curItemHost == null) return;
        if (draggedItem != null) CancelItemDrag();
        foreach (Node c in curItemHost.GetChildren()) c.QueueFree();
        for (int i = 0; i < 3; i++) if (itemSlotHosts[i] != null) foreach (Node c in itemSlotHosts[i].GetChildren()) c.QueueFree();
        itemNodeMap.Clear();

        var items = Session.Board.Cells[Session.Selected.Coord].Items;
        foreach (GroundObject item in items)
        {
            if (item.Kind != GroundObjectKind.Item) continue;
            Control node = MakeItemPrefab(item);
            itemNodeMap[node.GetInstanceId()] = item;
            curItemHost.AddChild(node);
        }
        if (items.Count == 0) curItemHost.AddChild(Label("（无）", 11, new Color("c8d0d8")));

        var loadout = Session.SelectedLoadout;
        for (int i = 0; i < 3; i++)
        {
            if (itemSlotHosts[i] == null) continue;
            if (loadout.Items[i] != null)
            {
                Control node = MakeItemPrefab(loadout.Items[i]);
                itemNodeMap[node.GetInstanceId()] = loadout.Items[i];
                itemSlotHosts[i].AddChild(node);
            }
            else
            {
                itemSlotHosts[i].AddChild(Label("空", 11, new Color("8a9199")));
            }
        }
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
