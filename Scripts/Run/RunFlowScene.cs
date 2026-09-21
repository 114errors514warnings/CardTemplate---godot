using Godot;
using System;
using System.Linq;

public partial class RunFlowScene : Control
{
    // CanvasLayer 是运行局唯一的跨子树层级边界；层号统一定义在 RunUiLayers，禁止用 ZIndex 跨层抢占。
    private MapScene map;
    private Control host;
    private Button mapButton, focusButton, debugButton, pauseButton, logButton, hideButton, autoButton, skipButton;
    // 剧情专属按钮（Log / 隐藏 / Auto / 跳过）：排在通用按钮下方，打开世界地图时整行隐藏。
    private Control storyRow, skipRow;
    private HexBattleScene activeBattle;
    // 地图交互模式：内容进行中 = 只读，内容完成或尚未开始 = 可选（对应策划案的 ReadOnly / Selectable）。
    private bool mapSelectable = true;
    // 等待内容战场视图接入按钮栏的重试上限：内容没有战场视图时必须停下，不能无限 deferred 自旋。
    private const int AttachMapRetryLimit = 180;
    private int attachMapRetries;
    private CanvasLayer contentLayer, worldMapLayer, modalLayer, globalButtonLayer;
    private byte[] runSaveBackup;
    private bool runSaveExisted;
    private CardSimulator.Battlefield.HexBattleDebugPanel debugPanel;
    private Node debugPanelSource;
    public override void _Ready()
    {
        bool uiSmoke = OS.GetCmdlineUserArgs().Contains("--run-flow-ui-smoke");
        if (uiSmoke && RunSession.Instance?.Current == null)
        {
            // 烟测会新建本局并写档：先备份玩家存档，结束时还原，避免覆盖正式进度。
            BackupRunSaveFile();
            LoadingSystem.EnsureAllDataLoaded();
            RunSession.Instance.StartNewRun(new[] { 1002, 1003, 1004 }, 20260921);
        }

        contentLayer = CreateLayer("ContentLayer", RunUiLayers.Content);
        worldMapLayer = CreateLayer("WorldMapLayer", RunUiLayers.WorldMap);
        modalLayer = CreateLayer("ModalLayer", RunUiLayers.Modal);
        globalButtonLayer = CreateLayer("GlobalButtonLayer", RunUiLayers.GlobalButton);

        host = new Control { MouseFilter = MouseFilterEnum.Ignore };
        host.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        contentLayer.AddChild(host);

        var packed = GD.Load<PackedScene>("res://Scenes/Map/MapScene.tscn");
        map = packed.Instantiate<MapScene>(); map.EmbeddedMode = true;
        map.LevelRequested += StartLevel; map.EventRequested += StartEvent;
        map.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        worldMapLayer.AddChild(map);
        BuildGlobalTopBar();
        var run = RunSession.Instance;
        if (run?.Current?.PendingContentType == "Event") StartEvent(run.Current.PendingContentId);
        else if (run?.Current?.PendingContentType == "Level" || run?.IsInSettlement == true || run?.IsInBattleStart == true) StartLevel(run?.Current?.PendingContentId);
        else if (uiSmoke) { map.SetReadOnly(false); CallDeferred(nameof(RunUiSmoke)); }
        else { map.SetReadOnly(false); CallDeferred(nameof(TriggerStartEvent)); }
    }
    private void TriggerStartEvent() => map.TriggerStartEventIfNeeded();
    private void StartLevel(string id)
    {
        mapSelectable = false;
        activeBattle = null; // 上一份内容即将销毁：先断开引用，避免顶部按钮打到已释放节点。
        attachMapRetries = 0;
        ClearHost(); SetWorldMapVisible(false); map.SetReadOnly(true);
        var content = GD.Load<PackedScene>("res://Scenes/Run/RunBattleScene.tscn").Instantiate<RunBattleScene>();
        content.ContentFinished += ReturnToSelectableMap; host.AddChild(content);
        CallDeferred(nameof(AttachMapToContent));
    }
    private void StartEvent(string id)
    {
        mapSelectable = false;
        activeBattle = null; // 上一份内容即将销毁：先断开引用，避免顶部按钮打到已释放节点。
        attachMapRetries = 0;
        ClearHost(); SetWorldMapVisible(false); map.SetReadOnly(true);
        var content = GD.Load<PackedScene>("res://Scenes/Run/RunEventScene.tscn").Instantiate<RunEventScene>();
        content.ContentFinished += ReturnToSelectableMap; content.LevelRequested += StartLevel; host.AddChild(content);
        CallDeferred(nameof(AttachMapToContent));
    }
    private void ToggleMap()
    {
        // 没有内容可返回（续玩刚进根场景、纯地图烟测等）时只保证地图可见可选，避免关成空屏。
        if (host.GetChildCount() == 0)
        {
            SetWorldMapVisible(true); map.SetReadOnly(!mapSelectable); mapButton.Text = "地图";
            return;
        }
        bool visible = !map.Visible;
        SetWorldMapVisible(visible);
        // 内容未完成的地图只读；内容完成后返回的地图必须可选，否则选不了下一个节点。
        map.SetReadOnly(!mapSelectable);
        mapButton.Text = visible ? "返回" : "地图";
    }
    /// <summary>世界地图开合的唯一入口：同时通知内容让出/收回自己的 UI，
    /// 否则地图之下会透出内容界面（剧情 UI、战斗 HUD）。</summary>
    private void SetWorldMapVisible(bool visible)
    {
        map.Visible = visible;
        ApplyStoryRowVisibility();
        if (activeBattle != null && GodotObject.IsInstanceValid(activeBattle)) activeBattle.SetWorldMapOpen(visible);
    }
    /// <summary>内容完成：不销毁已完成内容，只把世界地图以“已打开”的覆盖层状态叠上去，
    /// 顶部按钮栏继续按当前内容形态配置——表现与局内按下“地图”按钮完全一致。</summary>
    private void ReturnToSelectableMap()
    {
        mapSelectable = true;
        SetWorldMapVisible(true);
        map.SetReadOnly(false);
        ConfigureGlobalTopBar(FindBattle(host));
    }
    private void ClearHost()
    {
        foreach (Node child in host.GetChildren()) { host.RemoveChild(child); child.QueueFree(); }
    }
    private void AttachMapToContent()
    {
        if (mapSelectable) return; // 内容已完成：地图已作为覆盖层打开，不再回写内容态按钮栏。
        HexBattleScene battle = FindBattle(host);
        if (battle == null || battle.MapView == null)
        {
            // 部分内容没有战场视图（读档重进的结算界面）：限量重试，避免 deferred 自我重排无限自旋，
            // 否则退出游戏时挂起的 deferred 调用会落到正在拆除的实例上（native 访问违例）。
            if (++attachMapRetries > AttachMapRetryLimit)
            {
                GD.Print("[运行局] 内容没有战场视图，保留当前按钮栏，不再等待其接管。");
                return;
            }
            CallDeferred(nameof(AttachMapToContent));
            return;
        }
        attachMapRetries = 0;
        // 地图必须始终是 RunFlowScene 的直接子节点，不能重挂进 HexBattleScene。
        // 否则会和剧情浮层共用父节点并退化成依赖节点插入顺序的渲染关系。
        SetWorldMapVisible(false);
        ConfigureGlobalTopBar(battle);
    }
    private static HexBattleScene FindBattle(Node node)
    {
        if (node is HexBattleScene battle) return battle;
        foreach (Node child in node.GetChildren()) { HexBattleScene found = FindBattle(child); if (found != null) return found; }
        return null;
    }

