using Godot;
using System;
using System.Collections.Generic;
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
    private CanvasLayer contentLayer, worldMapLayer, badgeLayer, modalLayer, abandonLayer, globalButtonLayer;
    // 结算界面（结算面板 + 卡牌三选一 + 待领取浮窗 + 放弃确认弹窗）常驻在本场景：
    // 内容重建不丢浮窗，节点进入前的拦截与 `Esc` 分层也有唯一出口。
    private SettlementUi settlementUi;
    // 当前内容宿主（RunBattleScene）：结算完成时由它推进节点并回地图。
    private RunBattleScene activeContent;
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
        badgeLayer = CreateLayer("SettlementBadgeLayer", RunUiLayers.SettlementBadge);
        modalLayer = CreateLayer("ModalLayer", RunUiLayers.Modal);
        abandonLayer = CreateLayer("AbandonConfirmLayer", RunUiLayers.AbandonConfirm);
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
        // 节点进入前置闸门（§7.1）：待领取态点任何类型节点都先弹放弃确认弹窗。
        map.EnterGate = OnNodeEnterRequested;
        settlementUi = new SettlementUi();
        AddChild(settlementUi);
        settlementUi.Bind(modalLayer, badgeLayer, abandonLayer);
        settlementUi.PanelClosed += OnSettlementPanelClosed;
        settlementUi.StateChanged += OnSettlementStateChanged;
        settlementUi.AbandonCommitted += OnSettlementAbandonCommitted;
        var run = RunSession.Instance;
        // 结算态优先：非战斗来源（事件 / 商人）发卡同样落 InSettlement，读档重进必须复现结算界面 / 浮窗，
        // 不能重播事件（§5.7 / §6.5）。
        if (run?.IsInSettlement == true) StartLevel(run.Current.PendingContentId);
        else if (run?.Current?.PendingContentType == "Event") StartEvent(run.Current.PendingContentId);
        else if (run?.Current?.PendingContentType == "Level" || run?.IsInBattleStart == true) StartLevel(run?.Current?.PendingContentId);
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
        content.ContentFinished += ReturnToSelectableMap; content.SettlementReady += OnSettlementReady;
        // 订阅必须在 AddChild 之前：InSettlement 读档重进时 RunBattleScene._Ready 就会广播结算就绪。
        activeContent = content; host.AddChild(content);
        CallDeferred(nameof(AttachMapToContent));
    }
    private void StartEvent(string id)
    {
        mapSelectable = false;
        activeBattle = null; // 上一份内容即将销毁：先断开引用，避免顶部按钮打到已释放节点。
        // 非战斗来源（事件 / 商人）发卡会进入同一个结算界面（§5.7）：结算的节点推进由本场景自己负责，
        // 因此这里仍然不挂战斗宿主（activeContent 保持 null，见 FinishSettlementContent）。
        activeContent = null;
        attachMapRetries = 0;
        ClearHost(); SetWorldMapVisible(false); map.SetReadOnly(true);
        var content = GD.Load<PackedScene>("res://Scenes/Run/RunEventScene.tscn").Instantiate<RunEventScene>();
        content.ContentFinished += ReturnToSelectableMap; content.LevelRequested += StartLevel;
        content.SettlementReady += OnEventSettlementReady;
        host.AddChild(content);
        CallDeferred(nameof(AttachMapToContent));
    }
    private void ToggleMap()
    {
        // 结算面板 / 放弃确认弹窗打开期间地图只读：按钮不再开合地图，避免面板之下透出可操作地图（§九）。
        if (settlementUi != null && (settlementUi.IsPanelOpen || settlementUi.IsConfirmOpen))
        {
            return;
        }

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
    // ── 结算界面与放弃闸门（交互案 §6 / §7 / §九） ────────────────────

    /// <summary>结算已落档：按存档复现面板 / 浮窗（读档重进只看 `SettlementPanelClosed`，§6.5）。</summary>
    private void OnSettlementReady(int refundedStolenGold)
    {
        if (settlementUi == null) return;
        settlementUi.RefundedStolenGold = refundedStolenGold;
        settlementUi.RefreshFromSave();
        if (settlementUi.IsPanelOpen) map.SetReadOnly(true);
    }

    /// <summary>
    /// 非战斗来源（事件 / 商人）发卡落档（§5.7）：与战斗结算共用同一界面与浮窗；来源没有战斗追回金币，
    /// 参数恒 0。结算面板是模态层（40），高于内容层（10），因此先让剧情 UI 让位（与打开世界地图同一口径）。
    /// </summary>
    private void OnEventSettlementReady()
    {
        activeBattle?.SetWorldMapOpen(true);
        OnSettlementReady(0);
    }

    /// <summary>
    /// 面板关闭（§6.2 / §7.3 / 新案 §三）：浮窗接管；有实机战场时**停留战场**，全部领完则视为结算完成、直接回地图。
    /// </summary>
    private void OnSettlementPanelClosed()
    {
        if (settlementUi.HasUnclaimed && HasLiveBattlefield())
        {
            // 战斗来源：地图只转可选、不自动打开 —— 玩家可以立刻在战场上操作（选人 / 悬停详情 / 战后移动）。
            StayOnBattlefield();
            return;
        }

        ReturnToSelectableMap();
        if (settlementUi.HasUnclaimed) return;
        FinishSettlementContent();
    }

    /// <summary>当前内容是否有实机战场（读档重进的结算界面只有结算 UI，没有可操作的战场）。</summary>
    private bool HasLiveBattlefield() =>
        activeContent != null && GodotObject.IsInstanceValid(activeContent) && activeContent.HasLiveBattlefield;

    /// <summary>
    /// 停留战场（新案 §三）：地图转可选但**不打开**，战场保持可见可点，按钮栏文案回「地图」。
    /// 要离开时点常驻栏「地图」→ 可选地图 → 选节点（仍有未领取项时先过放弃闸门）。
    /// </summary>
    private void StayOnBattlefield()
    {
        mapSelectable = true;
        SetWorldMapVisible(false);
        map.SetReadOnly(false);
        ConfigureGlobalTopBar(FindBattle(host));
        // 战后战场操作态（新案 §四 / §五）：只保留「切换角色」与「自由移动」，
        // 隐藏手牌槽 / 能量 / 牌堆 / 结束回合。未结束的战斗由 HexBattleScene.SetPostSettlementMode 自行拒绝（防软锁）。
        if (activeBattle != null && GodotObject.IsInstanceValid(activeBattle)) activeBattle.SetPostSettlementMode(true);
    }

    /// <summary>面板打开时地图只读（§九）；待领取态 / 浮窗下地图可选。</summary>
    private void OnSettlementStateChanged()
    {
        if (settlementUi != null && settlementUi.IsPanelOpen) map.SetReadOnly(true);
    }

    /// <summary>确认放弃后重放被拦下的那次节点进入（§7.1 / §7.3）。</summary>
    private void OnSettlementAbandonCommitted()
    {
        ReturnToSelectableMap();
        map.ReplayPendingNodeEnter();
    }

    /// <summary>
    /// 节点进入前置闸门（§7.1，与节点类型无关）：待领取态仍有未领取项 → 先弹放弃确认弹窗，不直接进入；
    /// 本局已勾选「本局游戏内不再显示」→ 直接作废未领取项并放行。返回 false = 本次点击不进入。
    /// </summary>
    public bool OnNodeEnterRequested(int nodeId)
    {
        if (settlementUi == null || settlementUi.IsPanelOpen || settlementUi.IsConfirmOpen)
        {
            return false;
        }

        if (!settlementUi.HasUnclaimed)
        {
            return true;
        }

        if (RunSession.Instance?.Current?.SuppressAbandonSettlementConfirm == true)
        {
            settlementUi.CommitAbandon(SettlementRewardPresenter.AbandonModeSuppressed);
            return true;
        }

        settlementUi.ShowAbandonConfirm();
        return false;
    }

    /// <summary>结算内容完成（全部领取后关闭）：清结算态并回到可选地图；没有内容宿主时直接清存档状态。</summary>
    private void FinishSettlementContent()
    {
        if (activeContent != null && GodotObject.IsInstanceValid(activeContent))
        {
            activeContent.FinishSettlementToMap();
            return;
        }

        // 非战斗来源（事件 / 商人）没有内容宿主：节点推进与清结算态由宿主代做，
        // 与 RunBattleScene.FinishSettlementToMap 的顺序一致（先标记节点，再清结算态）。
        RunSession run = RunSession.Instance;
        run?.MarkCurrentNodeVisitedAndAdvanceEncounter();
        run?.CompleteSettlementToMap();
        ReturnToSelectableMap();
    }

    /// <summary>
    /// `Esc` 逐层关闭（§九）：放弃确认弹窗 → 卡牌三选一 → 结算面板 → 待领取态的地图。返回 true = 已消费。
    /// 战场内部的 Esc（取消施法 / 移动规划 / 暂停）由 HexBattleScene 先处理，处理不到才回调本方法。
    /// </summary>
    private bool HandleEscapeLayers()
    {
        if (settlementUi != null && settlementUi.HandleEscape())
        {
            return true;
        }

        if (settlementUi?.HasUnclaimed == true && map.Visible && mapSelectable)
        {
            ToggleMap(); // 待领取态 + 地图打开 → 关闭地图回内容画面（浮窗仍在，仍是待领取态）
            return true;
        }

        return false;
    }

    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.Escape)
        {
            return;
        }

        // 有战场视图时 Esc 由 HexBattleScene 先接管（它消费后不会再到这里）；这里只覆盖「没有战场视图」的形态
        // （读档重进的结算界面等）。
        if (HandleEscapeLayers())
        {
            GetViewport()?.SetInputAsHandled();
        }
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
        // 战场内的 `Esc` 分工：本场没有内部状态可退出时回调宿主逐层关闭（结算面板 / 浮窗 / 地图，§九）。
        battle.EscapeFallback = HandleEscapeLayers;
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

            // 结算界面（交互案 §十一 1 / 2 / 6 / 7 / 8）：面板 → 关闭 → 待领取浮窗 → 点浮窗重开 → 放弃确认。
            RunSession run = RunSession.Instance;
            SettlementUi.ResetLastAbandonLog();
            int smokeSlotCharacterId = run.Current.CharacterSlots.Count > 0 ? run.Current.CharacterSlots[0].CharacterId : 0;
            run.EnterSettlement(new SettlementStartRequest
            {
                SourceName = "烟测结算",
                SourceNodeType = MapNodeType.NormalCombat,
                DropTableId = 0,
                CardPools = new List<SettlementCardPoolSave>
                {
                    new SettlementCardPoolSave
                    {
                        SlotIndex = 0,
                        CharacterId = smokeSlotCharacterId,
                        CandidateCardIds = new List<int>(),
                    },
                },
                LossTier = 0,
                ValueRatio = 1.0,
            });
            StartLevel(string.Empty);
            await WaitFrames(3);
            Require(FindButtonByName(this, "SettlementCloseButton")?.IsVisibleInTree() == true, "结算面板未提供“关闭”按钮。");
            string smokeSlotName = run.GetSlotDisplayName(0);
            Require(FindButton(this, SettlementRewardPresenter.GetCardTabText(smokeSlotName)) != null,
                $"卡牌 Tab 文案应为“{SettlementRewardPresenter.GetCardTabText(smokeSlotName)}”。");
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-settlement-panel.png");
            FindButtonByName(this, "SettlementCloseButton").EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(run.IsInSettlement, "关闭结算面板后应保持 InSettlement（待领取态）。");
            Button badge = FindButtonContaining(this, "未领取");
            Require(badge?.IsVisibleInTree() == true, "关闭结算面板后应出现待领取浮窗。");
            Require(map.Visible && !map.IsReadOnly, "关闭结算面板后地图应可选。");
            badge.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(FindButtonByName(this, "SettlementCloseButton")?.IsVisibleInTree() == true, "点浮窗应重新打开结算面板。");
            Require(FindButtonContaining(this, "未领取")?.IsVisibleInTree() != true, "面板打开时不应再显示待领取浮窗。");
            // Esc 逐层关闭（§九）：面板 → 关闭（待领取态）；待领取态 + 地图打开 → 关闭地图。
            PushEscape();
            await WaitFrames(2);
            Require(run.IsInSettlement && FindButtonContaining(this, "未领取")?.IsVisibleInTree() == true, "面板上按 Esc 应关闭面板并进入待领取态。");
            Require(map.Visible, "待领取态应保持地图打开。");
            PushEscape();
            await WaitFrames(2);
            Require(!map.Visible, "待领取态 + 地图打开时按 Esc 应关闭地图。");
            Require(run.IsInSettlement && FindButtonContaining(this, "未领取")?.IsVisibleInTree() == true, "关闭地图后仍应保持待领取态与浮窗。");
            // 待领取态点节点 → 先弹放弃确认弹窗；「取消」不放弃、不打印日志（§7.1 / §7.2）。
            Require(FindButtonByName(this, "SettlementCloseButton")?.IsVisibleInTree() != true, "Esc 关闭后结算面板不应还在。");
            Require(!OnNodeEnterRequested(0), "待领取态点节点应先弹出放弃确认弹窗。");
            await WaitFrames(1);
            Require(FindLabel(this, SettlementUi.AbandonTitleText)?.IsVisibleInTree() == true, "放弃确认弹窗应显示标题「放弃未领取的战利品？」。");
            Require(FindButton(this, SettlementUi.AbandonConfirmText)?.IsVisibleInTree() == true, "放弃确认弹窗缺少“确认放弃并进入”。");
            FindButton(this, SettlementUi.CancelText).EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(1);
            Require(string.IsNullOrEmpty(SettlementUi.LastAbandonLog), "点“取消”不应打印放弃日志。");
            Require(run.IsInSettlement, "点“取消”后应保持待领取态、不放弃任何内容。");
            // 「确认放弃并进入」→ 打印一条 SETTLEMENT_ABANDONED、浮窗消失、转 OnMap（§7.3 / §7.4）。
            Require(!OnNodeEnterRequested(0), "确认前应再次被放弃确认弹窗拦截。");
            await WaitFrames(1);
            FindButton(this, SettlementUi.AbandonConfirmText).EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(SettlementUi.LastAbandonLog?.Contains("SETTLEMENT_ABANDONED") == true,
                $"确认放弃后应打印 SETTLEMENT_ABANDONED 日志，实际：{SettlementUi.LastAbandonLog ?? "<空>"}");
            Require(!run.IsInSettlement, "确认放弃后应转 OnMap。");
            Require(FindButtonContaining(this, "未领取") == null, "放弃后待领取浮窗应消失。");
            Require(map.Visible && !map.IsReadOnly && map.MouseFilter == MouseFilterEnum.Stop, "放弃后地图必须是可选态并能接收输入。");

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

            SettlementUi.ResetLastAbandonLog();
            Require(FindButtonByName(this, "SettlementCloseButton")?.IsVisibleInTree() == true, "关卡胜利后应弹出结算面板。");
            Require(run.Current.SettlementCardPools.Count > 0, "关卡应给出至少一份卡牌奖励（每个角色槽位一份）。");
            Require(FindLabelContaining(modalLayer, "卡牌奖励减少") == null, "全歼（击败比例 100%）不应显示折损行。");
            // 卡牌三选一：候选必须用 CardDisplayPrefab 展示，卡下方显示该份角色的显示名（§5.2 / §5.3）。
            Button realCardTab = FindButtonContaining(modalLayer, "将一张牌添加到你的牌组。· ");
            Require(realCardTab != null, "结算面板缺少卡牌 Tab。");
            realCardTab.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Require(FindButtonByName(this, "SettlementCardSkipButton")?.IsVisibleInTree() == true, "卡牌三选一缺少“跳过”。");
            Require(FindCardPickButton(modalLayer) != null, "卡牌三选一未用 CardDisplayPrefab 展示候选。");
            int firstSlot = run.Current.SettlementCardPools[0].SlotIndex;
            string firstSlotName = SettlementRewardPresenter.GetSlotDisplayName(run.Current.SettlementCardPools[0], run.GetSlotDisplayName);
            Require(FindLabel(modalLayer, firstSlotName)?.IsVisibleInTree() == true, $"卡牌下方应显示归属角色名“{firstSlotName}”。");
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-settlement-pick.png");
            // Esc = 跳过（§5.5）：只关三选一、不透传成关闭结算面板，该份仍未领取。
            PushEscape();
            await WaitFrames(2);
            Require(FindButtonByName(this, "SettlementCardSkipButton")?.IsVisibleInTree() != true, "三选一内按 Esc 应关闭三选一。");
            Require(FindButtonByName(this, "SettlementCloseButton")?.IsVisibleInTree() == true, "三选一按 Esc 后不应连结算面板一起关闭。");
            Require(!SettlementRewardPresenter.IsCardPoolClaimed(run.Current, firstSlot), "Esc 跳过不应把该份卡牌标记为已领取。");
            // 「跳过」按钮同义：不改状态，该份仍未领取、可再次点开（§5.5 / 验收 6）。
            FindButtonByName(this, SettlementUi.CardTabButtonNamePrefix + firstSlot).EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Require(FindButtonByName(this, "SettlementCardSkipButton")?.IsVisibleInTree() == true, "卡牌三选一缺少“跳过”。");
            FindButtonByName(this, "SettlementCardSkipButton").EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Require(!SettlementRewardPresenter.IsCardPoolClaimed(run.Current, firstSlot), "跳过不应把该份卡牌标记为已领取。");
            Require(string.IsNullOrEmpty(SettlementUi.LastAbandonLog), "跳过不应打印放弃日志。");
            // 点候选卡 → 立即领取并回到面板，只有该份转「已领取」（§5.4 / 验收 5）。
            FindButtonByName(this, SettlementUi.CardTabButtonNamePrefix + firstSlot).EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            FindCardPickButton(modalLayer).EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(SettlementRewardPresenter.IsCardPoolClaimed(run.Current, firstSlot), "选中候选后该份应落档为已领取。");
            Require(FindButtonContaining(this, "已领取") != null, "领取后该份卡牌 Tab 应显示「已领取」。");
            // 关闭面板 → **停留战场**（新案 §二 / §三）：有实机战场时不自动打开世界地图，浮窗常驻、战场仍可见可点。
            FindButtonByName(this, "SettlementCloseButton").EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(run.IsInSettlement, "关闭结算面板后应保持 InSettlement（待领取态）。");
            Require(FindButtonContaining(this, "未领取")?.IsVisibleInTree() == true, "关闭结算面板后应出现待领取浮窗。");
            Require(!map.Visible, "有实机战场时关闭结算面板不应自动打开世界地图。");
            Require(!map.IsReadOnly, "停留战场时地图应已转为可选（点「地图」即可选点）。");
            Require(realBattle.MapView.Visible, "停留战场时战场必须保持可见。");
            Require(mapButton.Text == "地图", $"停留战场时「地图」按钮文案应为「地图」，实际：{mapButton.Text}。");
            // 战后战场操作态（新案 §四 / §五 / §九 6–10）：只保留「切换角色」与「自由移动」。
            Require(realBattle.IsPostSettlementMode, "关闭结算面板后战场应进入战后战场操作态。");
            Require(realBattle.PostSettlementUiCollapsed,
                "战后应隐藏手牌槽 / 能量与额度面板 / 结束回合，且移动按钮文案不含「能量」。");
            int postMover = realBattle.Session.SelectedId;
            AxialHex moverFrom = realBattle.Session.Occupancy.Placements[postMover].Coord;
            int postEnergy = realBattle.Session.Occupancy.Placements[postMover].Unit.Energy;
            int postMovesUsed = realBattle.Session.Occupancy.Placements[postMover].MovesUsedThisTurn;
            int postRound = realBattle.Session.Round;
            List<AxialHex> postReachable = realBattle.Session.Movement.ReachableCells(postMover).ToList();
            Require(postReachable.Count > 0, "战后应有可达格（连通可达区域）。");
            Require(postReachable.Count > realBattle.Session.Movement.LegalDestinations(postMover).Count,
                "战后可达格应多于战斗内一次动作的合法格（不限移动额度）。");
            // 点「移动」→ 规划态；再点可达格 → 立即移动到位（没有「确定移动 / 取消移动」两步确认）。
            Button postMoveButton = FindButton(host, "移动");
            Require(postMoveButton?.IsVisibleInTree() == true, "战后应保留文案为「移动」的按钮。");
            postMoveButton.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Require(realBattle.IsMovePlanning, "点「移动」应进入战后移动规划态。");
            AxialHex postTarget = postReachable[^1];
            Require(postTarget != moverFrom, "战后移动目标不应是角色当前格。");
            realBattle.MapView._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = true, Position = realBattle.MapView.CellPosition(postTarget),
            });
            await WaitFrames(8);
            // 移动表现逐格播放（逻辑已同步到位）：等它播完，后续点击才不会被「上一段移动仍在结算」挡住。
            int presentationFrames = 0;
            while (realBattle.MapView.HasPendingPresentation && presentationFrames++ < 900) await WaitFrames(1);
            Require(!realBattle.MapView.HasPendingPresentation, "战后移动的逐格表现未在限定帧内播完。");
            Require(realBattle.Session.Occupancy.Placements[postMover].Coord == postTarget,
                $"战后点可达格应立即移动到位，实际 {realBattle.Session.Occupancy.Placements[postMover].Coord}。");
            Require(realBattle.Session.Occupancy.Placements[postMover].Unit.Energy == postEnergy &&
                realBattle.Session.Occupancy.Placements[postMover].MovesUsedThisTurn == postMovesUsed,
                "战后移动不得消耗能量与移动次数。");
            Require(realBattle.Session.Phase == CardSimulator.Battlefield.BattlefieldSession.BattlePhase.Victory && realBattle.Session.Round == postRound,
                "战后移动不得改变战斗阶段与回合数。");
            // 点自己人 = 切换选中角色（角色 Tab 行保留），不发生移动。
            int postOther = realBattle.Session.PlayerIds.First(id => id != postMover);
            AxialHex postOtherCoord = realBattle.Session.Occupancy.Placements[postOther].Coord;
            realBattle.MapView._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = true, Position = realBattle.MapView.CellPosition(postOtherCoord),
            });
            await WaitFrames(2);
            Require(realBattle.Session.SelectedId == postOther, "战后点自己人应切换选中角色。");
            Require(realBattle.Session.Occupancy.Placements[postMover].Coord == postTarget,
                "战后点自己人不得把已选角色移走。");
            // 点不可达格 → 只打日志、不移动、不退出规划态（§五 6）。
            AxialHex? postBlocked = realBattle.Session.Board.Cells.Keys
                .Where(c => c != postOtherCoord && !postReachable.Contains(c) && realBattle.Session.Occupancy.At(c) == null)
                .Cast<AxialHex?>().FirstOrDefault();
            if (postBlocked.HasValue)
            {
                AxialHex postOtherFrom = realBattle.Session.Occupancy.Placements[postOther].Coord;
                realBattle.MapView._GuiInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left, Pressed = true, Position = realBattle.MapView.CellPosition(postBlocked.Value),
                });
                await WaitFrames(3);
                Require(realBattle.Session.Occupancy.Placements[postOther].Coord == postOtherFrom,
                    "战后点不可达格不得移动。");
                Require(realBattle.IsMovePlanning, "战后点不可达格不得退出规划态。");
            }
            else
            {
                GD.Print("RUN_FLOW_UI_SMOKE_NOTE: 本关没有不可达空格，跳过战后不可达点击断言。");
            }
            // 被隐藏的「结束回合」不得推进回合 / 触发敌人回合（§九 9）。
            FindButton(host, "结束回合")?.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Require(realBattle.Session.Phase == CardSimulator.Battlefield.BattlefieldSession.BattlePhase.Victory && realBattle.Session.Round == postRound,
                "战后点「结束回合」不得改变阶段 / 回合。");
            // Esc 只退出规划态：不清结算态、不放弃、不回地图（新案 §六 4）。
            PushEscape();
            await WaitFrames(2);
            Require(!realBattle.IsMovePlanning, "战后规划态按 Esc 应退出规划态。");
            Require(run.IsInSettlement && FindButtonContaining(this, "未领取")?.IsVisibleInTree() == true,
                "战后退出规划态后应仍是待领取态且浮窗仍在。");
            Require(realBattle.PostSettlementUiCollapsed, "退出规划态后仍应是战后操作态表现（移动按钮文案回「移动」）。");

            // ── 位置落档（交互案 §七 4 改口径：落档）──
            // 战后布局写进本局存档：进入战后操作态时落一次，此后每次战后移动 / 拾取再落一次。
            RunPostBattleSave postSave = run.Current.PostBattleBattlefield;
            Require(postSave != null, "战后操作态的布局应落档（PostBattleBattlefield）。");
            Require(postSave.LevelId == smokeLevel.LevelId, $"战后落档应带当前关卡 ID，实际 {postSave.LevelId}。");
            Require(!string.IsNullOrEmpty(postSave.MapId), "战后落档应带重建战场用的地图 ID。");
            Require(postSave.Round == postRound, $"战后落档应记录当前回合 {postRound}，实际 {postSave.Round}。");
            Require(postSave.Loadouts.Count == run.Current.CharacterSlots.Count, "战后落档应含每个角色槽的随身与手位。");
            RunUnitPlacementSave selectedInSave = postSave.Units.FirstOrDefault(u => u.SlotIndex == postSave.SelectedSlotIndex);
            Require(selectedInSave != null, "战后落档应含选中角色槽的位置。");
            AxialHex selectedNow = realBattle.Session.Occupancy.Placements[realBattle.Session.SelectedId].Coord;
            Require(selectedInSave.Q == selectedNow.Q && selectedInSave.R == selectedNow.R,
                $"战后移动后应把新坐标落档，落档 {selectedInSave.Q},{selectedInSave.R} / 实际 {selectedNow}。");
            Require(RunSaveJson.Deserialize(RunSaveJson.Serialize(run.Current)).PostBattleBattlefield != null,
                "战后落档必须能通过存档 JSON 往返。");
            Require(FileAccess.FileExists(RunSession.SavePath) &&
                FileAccess.GetFileAsString(RunSession.SavePath).Contains("PostBattleBattlefield"),
                "战后落档必须真的写进存档文件。");

            // ── 战后拾取（交互案 §七 3 改口径：可拾取）──
            int pickMover = realBattle.Session.SelectedId;
            AxialHex pickCell = realBattle.Session.Occupancy.Placements[pickMover].Coord;
            Require(realBattle.Session.Board.TryAddObject(pickCell,
                new CardSimulator.Battlefield.GroundObject("smoke-post-item", "测试道具", CardSimulator.Battlefield.GroundObjectKind.Item), out string postItemError),
                "无法在战后当前格放置测试道具：" + postItemError);
            Require(realBattle.TryPostBattlePickup("smoke-post-item", 0, null, out string pickupMessage),
                "战后应能把当前格道具拾取到随身槽：" + pickupMessage);
            Require(realBattle.Session.SelectedLoadout.Items[0]?.InstanceId == "smoke-post-item", "拾取后随身道具槽 1 应持有该道具。");
            Require(realBattle.Session.Board.Cells[pickCell].Items.All(x => x.InstanceId != "smoke-post-item"), "拾取后该道具应从地面移除。");
            RunPostBattleSave afterPickup = run.Current.PostBattleBattlefield;
            Require(afterPickup.GroundObjects.All(x => x.InstanceId != "smoke-post-item"), "拾取结果应落档：地面物件不再含该道具。");
            Require(afterPickup.Loadouts.Any(l => l.Items.Any(x => x != null && x.InstanceId == "smoke-post-item")), "拾取结果应落档：随身栏含该道具。");

            // ── 落档还原（读档重进结算界面走同一条路径）──
            // 先把状态改乱（角色走开、已拾取道具塞回地面），再按落档快照还原：位置 / 随身 / 地面都应回到落档当刻。
            RunPostBattleSave restoreSnapshot = run.Current.PostBattleBattlefield;
            AxialHex awayCell = realBattle.Session.Movement.ReachableCells(pickMover).First();
            Require(realBattle.Session.TryPostBattleMove(pickMover, awayCell, out string awayError), "战后应能移动到别的可达格：" + awayError);
            int awayFrames = 0;
            while (realBattle.MapView.HasPendingPresentation && awayFrames++ < 900) await WaitFrames(1);
            Require(realBattle.Session.Occupancy.Placements[pickMover].Coord == awayCell, "改乱用例：角色应先离开落档格。");
            Require(realBattle.Session.Board.TryAddObject(awayCell,
                new CardSimulator.Battlefield.GroundObject("smoke-post-item", "测试道具", CardSimulator.Battlefield.GroundObjectKind.Item), out _),
                "改乱用例需要先把已拾取道具塞回地面。");
            Require(realBattle.ApplyPostBattleSnapshot(restoreSnapshot, out string restoreError), "按落档快照还原战后战场失败：" + restoreError);
            Require(realBattle.Session.Occupancy.Placements[pickMover].Coord == pickCell,
                $"还原后角色应回到落档坐标 {pickCell}，实际 {realBattle.Session.Occupancy.Placements[pickMover].Coord}。");
            Require(realBattle.Session.Board.Cells[awayCell].Items.All(x => x.InstanceId != "smoke-post-item"), "还原后地面不应再出现已拾取道具。");
            Require(realBattle.Session.SelectedLoadout.Items[0]?.InstanceId == "smoke-post-item", "还原后随身栏应仍持有落档道具。");
            Require(realBattle.Session.Phase == CardSimulator.Battlefield.BattlefieldSession.BattlePhase.Victory && realBattle.IsPostSettlementMode,
                "还原后仍应是战后操作态（结算态与战场表现未被破坏）。");

            // ── 读档重进结算界面：内容宿主按落档重建同一张战场（`RunBattleScene` 的 InSettlement 分支）──
            // 临时实例化一个内容宿主走同一条 `_Ready` 路径：有落档 → 重建战场 + 还原布局 + 直接进入战后操作态。
            var reloadedContent = new RunBattleScene();
            AddChild(reloadedContent);
            await WaitFrames(2);
            Require(reloadedContent.HasLiveBattlefield, "有战后落档时，重进结算界面应重建出实机战场。");
            HexBattleScene reloadedBattle = reloadedContent.BattleView;
            Require(reloadedBattle.IsPostSettlementMode, "重建的战场应直接进入战后操作态（手牌 / 能量 / 结束回合已收起）。");
            Require(reloadedBattle.Session.Phase == CardSimulator.Battlefield.BattlefieldSession.BattlePhase.Victory,
                "重建的战场应处于胜利（结算态）。");
            Require(reloadedBattle.Session.Occupancy.Placements
                    .Values.Any(p => p.Role == CardSimulator.Battlefield.BattlefieldRole.Player && p.Coord == pickCell),
                "重建的战场应把角色还原到落档坐标。");
            Require(reloadedBattle.Session.SelectedLoadout.Items.Any(x => x?.InstanceId == "smoke-post-item"),
                "重建的战场应还原落档的随身道具（战后拾取结果）。");
            Require(reloadedBattle.Session.Board.Cells.Values.All(c => c.Items.All(x => x.InstanceId != "smoke-post-item")),
                "重建的战场不应把已拾取道具放回地面。");
            reloadedContent.QueueFree();
            await WaitFrames(2);

            // 点「地图」→ 打开可选地图；点「返回」→ 回战场（浮窗仍在）——「停留战场」不等于「不能去地图」。
            mapButton.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(map.Visible && !map.IsReadOnly && mapButton.Text == "返回", "停留战场后点「地图」应打开可选地图。");
            mapButton.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(!map.Visible && run.IsInSettlement, "点「返回」应回到战场并保持待领取态。");
            Require(FindButtonContaining(this, "未领取")?.IsVisibleInTree() == true, "回到战场后待领取浮窗应仍在。");
            // 点浮窗 → 重新打开结算面板，继续走领取流程。
            FindButtonContaining(this, "未领取").EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(FindButtonByName(this, "SettlementCloseButton")?.IsVisibleInTree() == true, "点浮窗应重新打开结算面板。");
            // 领完物品 Tab：点击即领取、面板会重建，因此每次都要重新查找（§四 / 验收 2）。
            int claimGuard = 0;
            while (SettlementRewardPresenter.CountUnclaimedItems(run.Current, LoadingSystem.DropTableEntries) > 0 && claimGuard++ < 12)
            {
                Button itemTab = FindUnclaimedItemTab(modalLayer);
                Require(itemTab != null, "有未领取物品 Tab 却找不到可点条目。");
                itemTab.EmitSignal(BaseButton.SignalName.Pressed);
                await WaitFrames(2);
            }
            // 把剩下的卡牌份也领掉，验证「全部领取后关闭 = 结算完成、无浮窗」（§7.3 / 验收 10）。
            int cardGuard = 0;
            while (SettlementRewardPresenter.CountUnclaimedCards(run.Current) > 0 && cardGuard++ < 6)
            {
                int slot = FindUnclaimedCardSlot(run.Current);
                Require(slot >= 0, "有未领取卡牌份却找不到份槽位。");
                FindButtonByName(modalLayer, SettlementUi.CardTabButtonNamePrefix + slot).EmitSignal(BaseButton.SignalName.Pressed);
                await WaitFrames(2);
                Button pick = FindCardPickButton(modalLayer);
                if (pick == null)
                {
                    // 该角色卡池为空（该份只能「跳过」）：停止循环，交给下面的放弃分支收尾。
                    FindButtonByName(this, "SettlementCardSkipButton")?.EmitSignal(BaseButton.SignalName.Pressed);
                    await WaitFrames(1);
                    break;
                }

                pick.EmitSignal(BaseButton.SignalName.Pressed);
                await WaitFrames(3);
            }

            if (SettlementRewardPresenter.CountUnclaimed(run.Current, LoadingSystem.DropTableEntries) > 0)
            {
                // 仍有不可领取的份：走「确认放弃并进入」收尾（§7.3）。
                Require(!OnNodeEnterRequested(0), "待领取态点节点应先弹放弃确认弹窗。");
                await WaitFrames(1);
                FindButton(this, SettlementUi.AbandonConfirmText).EmitSignal(BaseButton.SignalName.Pressed);
                await WaitFrames(3);
            }
            else
            {
                FindButtonByName(this, "SettlementCloseButton").EmitSignal(BaseButton.SignalName.Pressed);
                await WaitFrames(8);
            }

            Require(!run.IsInSettlement, "结算完成后应转 OnMap。");
            Require(FindButtonContaining(this, "未领取") == null, "结算完成后不应出现待领取浮窗。");

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

            // 非战斗来源发卡（交互案 §5.7）：事件效果 `CardAdd` → 与战斗结算**同一界面**，不折损、同样有浮窗与放弃闸门。
            // 夹具事件 EVT-CARD-001 只在 EventIndex 登记（不进 EventPool：正式流程不可达，兼作配置示例）。
            if (map.Visible) { ToggleMap(); await WaitFrames(2); }
            RunSession eventRun = RunSession.Instance;
            int slotOneDeckBefore = eventRun.GetSlotDeck(1).Count;
            eventRun.BeginRunEvent("EVT-CARD-001", eventRun.Current.MapState.CurrentNodeId, MapNodeType.Merchant);
            StartEvent("EVT-CARD-001");
            await WaitFrames(5);
            HexBattleScene cardEvent = FindBattle(host);
            Require(cardEvent?.ActiveStoryOverlay?.Visible == true, "夹具事件应显示剧情 UI。");
            Require(skipRow.Visible && skipButton.Visible, "夹具事件（canSkip）应提供“跳过”。");
            skipButton.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Button cardSkipConfirm = FindButton(cardEvent, "确认跳过");
            Require(cardSkipConfirm?.IsVisibleInTree() == true, "夹具事件跳过确认面板缺少“确认跳过”。");
            cardSkipConfirm.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Button cardChoice = FindButtonContaining(cardEvent, "整只行囊搬走");
            Require(cardChoice?.IsVisibleInTree() == true, "夹具事件缺少带公开影响（effectPreview）的发卡选项。");
            cardChoice.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(5);

            Require(eventRun.IsInSettlement, "事件发卡后应落 InSettlement（§5.7）。");
            Require(eventRun.Current.SettlementCardPools.Count == 2, $"事件发卡应生成 2 份（每份一个槽位），实际 {eventRun.Current.SettlementCardPools.Count}。");
            Require(eventRun.Current.SettlementLossTier == 0 && eventRun.Current.SettlementValueRatio == 1.0, "非战斗来源不做折损（档位 0 / 比例 1.0）。");
            Require(eventRun.Current.SettlementSourceNodeType == (int)MapNodeType.Merchant, "结算来源类型应为商人节点（BeginRunEvent 传入）。");
            Require(FindButtonByName(this, "SettlementCloseButton")?.IsVisibleInTree() == true, "事件发卡后应弹出与战斗结算相同的面板。");
            Require(cardEvent.ActiveStoryOverlay.Visible == false, "结算面板打开时剧情 UI 应让位。");
            Require(FindLabelContaining(modalLayer, "卡牌奖励减少") == null, "非战斗来源不应出现折损行。");
            Require(FindUnclaimedItemTab(modalLayer) == null, "非战斗来源不绑战斗掉落表，不应出现物品 Tab。");
            Require(CountButtonsByNamePrefix(modalLayer, SettlementUi.CardTabButtonNamePrefix) == 2, "事件发卡应有两个卡牌份 Tab。");
            Require(FindButton(this, SettlementRewardPresenter.GetCardTabText(eventRun.GetSlotDisplayName(0))) != null, "卡牌 Tab 文案应为该份角色的显示名。");
            Require(FindButton(this, SettlementRewardPresenter.GetCardTabText(eventRun.GetSlotDisplayName(1))) != null, "第二份卡牌 Tab 文案应为该份角色的显示名。");
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-event-card.png");

            // 任选份：候选 3 张来自该份角色自己的卡池；「跳过」不领取、不改状态（§5.3 / §5.5）。
            FindButtonByName(this, SettlementUi.CardTabButtonNamePrefix + 0).EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            int looseCandidates = CountButtonsByNamePrefix(modalLayer, SettlementUi.CardPickButtonNamePrefix);
            Require(looseCandidates == 3, $"事件任选份的候选应为 3 张，实际 {looseCandidates}。");
            FindButtonByName(this, "SettlementCardSkipButton").EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(2);
            Require(!SettlementRewardPresenter.IsCardPoolClaimed(eventRun.Current, 0), "跳过不应把任选份标记为已领取。");
            // 指定具体卡的份：候选恒为这 1 张；领取后写进该份自带槽位的牌库（§5.7）。
            FindButtonByName(this, SettlementUi.CardTabButtonNamePrefix + 1).EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            int fixedCandidates = CountButtonsByNamePrefix(modalLayer, SettlementUi.CardPickButtonNamePrefix);
            Require(fixedCandidates == 1, $"来源指定具体卡的份候选应恒为 1 张，实际 {fixedCandidates}。");
            FindCardPickButton(modalLayer).EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            SettlementCardClaimSave eventClaim = SettlementRewardPresenter.FindCardClaim(eventRun.Current, 1);
            Require(eventClaim != null && eventClaim.CardId == 10000001, $"事件指定卡应落档到槽位 1、卡牌 10000001，实际 {eventClaim?.CardId}。");
            Require(eventRun.GetSlotDeck(1).Count == slotOneDeckBefore + 1, "事件发卡的指定卡应写入槽位 1 的牌库。");

            // 关闭 → 待领取态：浮窗显示来源名（事件标题）与剩余未领取项数（§6.3 / §5.7）。
            FindButtonByName(this, "SettlementCloseButton").EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(eventRun.IsInSettlement && map.Visible && !map.IsReadOnly, "关闭事件结算面板后应进入待领取态且地图可选。");
            Button eventBadge = FindButtonContaining(this, "未领取");
            Require(eventBadge?.IsVisibleInTree() == true, "事件结算关闭后应出现待领取浮窗。");
            Require(eventBadge.Text.Contains("1 项未领取") && eventBadge.Text.Contains("结算夹具：发卡事件"),
                $"事件浮窗应显示未领取项数与来源名，实际：{eventBadge.Text.Replace("\n", " / ")}。");
            // 待领取态点节点 → 先弹放弃确认弹窗；确认后打印一条带来源名 + 节点类型的日志（§7.1 / §7.4）。
            Require(!OnNodeEnterRequested(0), "事件待领取态点节点应先弹放弃确认弹窗。");
            await WaitFrames(1);
            SettlementUi.ResetLastAbandonLog();
            FindButton(this, SettlementUi.AbandonConfirmText).EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            string eventAbandonLog = SettlementUi.LastAbandonLog ?? string.Empty;
            Require(eventAbandonLog.Contains("SETTLEMENT_ABANDONED"), $"确认放弃事件未领取项应打印日志，实际：{eventAbandonLog}。");
            Require(eventAbandonLog.Contains("结算夹具：发卡事件") && eventAbandonLog.Contains(SettlementRewardPresenter.GetSourceNodeTypeText(MapNodeType.Merchant)),
                $"放弃日志应带事件来源名与节点类型，实际：{eventAbandonLog}。");
            Require(!eventRun.IsInSettlement, "放弃事件未领取项后应转 OnMap。");
            Require(FindButtonContaining(this, "未领取") == null, "放弃后事件浮窗应消失。");

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

    /// <summary>按节点名找按钮（结算界面用固定节点名定位：关闭 / 跳过 / 卡牌份 Tab）。</summary>
    private static Button FindButtonByName(Node node, string name)
    {
        if (node is Button button && button.Name.ToString() == name) return button;
        foreach (Node child in node.GetChildren())
        {
            Button found = FindButtonByName(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>数某前缀按钮的个数（烟测：卡牌奖励份数 / 三选一候选数）。</summary>
    private static int CountButtonsByNamePrefix(Node node, string prefix)
    {
        int count = node is Button button && button.Name.ToString().StartsWith(prefix, StringComparison.Ordinal) ? 1 : 0;
        foreach (Node child in node.GetChildren())
        {
            count += CountButtonsByNamePrefix(child, prefix);
        }
        return count;
    }

    /// <summary>找结算三选一的候选卡点击区（`CardPick_` 前缀，覆盖在 CardDisplayPrefab 卡面上）。</summary>
    private static Button FindCardPickButton(Node node)
    {
        if (node is Button button && button.Name.ToString().StartsWith(SettlementUi.CardPickButtonNamePrefix, StringComparison.Ordinal)) return button;
        foreach (Node child in node.GetChildren())
        {
            Button found = FindCardPickButton(child);
            if (found != null) return found;
        }
        return null;
    }

    private static Label FindLabel(Node node, string text)
    {
        if (node is Label label && label.Text == text) return label;
        foreach (Node child in node.GetChildren())
        {
            Label found = FindLabel(child, text);
            if (found != null) return found;
        }
        return null;
    }

    private static Label FindLabelContaining(Node node, string fragment)
    {
        if (node is Label label && label.Text.Contains(fragment, StringComparison.Ordinal)) return label;
        foreach (Node child in node.GetChildren())
        {
            Label found = FindLabelContaining(child, fragment);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>找结算面板里第一个未领取的物品 Tab（文案来自 `FormatRewardLine` 的固定前缀）。</summary>
    private static Button FindUnclaimedItemTab(Node node)
    {
        if (node is Button button && !button.Disabled && IsRewardLineText(button.Text)) return button;
        foreach (Node child in node.GetChildren())
        {
            Button found = FindUnclaimedItemTab(child);
            if (found != null) return found;
        }
        return null;
    }

    private static bool IsRewardLineText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.StartsWith("金币 +", StringComparison.Ordinal)
            || text.StartsWith("钥匙 +", StringComparison.Ordinal)
            || text.StartsWith("材料 ", StringComparison.Ordinal)
            || text.StartsWith("道具 ", StringComparison.Ordinal)
            || text.StartsWith("装备 ", StringComparison.Ordinal);
    }

    /// <summary>第一个尚未领取的卡牌份槽位（-1 = 全部领完）。</summary>
    private static int FindUnclaimedCardSlot(RunSaveData run)
    {
        if (run?.SettlementCardPools == null) return -1;
        foreach (SettlementCardPoolSave pool in run.SettlementCardPools)
        {
            if (pool != null && !SettlementRewardPresenter.IsCardPoolClaimed(run, pool.SlotIndex)) return pool.SlotIndex;
        }
        return -1;
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

    /// <summary>注入一次 Esc 按键（`Esc` 逐层关闭的烟测入口）：按下与抬起同帧，避免事件被合并丢掉。</summary>
    private void PushEscape()
    {
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
    }
}