    private CanvasLayer CreateLayer(string name, int order)
    {
        CanvasLayer layer = new CanvasLayer { Name = name, Layer = order };
        AddChild(layer);
        return layer;
    }

    private void BuildGlobalTopBar()
    {
        // 通用按钮行：选点态也显示的常驻入口，放最上层位置。
        HBoxContainer generalRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        generalRow.AddThemeConstantOverride("separation", 8);
        generalRow.AnchorLeft = .64f; generalRow.AnchorTop = .025f; generalRow.AnchorRight = .98f; generalRow.AnchorBottom = .08f;
        globalButtonLayer.AddChild(generalRow);
        mapButton = AddTopButton(generalRow, "地图", ToggleMap);
        focusButton = AddTopButton(generalRow, "定位当前角色", () => activeBattle?.CenterSelectedUnitFromGlobalTopBar());
        debugButton = AddTopButton(generalRow, "调试", ToggleDebugPanel);
        pauseButton = AddTopButton(generalRow, "暂停", () => activeBattle?.TogglePauseFromGlobalTopBar());

        // 剧情专属按钮：左侧 Log / 隐藏 / Auto，右侧 跳过；都在通用行下方同一带内。
        storyRow = new HBoxContainer();
        storyRow.AddThemeConstantOverride("separation", 10);
        storyRow.AnchorLeft = .02f; storyRow.AnchorTop = .09f; storyRow.AnchorRight = .37f; storyRow.AnchorBottom = .145f;
        globalButtonLayer.AddChild(storyRow);
        logButton = AddTopButton(storyRow, "Log", () => activeBattle?.ActiveStoryOverlay?.ToggleLogFromGlobalTopBar());
        hideButton = AddTopButton(storyRow, "隐藏", () => activeBattle?.ActiveStoryOverlay?.ToggleHiddenFromGlobalTopBar());
        autoButton = AddTopButton(storyRow, "Auto: 关闭", () => activeBattle?.ActiveStoryOverlay?.ToggleAutoFromGlobalTopBar());

        skipRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        skipRow.AddThemeConstantOverride("separation", 8);
        skipRow.AnchorLeft = .64f; skipRow.AnchorTop = .09f; skipRow.AnchorRight = .98f; skipRow.AnchorBottom = .145f;
        globalButtonLayer.AddChild(skipRow);
        skipButton = AddTopButton(skipRow, "跳过", () => activeBattle?.ActiveStoryOverlay?.ToggleSkipFromGlobalTopBar());
        ConfigureGlobalTopBar(null);
    }

    private static Button AddTopButton(Control parent, string text, System.Action action)
    {
        Button button = new Button { Text = text, CustomMinimumSize = new Vector2(88, 38) };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private void ConfigureGlobalTopBar(HexBattleScene battle)
    {
        activeBattle = battle;
        bool hasContent = battle != null;
        EventStoryOverlay story = battle?.ActiveStoryOverlay;
        bool isStory = story != null;

        mapButton.Visible = true;
        // 地图按钮常驻可用：无内容时它的作用是“确保地图可见且可选”（见 ToggleMap），不再用灰显表达。
        mapButton.Disabled = false;
        mapButton.Text = map.Visible && hasContent ? "返回" : "地图";
        focusButton.Visible = hasContent && !isStory;
        // 选点态保留调试入口；暂停只在存在可暂停的内容时显示。
        debugButton.Visible = true;
        pauseButton.Visible = hasContent;
        // 地图打开时剧情 UI 必须处于让位状态：无论剧情是“按地图前”还是“地图打开后”才打开的，都统一在这里对齐。
        story?.SetWorldMapOpen(map.Visible);
        ApplyStoryRowVisibility();

        if (battle == null) return;
        battle.SetBuiltInTopActionsVisible(false);
        story?.SetBuiltInTopControlsVisible(false);
        // 内容完成返回地图时会按同一内容再次配置按钮栏，必须先解绑，避免重复注册导致调试窗被连点两次。
        battle.StoryOverlayOpened -= OnStoryOverlayOpened;
        battle.DebugRequested -= OnBattleDebugRequested;
        battle.StoryOverlayOpened += OnStoryOverlayOpened;
        battle.DebugRequested += OnBattleDebugRequested;
    }

    private void OnStoryOverlayOpened(HexBattleScene battle)
    {
        if (battle == activeBattle) ConfigureGlobalTopBar(battle);
    }

    /// <summary>剧情推进到选项 / 结束面板会改变 `CanSkipNow`，而浮层不广播这类状态；
    /// 这里每帧只做一次廉价比较，状态真的变了才重算可见性（不每帧写 Visible）。</summary>
    public override void _Process(double delta)
    {
        if (storyRow == null) return;
        EventStoryOverlay story = activeBattle?.ActiveStoryOverlay;
        bool show = story != null && !map.Visible;
        if (storyRow.Visible == show && skipRow.Visible == (show && story.CanSkipNow)) return;
        ApplyStoryRowVisibility();
    }

    /// <summary>剧情专属按钮（左：Log / 隐藏 / Auto；右：跳过）：只在“存在剧情 UI 且世界地图未打开”时显示。
    /// 它们属于剧情 UI，排在通用按钮下方；地图打开后由通用按钮（地图 / 定位 / 调试 / 暂停）接管。
    /// 跳过只到“剧情仍在推进”为止：进入选项或结束面板后与浮层内置跳过按钮同步隐藏。</summary>
    private void ApplyStoryRowVisibility()
    {
        EventStoryOverlay story = activeBattle?.ActiveStoryOverlay;
        bool show = story != null && !map.Visible;
        storyRow.Visible = show;
        skipRow.Visible = show && story.CanSkipNow;
        logButton.Visible = show;
        hideButton.Visible = show;
        autoButton.Visible = show;
        skipButton.Visible = show && story.CanSkipNow;
    }

    private void ToggleDebugPanel()
    {
        if (activeBattle != null) ShowDebugPanel(activeBattle, activeBattle.CreateDebugPanel);
        else ShowDebugPanel(map, map.CreateDebugPanel);
    }

    private void OnBattleDebugRequested(HexBattleScene battle)
    {
        if (battle == activeBattle) ShowDebugPanel(battle, battle.CreateDebugPanel);
    }

    private void ShowDebugPanel(Node source, System.Func<CardSimulator.Battlefield.HexBattleDebugPanel> create)
    {
        if (debugPanelSource != source || debugPanel == null || !GodotObject.IsInstanceValid(debugPanel))
        {
            if (debugPanel != null && GodotObject.IsInstanceValid(debugPanel)) debugPanel.QueueFree();
            debugPanel = create();
            debugPanel.Visible = false;
            debugPanel.VisibilityChanged += OnDebugPanelVisibilityChanged;
            modalLayer.AddChild(debugPanel);
            debugPanelSource = source;
        }
        if (!debugPanel.Visible) SetMapInputForModal(true);
        debugPanel.ToggleVisible();
    }

    private void OnDebugPanelVisibilityChanged()
    {
        if (debugPanel != null && GodotObject.IsInstanceValid(debugPanel))
            SetMapInputForModal(debugPanel.Visible);
    }

    private void SetMapInputForModal(bool modalOpen)
    {
        if (map != null && map.Visible)
            map.MouseFilter = modalOpen ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
    }

    /// <summary>运行局 UI 回归烟测：验证世界地图上方的 ModalLayer 能接收调试窗关闭按钮的真实鼠标输入。</summary>
    private async void RunUiSmoke()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(map.GetParent() == worldMapLayer, "世界地图必须属于 WorldMapLayer。");
            Require(contentLayer.Layer < worldMapLayer.Layer && worldMapLayer.Layer < modalLayer.Layer && modalLayer.Layer < globalButtonLayer.Layer,
                "运行局 CanvasLayer 顺序错误。");

            // 事件中的硬要求：除战场本身外不得显示任何战斗 UI（剧情 UI 在打开地图时让位）。
            void RequireOnlyBattlefieldVisible(HexBattleScene battle, string phase)
            {
                foreach (Node child in battle.GetChildren())
                {
                    if (child is not CanvasItem item || child == battle.MapView || child == battle.ActiveStoryOverlay) continue;
                    if (item.Name.ToString().Contains("Debug")) continue; // 调试面板按设计在内容中保持可用。
                    Require(!item.Visible, $"{phase}：除战场本身外不得显示战斗 UI，实际仍在显示：{item.Name}");
                }
            }

            ToggleDebugPanel();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(debugPanel?.Visible == true, "调试窗未显示在 ModalLayer。");
            Button close = FindButton(debugPanel, "关闭");
            Require(close != null, "调试窗缺少关闭按钮。");

            Vector2 position = close.GetGlobalRect().GetCenter();
            bool closeReceivedGuiInput = false;
            close.GuiInput += _ => closeReceivedGuiInput = true;
            GD.Print($"RUN_FLOW_UI_SMOKE_CLOSE_RECT: {close.GetGlobalRect()} / mapFilter={map.MouseFilter} / debugFilter={debugPanel.MouseFilter}");
            GetViewport().WarpMouse(position);
            GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("RUN_FLOW_UI_SMOKE_HOVERED: " + (GetViewport().GuiGetHoveredControl()?.GetPath().ToString() ?? "<none>"));
            // 按下与抬起必须同一帧注入：窗口在屏幕外或物理光标移动时，跨帧的注入点击会丢 pressed 事件。
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = position, GlobalPosition = position });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = position, GlobalPosition = position });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(closeReceivedGuiInput, "关闭按钮没有收到 GUI 输入。");
            Require(!debugPanel.Visible, "地图打开时，调试窗关闭按钮未收到输入。");

            // 内容完成后返回地图：内容不销毁，地图以“已打开”的覆盖层叠上去，
            // 顶部按钮栏保持内容形态——与局内按下“地图”按钮的表现一致。
            TriggerStartEvent();
            await WaitFrames(3);
            // 事件中（尚未点“前往地图”）：除战场本身外不得显示战斗 UI；
            // 此时按地图应只读打开，且剧情 UI 必须让位、关闭地图后恢复。
            HexBattleScene eventContent = FindBattle(host);
            Require(eventContent?.ActiveStoryOverlay?.Visible == true, "事件中应显示剧情 UI。");
            RequireOnlyBattlefieldVisible(eventContent, "事件中");
            ToggleMap();
            await WaitFrames(2);
            Require(map.Visible && map.IsReadOnly, "事件进行中打开地图应只读。");
            Require(!eventContent.ActiveStoryOverlay.Visible, "事件中打开地图时剧情 UI 必须让位。");
            Require(!storyRow.Visible && !logButton.Visible, "打开世界地图时不显示剧情专属按钮行。");
            RequireOnlyBattlefieldVisible(eventContent, "事件中打开地图时");
            ToggleMap();
            await WaitFrames(2);
            Require(!map.Visible && eventContent.ActiveStoryOverlay.Visible, "关闭地图后事件 UI 应恢复显示。");
            Require(storyRow.Visible && logButton.Visible && hideButton.Visible && autoButton.Visible, "关闭地图后应显示剧情专属按钮行（Log / 隐藏 / Auto）。");
            Require(logButton.GetGlobalRect().Position.Y > pauseButton.GetGlobalRect().Position.Y, "剧情专属按钮必须排在通用按钮（暂停 / 调试）下方。");
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-story-buttons.png");

            // 暂停界面必须在最高层级：压过世界地图、模态窗与常驻按钮栏，且“继续”可点。
            eventContent.TogglePauseFromGlobalTopBar();
            await WaitFrames(2);
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-pause.png");
            CanvasLayer pauseLayer = FindLayer(eventContent, RunUiLayers.Pause);
            Require(pauseLayer != null, "暂停界面应挂在独立的最高层 CanvasLayer 上。");
            Require(pauseLayer.Layer > globalButtonLayer.Layer && pauseLayer.Layer > worldMapLayer.Layer && pauseLayer.Layer > modalLayer.Layer,
                $"暂停层必须是最高层，实际 {pauseLayer.Layer}。");
            Button resume = FindButton(pauseLayer, "继续");
            Require(resume != null, "暂停界面缺少“继续”按钮。");
            resume.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Require(!GetTree().Paused, "点“继续”后应恢复运行。");
            Button goToMap = FindButton(host, "前往地图");
            Require(goToMap != null, "开始事件未提供“前往地图”按钮。");
            goToMap.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            GD.Print($"RUN_FLOW_UI_SMOKE_MAP_RETURN: map={map.Visible} text={mapButton.Text} disabled={mapButton.Disabled} focus={focusButton.Visible} " +
                $"pause={pauseButton.Visible} storyRow={storyRow.Visible} log={logButton.Visible} story={(eventContent?.ActiveStoryOverlay != null)} " +
                $"storyVisible={eventContent?.ActiveStoryOverlay?.Visible} readOnly={map.IsReadOnly} filter={map.MouseFilter}");
            Require(map.Visible, "完成事件返回地图后地图应保持打开。");
            Require(mapButton.Text == "返回", $"完成事件返回地图后地图按钮应显示“返回”，实际“{mapButton.Text}”。");
            Require(!mapButton.Disabled, "完成事件返回地图后地图按钮应保持可用。");
            Require(pauseButton.Visible, "完成事件返回地图后应保留内容形态的顶部按钮栏（暂停）。");
            Require(!map.IsReadOnly && map.MouseFilter == MouseFilterEnum.Stop, "完成事件返回地图后地图必须是可选态并能接收输入。");

            // 事件的两条硬要求：① 地图之下只露出战场本身（战斗 UI 全部隐藏）；
            // ② 剧情 UI 不被销毁，只是让位给地图，关闭地图后能恢复。
            Require(eventContent?.ActiveStoryOverlay != null, "完成事件后应保留剧情 UI（供关闭地图后恢复）。");
            Require(!eventContent.ActiveStoryOverlay.Visible, "世界地图打开时剧情 UI 必须隐藏。");
            Require(!storyRow.Visible && !logButton.Visible && !hideButton.Visible && !autoButton.Visible, "世界地图打开时不显示剧情专属按钮行（Log / 隐藏 / Auto）。");
            RequireOnlyBattlefieldVisible(eventContent, "完成事件返回地图后");
            // 关闭地图应回到事件 UI（剧情浮层恢复显示，地图不再遮挡，剧情专属按钮行回来）。
            ToggleMap();
            await WaitFrames(2);
            Require(!map.Visible && eventContent.ActiveStoryOverlay.Visible, "关闭地图后应恢复显示事件 UI。");
            Require(storyRow.Visible && logButton.Visible, "关闭地图后应恢复显示剧情专属按钮行。");
            ToggleMap();
            await WaitFrames(2);
            Require(map.Visible && !eventContent.ActiveStoryOverlay.Visible, "再次打开地图应重新让出事件 UI。");
            Require(!storyRow.Visible, "再次打开地图应再次隐藏剧情专属按钮行。");

            // 地图打开时暂停仍必须压住地图与常驻按钮栏，解除后地图保持打开。
            eventContent.TogglePauseFromGlobalTopBar();
            await WaitFrames(2);
            Require(GetTree().Paused, "打开地图时点“暂停”应进入暂停态。");
            Require(FindLayer(eventContent, RunUiLayers.Pause)?.Visible == true, "打开地图时暂停界面必须显示在最高层。");
            Button resumeOnMap = FindButton(eventContent, "继续");
            Require(resumeOnMap != null, "暂停界面缺少“继续”按钮。");
            resumeOnMap.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Require(!GetTree().Paused && map.Visible, "解除暂停后地图应保持打开。");
            await CaptureSmoke("res://Tests/run-flow-ui-smoke.png");

            // 完成关卡（结算确认）后返回地图：被消费的结算界面必须让位，不能压在地图上或吃掉地图输入。
            RunSession run = RunSession.Instance;
            run.EnterSettlement("烟测结算", 0, null);
            StartLevel(string.Empty);
            await WaitFrames(3);
            Button backToMap = FindButton(host, "返回地图");
            Require(backToMap != null, "结算界面未提供“返回地图”按钮。");
            backToMap.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);

            Vector2 mapCenter = GetViewportRect().Size * 0.5f;
            GetViewport().WarpMouse(mapCenter);
            GetViewport().PushInput(new InputEventMouseMotion { Position = mapCenter, GlobalPosition = mapCenter });
            await WaitFrames(1);
            string hovered = GetViewport().GuiGetHoveredControl()?.GetPath().ToString() ?? "<none>";
            GD.Print($"RUN_FLOW_UI_SMOKE_MAP_RETURN_SETTLEMENT: map={map.Visible} readOnly={map.IsReadOnly} filter={map.MouseFilter} hovered={hovered}");
            Require(map.Visible && !map.IsReadOnly && map.MouseFilter == MouseFilterEnum.Stop, "结算返回地图后地图必须是可选态并能接收输入。");
            Require(hovered == map.GetPath().ToString(), "结算返回地图后地图中心应能被点中（结算界面不得遮挡）。");
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-settlement-readmit.png");

            // 真实流程：进关卡战斗 → 打赢 → 结算 → 返回地图。
            // 用测试钩子清空敌人（DebugClearEnemies → EvaluateOutcome → 胜利），走完整转场而不依赖鼠标注入。
            CardSimulator.Battlefield.BattleLevelConfig smokeLevel = FindSmokeLevel();
            Require(smokeLevel != null, "找不到带怪物配置的关卡，无法覆盖真实战斗流程。");
            run.BeginRunBattleEncounter(string.Empty, new StageEncounterRow
            {
                Layer = string.Empty,
                NodeType = MapNodeType.NormalCombat,
                Name = smokeLevel.LevelId,
                Difficulty = StageDifficulty.Any,
                DropTableId = smokeLevel.DropTableId,
                LevelId = smokeLevel.LevelId,
                MonsterIds = smokeLevel.Objects.Where(x => x.ObjectType == "Monster").Select(x => int.Parse(x.DefinitionId)).ToArray(),
            });
            StartLevel(smokeLevel.LevelId);
            await WaitFrames(8);
            HexBattleScene realBattle = FindBattle(host);
            Require(realBattle?.Session != null, "关卡战斗内容没有创建战场视图。");
            realBattle.Session.DebugClearEnemies();
            await WaitFrames(8);

            Button battleReturn = FindButton(host, "返回地图") ?? FindButton(host, "确认选择并返回地图");
            Require(battleReturn != null, "关卡结算界面未提供返回地图按钮。");
            if (battleReturn.Disabled)
            {
                // 有卡牌奖励时确认按钮要选完卡才可用：按玩家路径先开卡牌页再选第一张候选。
                Button cardTab = FindButton(host, "卡牌");
                if (cardTab != null) { cardTab.EmitSignal(BaseButton.SignalName.Pressed); await WaitFrames(1); }
                Button candidate = FindToggleButton(host);
                Require(candidate != null, "结算要求选牌，但没有找到候选卡牌按钮。");
                candidate.ButtonPressed = true;
                await WaitFrames(1);
            }
            battleReturn.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(8);

            GD.Print($"RUN_FLOW_UI_SMOKE_MAP_RETURN_BATTLE: level={smokeLevel.LevelId} map={map.Visible} text={mapButton.Text} disabled={mapButton.Disabled} " +
                $"focus={focusButton.Visible} pause={pauseButton.Visible} readOnly={map.IsReadOnly} filter={map.MouseFilter}");
            Require(map.Visible && mapButton.Text == "返回" && !mapButton.Disabled, "关卡结算返回地图后地图应保持打开且按钮显示“返回”。");
            Require(focusButton.Visible && pauseButton.Visible, "关卡结算返回地图后应保留内容形态按钮栏（定位当前角色/暂停）。");
            Require(!map.IsReadOnly && map.MouseFilter == MouseFilterEnum.Stop, "关卡结算返回地图后地图必须是可选态并能接收输入。");
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-battle.png");

            // 普通事件（非 autoComplete）进行中必须能看到“跳过”，且左 Log/隐藏/Auto、右 跳过。
            // 直接在当前战斗内容上打开剧情（OpenStoryEvent），避免切场景导致烟测协程被换掉。
            if (map.Visible) { ToggleMap(); await WaitFrames(2); }
            realBattle.OpenStoryEvent("EVT-F1-001");
            await WaitFrames(4);
            HexBattleScene normalEvent = realBattle;
            Require(normalEvent?.ActiveStoryOverlay?.Visible == true, "普通事件进行中应显示剧情 UI。");
            GD.Print($"RUN_FLOW_UI_SMOKE_SKIP: storyRow={storyRow.Visible} skipRow={skipRow.Visible} skip={skipButton.Visible} " +
                $"autoX={autoButton.GetGlobalRect().Position.X} pauseX={pauseButton.GetGlobalRect().Position.X} skipX={skipButton.GetGlobalRect().Position.X}");
            Require(storyRow.Visible && logButton.Visible && hideButton.Visible && autoButton.Visible, "普通事件进行中应显示 Log / 隐藏 / Auto。");
            Require(skipRow.Visible && skipButton.Visible, "普通事件进行中应显示“跳过”按钮。");
            Require(autoButton.GetGlobalRect().Position.X < pauseButton.GetGlobalRect().Position.X, "Log / 隐藏 / Auto 必须排在左侧。");
            Require(skipButton.GetGlobalRect().Position.X > autoButton.GetGlobalRect().Position.X, "“跳过”必须排在右侧。");
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-skip.png");
            // 点“跳过”先弹确认面板，取消后回到剧情（跳过不会替玩家选结果）。
            skipButton.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Button backToStory = FindButton(normalEvent, "返回剧情");
            Require(backToStory != null, "点“跳过”应弹出跳过确认面板。");
            backToStory.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Require(normalEvent.ActiveStoryOverlay.Visible, "取消跳过后应回到剧情。");
            // 剧情推进到选项面板后，常驻栏“跳过”必须与浮层内置跳过同步消失（否则点到的是空操作）。
            skipButton.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Button confirmSkip = FindButton(normalEvent, "确认跳过");
            Require(confirmSkip?.IsVisibleInTree() == true, "跳过确认面板缺少可用的“确认跳过”。");
            confirmSkip.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(FindButtonContaining(normalEvent, "就地休息")?.IsVisibleInTree() == true, "确认跳过后应显示带公开影响的选项。");
            Require(!skipRow.Visible && !skipButton.Visible, "剧情进入选项面板后常驻栏“跳过”必须隐藏。");
            Require(confirmSkip.IsVisibleInTree() == false, "确认跳过后跳过确认面板必须关闭。");

            GD.Print("RUN_FLOW_UI_SMOKE_PASS: canvas layers, map modal input, debug close, map return after content");
            RestoreRunSaveFile();
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr("RUN_FLOW_UI_SMOKE_FAIL: " + ex);
            RestoreRunSaveFile();
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task CaptureSmoke(string resPath)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Require(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(resPath)) == Error.Ok, "无法保存运行局 UI 烟测截图：" + resPath);
    }

    private async System.Threading.Tasks.Task WaitFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>烟测会新建本局并写档：先备份玩家存档，结束时还原，避免覆盖正式进度。</summary>
    private void BackupRunSaveFile()
    {
        runSaveExisted = FileAccess.FileExists(RunSession.SavePath);
        runSaveBackup = runSaveExisted ? FileAccess.GetFileAsBytes(RunSession.SavePath) : null;
    }

    private void RestoreRunSaveFile()
    {
        if (runSaveBackup != null)
        {
            using FileAccess file = FileAccess.Open(RunSession.SavePath, FileAccess.ModeFlags.Write);
            file?.StoreBuffer(runSaveBackup);
            return;
        }
        if (!runSaveExisted && FileAccess.FileExists(RunSession.SavePath))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(RunSession.SavePath));
        }
    }

    /// <summary>烟测用关卡：取关卡索引里第一个含怪物配置的关卡，避免写死关卡 ID。</summary>
    private static CardSimulator.Battlefield.BattleLevelConfig FindSmokeLevel()
    {
        foreach (string line in LoadCsv.LoadCSVDataLines("res://DataBase/Level/LevelIndex.csv"))
        {
            string[] fields = LoadCsv.ParseCSVFields(line);
            if (fields.Length < 2 || !fields[1].StartsWith("res://", StringComparison.Ordinal)) continue;
            try
            {
                CardSimulator.Battlefield.BattleLevelConfig level = CardSimulator.Battlefield.BattleLevelCatalog.Load(fields[0]);
                if (level.Objects.Any(x => x.ObjectType == "Monster")) return level;
            }
            catch { }
        }
        return null;
    }

    private static Button FindToggleButton(Node node)
    {
        if (node is Button button && button.ToggleMode) return button;
        foreach (Node child in node.GetChildren())
        {
            Button found = FindToggleButton(child);
            if (found != null) return found;
        }
        return null;
    }

    private static CanvasLayer FindLayer(Node node, int layer)
    {
        if (node is CanvasLayer canvas && canvas.Layer == layer) return canvas;
        foreach (Node child in node.GetChildren())
        {
            CanvasLayer found = FindLayer(child, layer);
            if (found != null) return found;
        }
        return null;
    }

    private static Button FindButton(Node node, string text)
    {
        if (node is Button button && button.Text == text) return button;
        foreach (Node child in node.GetChildren())
        {
            Button found = FindButton(child, text);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>按片段匹配按钮文本：剧情选项的按钮文本会附带公开影响说明，不能按等值查找。</summary>
    private static Button FindButtonContaining(Node node, string fragment)
    {
        if (node is Button button && button.Text.Contains(fragment, StringComparison.Ordinal)) return button;
        foreach (Node child in node.GetChildren())
        {
            Button found = FindButtonContaining(child, fragment);
            if (found != null) return found;
        }
        return null;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
