using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class RunFlowScene : Control
{
    /// <summary>
    /// 是否把本场景登记进本机 AI 接口（默认开，与 `HexBattleScene.EnableCommandApi` 同一口径）。
    /// 登记后 `run.*`（玩家通道）与 `debug.run.*`（调试通道）可用；端口首次登记时才真正占用。
    /// </summary>
    [Export] public bool EnableCommandApi = true;

    /// <summary>接口端口（默认 17880；同一进程只监听一个端口，后到的值会被忽略并打日志）。</summary>
    [Export] public int ApiPort = ApiService.DefaultPort;

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
    // 常驻顶栏的底板层（`RunUiLayers.TopBarBackdrop = 32`）：不透明横条铺满第一行那一带，
    // 把战场 / 世界地图挡在身后；层号压在内容层与世界地图之上、一切模态之下。
    private CanvasLayer topBarBackdropLayer;
    // 底板本体（`_Ready` 构建）：随常驻栏一起显隐（营地期间整条顶栏让位）。
    private Panel topBarBackdrop;
    // 营地（篝火休息）层：42 —— 压过结算模态、低于放弃确认与常驻按钮栏（RunUiLayers.Camp）。
    private CanvasLayer campLayer;
    // 营地实例（null = 未在营地）；「结束当天」与时间点不足的强制转场共用 OpenCamp。
    private CampScene camp;
    // 常驻栏左侧的时间点显示（第 9 条：天数 + 当天剩余，精度 0.1）与主动结束当天入口。
    private HBoxContainer timeRow;
    // 常驻栏右侧通用按钮行（地图 / 定位当前角色 / 调试 / 暂停，几何取自 RunUiLayout）：
    // 提成字段是为了让烟测能直接读它的实际矩形，断言「为背包 / 装备预留的容量」真的落在屏幕上。
    private HBoxContainer generalRow;
    private Label timePointLabel;
    private Button endDayButton;
    // 背包入口（用户口径 2026-10-02：上边栏**时间点 UI 的右边**）与背包界面实例（挂 Modal 层，单实例）。
    private Button bagButton;
    private BagUi bagUi;
    // 装备入口（用户口径 2026-10-02：「入口放在背包入口的右边」）与装备界面实例（挂 Modal 层，与背包互斥）。
    private Button equipButton;
    private EquipmentUi equipmentUi;
    // 每帧只做一次廉价比较，文案真的变了才写 Label（与剧情按钮行同一收敛口径）。
    private string shownTimePointText = string.Empty;
    // 结算界面（结算面板 + 卡牌三选一 + 待领取浮窗 + 放弃确认弹窗）常驻在本场景：
    // 内容重建不丢浮窗，节点进入前的拦截与 `Esc` 分层也有唯一出口。
    private SettlementUi settlementUi;
    // 当前内容宿主（RunBattleScene）：结算完成时由它推进节点并回地图。
    private RunBattleScene activeContent;
    private byte[] runSaveBackup;
    private bool runSaveExisted;
    // 食物 / 烹饪烟测（2026-10-02 批 C）在「添加食物」面板里规划的食物实例、过期实例与预期效果类型：
    // 休息结算后按这些字段断言「草稿消耗 + 效果落档 + 跨天腐坏」，避免烟测里重写一份期望值。
    private readonly List<string> campFoodPlanIds = new List<string>();
    private readonly List<string> campFoodExpiredIds = new List<string>();
    private readonly List<int> campFoodEffectTypes = new List<int>();
    private CardSimulator.Battlefield.HexBattleDebugPanel debugPanel;
    private Node debugPanelSource;
    public override void _Ready()
    {
        bool uiSmoke = OS.GetCmdlineUserArgs().Contains("--run-flow-ui-smoke");
        // 省时口径（10 月施工文档 §17.5）：`--run-flow-ui-smoke=<段名>` 只跑改动到的那一段；
        // 裸 `--run-flow-ui-smoke` 仍是整套回归（提交门）。两者的建档 / 备份 / 收尾完全共用。
        string smokeSegment = ParseUiSmokeSegment(OS.GetCmdlineUserArgs());
        if ((uiSmoke || smokeSegment != null) && RunSession.Instance?.Current == null)
        {
            // 烟测会新建本局并写档：先备份玩家存档，结束时还原，避免覆盖正式进度。
            BackupRunSaveFile();
            LoadingSystem.EnsureAllDataLoaded();
            RunSession.Instance.StartNewRun(new[] { 1002, 1003, 1004 }, 20260921);
        }

        contentLayer = CreateLayer("ContentLayer", RunUiLayers.Content);
        worldMapLayer = CreateLayer("WorldMapLayer", RunUiLayers.WorldMap);
        topBarBackdropLayer = CreateLayer("TopBarBackdropLayer", RunUiLayers.TopBarBackdrop);
        badgeLayer = CreateLayer("SettlementBadgeLayer", RunUiLayers.SettlementBadge);
        modalLayer = CreateLayer("ModalLayer", RunUiLayers.Modal);
        abandonLayer = CreateLayer("AbandonConfirmLayer", RunUiLayers.AbandonConfirm);
        globalButtonLayer = CreateLayer("GlobalButtonLayer", RunUiLayers.GlobalButton);
        campLayer = CreateLayer("CampLayer", RunUiLayers.Camp);

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
        // 时间点不足（地图交互 §五）：地图侧不再改动任何状态，由这里接手营地转场。
        map.RestRequested += OpenCamp;
        settlementUi = new SettlementUi();
        AddChild(settlementUi);
        settlementUi.Bind(modalLayer, badgeLayer, abandonLayer);
        settlementUi.PanelClosed += OnSettlementPanelClosed;
        settlementUi.StateChanged += OnSettlementStateChanged;
        settlementUi.AbandonCommitted += OnSettlementAbandonCommitted;
        // 背包界面（P0-17 界面半）：常驻本场景、挂模态层，内容重建不丢；开关由顶栏「背包」按钮驱动。
        bagUi = new BagUi();
        AddChild(bagUi);
        bagUi.Bind(modalLayer);
        // 装备界面（P0-18 界面半）：同样常驻本场景、挂模态层；与背包互斥（开关都在顶栏入口里处理）。
        equipmentUi = new EquipmentUi();
        AddChild(equipmentUi);
        equipmentUi.Bind(modalLayer);
        // 本机 AI 接口（2026-10-02）：把本场景登记进唯一服务 —— `run.*`（玩家通道，含背包 / 时间点 / 营地 UI）
        // 与 `debug.run.*`（调试通道，选关 / 跳关等）。懒启动：第一个登记的域决定端口。
        if (EnableCommandApi) ApiService.RegisterRun(new ApiRunContext { Scene = this }, ApiPort);
        var run = RunSession.Instance;
        // 结算态优先：非战斗来源（事件 / 商人）发卡同样落 InSettlement，读档重进必须复现结算界面 / 浮窗，
        // 不能重播事件（§5.7 / §6.5）。
        if (run?.IsInSettlement == true) StartLevel(run.Current.PendingContentId);
        else if (run?.Current?.PendingContentType == "Event") StartEvent(run.Current.PendingContentId);
        else if (run?.Current?.PendingContentType == "Level" || run?.IsInBattleStart == true) StartLevel(run?.Current?.PendingContentId);
        else if (smokeSegment != null) { map.SetReadOnly(false); CallDeferred(nameof(RunUiSmokeSegment), smokeSegment); }
        else if (uiSmoke) { map.SetReadOnly(false); CallDeferred(nameof(RunUiSmoke)); }
        else { map.SetReadOnly(false); CallDeferred(nameof(TriggerStartEvent)); }
    }
    private void TriggerStartEvent() => map.TriggerStartEventIfNeeded();

    public override void _ExitTree()
    {
        // 场景销毁（回主菜单 / 换场景）：摘掉运行局域（只摘自己那份，见 ApiService 的 owner 记账）。
        ApiService.UnregisterRun(this);
    }

    // ── AI 接口访问面（2026-10-02）────────────────────────────────────────────
    // 口径：这里暴露的都是「界面上真能点的那一下」；调试通道专用口（绕过闸门 / 选关 / 跳关）
    // 一律带 `Debug` 前缀并在注释里写明越权点，只有 `DebugApiRun` 会调用。

    public BagUi Bag => bagUi;
    public EquipmentUi Equipment => equipmentUi;
    public CampScene Camp => camp;
    public MapScene Map => map;
    public SettlementUi Settlement => settlementUi;
    public HexBattleScene ActiveBattle => activeBattle;

    /// <summary>营地（夜间 UI）是否开着。</summary>
    public bool IsCampOpen => camp != null && GodotObject.IsInstanceValid(camp);

    /// <summary>世界地图是否可见（顶栏「地图」的开合状态）。</summary>
    public bool IsMapVisible => map != null && GodotObject.IsInstanceValid(map) && map.Visible;

    /// <summary>地图是否可选（false = 有内容进行中）。</summary>
    public bool IsMapSelectable => mapSelectable;

    /// <summary>当前内容形态（`battle` / `event` / `none`），供 `run.state` 回读。</summary>
    public string ContentKind
    {
        get
        {
            if (host == null || host.GetChildCount() == 0) return "none";
            Node firstChild = host.GetChild(0);
            // 地点场景（村庄 / 商人）已撤除（2026-10-06 方案甲）：内容进行中只剩「战斗」与「事件」两种形态。
            return firstChild is RunBattleScene ? "battle" : "event";
        }
    }

    /// <summary>结算面板 / 放弃确认弹窗是否正开着（此时多数玩家入口被拦）。</summary>
    public bool IsSettlementBlocking => settlementUi != null && (settlementUi.IsPanelOpen || settlementUi.IsConfirmOpen);

    /// <summary>顶栏「背包」按钮：已开 → 关闭并返回 false；未开 → 打开并返回 true。</summary>
    public bool ToggleBagUi()
    {
        if (bagUi == null) return false;
        ToggleBag();
        return bagUi.IsOpen;
    }

    /// <summary>顶栏「装备」按钮：已开 → 关闭并返回 false；未开 → 打开并返回 true（与背包互斥）。</summary>
    public bool ToggleEquipUi()
    {
        if (equipmentUi == null) return false;
        ToggleEquip();
        return equipmentUi.IsOpen;
    }

    /// <summary>顶栏「结束当天」：进营地（内容进行中 / 结算面板打开 / 已在营地时返回 false，与按钮禁用口径一致）。</summary>
    public bool TryEndDay()
    {
        if (IsSettlementBlocking || !mapSelectable || IsCampOpen) return false;
        OpenCamp();
        return IsCampOpen;
    }

    /// <summary>顶栏「地图」按钮（开 / 关；结算面板与放弃弹窗打开时按钮本身不动作）。</summary>
    public void ToggleMapUi() => ToggleMap();

    /// <summary>玩家口径：点一个可达格进入（= 鼠标点击的同一入口，只认可达格）。</summary>
    public bool TryEnterReachableNode(int nodeId) => map != null && GodotObject.IsInstanceValid(map) && map.TryEnterReachableNode(nodeId);

    /// <summary>玩家口径：点第一个可达格。</summary>
    public bool TryEnterNextNode() => map != null && GodotObject.IsInstanceValid(map) && map.TryEnterFirstReachableNode();

    /// <summary>**调试通道**：直接以指定关卡开战（绕过地图、池档位、时间点与可达判定）。</summary>
    public bool DebugSelectLevel(string levelId, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(levelId)) { error = "请给出关卡 Id。"; return false; }
        try { CardSimulator.Battlefield.BattleLevelCatalog.Load(levelId); }
        catch (Exception ex) { error = $"关卡 {levelId} 加载失败：{ex.Message}"; return false; }
        StartLevel(levelId);
        return true;
    }

    /// <summary>**调试通道**：直接以指定事件开始（绕过地图与节点类型）。</summary>
    public bool DebugSelectEvent(string eventId, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(eventId)) { error = "请给出事件 Id。"; return false; }
        try { StoryEventCatalog.Load(eventId); }
        catch (Exception ex) { error = $"事件 {eventId} 加载失败：{ex.Message}"; return false; }
        StartEvent(eventId);
        return true;
    }

    /// <summary>**调试通道**：一键跳到最近的未访问战斗格（无视可达与只读闸门，时间点照常结算）。</summary>
    public bool DebugNextCombat(out int nodeId, out string error)
    {
        nodeId = -1;
        error = "地图未就绪。";
        return map != null && GodotObject.IsInstanceValid(map) && map.TryJumpToNextCombat(out nodeId, out error);
    }

    /// <summary>
    /// **调试通道**（`debug.run.complete_level`）：把**当前战斗关卡**直接判胜，随后照常走战斗结算
    /// （结算面板 → 选卡 → 回地图），落点与玩家真打完一场完全一致。
    /// 只认战斗内容：事件（或没有内容）一律拒绝 —— 事件必须由玩家选项推进，调试指令不得跳过。
    /// </summary>
    public bool DebugCompleteLevel(out string error)
    {
        error = string.Empty;
        if (ContentKind != "battle" || activeBattle == null || !GodotObject.IsInstanceValid(activeBattle))
        {
            error = "当前不是战斗关卡：本指令只用于完成战斗关卡，事件（或没有内容）不能跳过。";
            return false;
        }
        if (!activeBattle.DebugResolveVictory(out error)) return false;
        GD.Print("[API] 调试：当前战斗关卡已判胜，将按正常战斗流程结算。");
        return true;
    }

    /// <summary>
    /// **调试通道**：放弃当前内容并回到**可选地图**（不结算、不领取战利品）。
    /// 用途：模块级验证要一条「随时回到地图」的捷径（例如直接测背包 / 时间点 / 营地，不必先打完一场）。
    /// </summary>
    public void ApiBackToMap()
    {
        activeBattle = null;
        activeContent = null;
        attachMapRetries = 0;
        ClearHost();
        mapSelectable = true;
        ReturnToSelectableMap();
        GD.Print("[API] 调试：已放弃当前内容并回到可选地图。");
    }

    /// <summary>
    /// **调试通道**：截图到 `res://Tests/ApiCaptures/`（无窗口 / headless 时返回 null）。
    /// 运行局随时可用，不必真有战斗场（与 `HexBattleScene.CaptureApiScreenshot` 同一命名口径）。
    /// </summary>
    public string CaptureApiScreenshot(string requestedName, string captureMode)
    {
        string safeName = string.IsNullOrWhiteSpace(requestedName) ? "run-ui" : string.Concat(requestedName.Where(char.IsLetterOrDigit));
        if (safeName.Length == 0) safeName = "run-ui";
        string relative = $"res://Tests/ApiCaptures/{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        string absolute = ProjectSettings.GlobalizePath(relative);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolute));
        Image image = GetViewport().GetTexture().GetImage();
        if (image == null) return null;
        if (string.Equals(captureMode, "compressed", StringComparison.OrdinalIgnoreCase))
            image.Resize(1280, 720, Image.Interpolation.Lanczos);
        return image.SavePng(absolute) == Error.Ok ? relative : null;
    }

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

    /// <summary>
    /// 结算已落档：按存档复现面板 / 浮窗（读档重进只看 `SettlementPanelClosed`，§6.5）。
    /// 面板一出现就让战场转入战后操作态（2026-10-01 用户改判；原口径是「关闭面板之后才收」）。
    /// </summary>
    private void OnSettlementReady(int refundedStolenGold)
    {
        if (settlementUi == null) return;
        settlementUi.RefundedStolenGold = refundedStolenGold;
        settlementUi.RefreshFromSave();
        if (settlementUi.IsPanelOpen) map.SetReadOnly(true);
        EnterPostBattleStateForSettlement();
    }

    /// <summary>
    /// 结算界面一出现就把战场转入战后操作态（交互案 §三，2026-10-01 用户改判：不必等关闭面板）：
    /// 收起手牌槽 / 能量与额度面板 / 牌堆 / `结束回合`，并允许战后自由移动。
    /// **未结束的战斗**（战斗途中事件发卡结算）由 `HexBattleScene.SetPostSettlementMode` 自行拒绝 ——
    /// 那条路径若收掉手牌与结束回合就成软锁。读档重进结算界面时本方法多半是空跑（宿主还没挂上
    /// `activeBattle`），那条路径由 `RunBattleScene.TryRestorePostBattleBattlefield` 自行进入战后态。
    /// </summary>
    private void EnterPostBattleStateForSettlement()
    {
        if (activeBattle == null || !GodotObject.IsInstanceValid(activeBattle)) return;
        if (activeBattle.IsPostSettlementMode) return;
        activeBattle.SetPostSettlementMode(true);
        GD.Print("[RunFlow] 结算界面出现：战场转战后操作态（手牌 / 能量额度 / 牌堆 / 结束回合已收起）。");
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
        // 隐藏手牌槽 / 能量 / 牌堆 / 结束回合。战场的进入时机在**结算面板出现当刻**（见 `OnSettlementReady`），
        // 这里再调一次是幂等兜底（覆盖「面板出现时战斗尚未结束而被拒绝、之后才结束」的边界）。
        EnterPostBattleStateForSettlement();
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
        // 背包 / 装备界面在最上层模态里最晚打开：先关它（两者互斥，不可能同时开着），
        // 再走结算界面自己的分层（§九）。
        if (bagUi?.IsOpen == true)
        {
            bagUi.Close();
            SetMapInputForModal(false);
            return true;
        }

        if (equipmentUi?.IsOpen == true)
        {
            equipmentUi.Close();
            SetMapInputForModal(false);
            return true;
        }

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

        // 当天已耗尽（战斗 / 移动跨过日界）时回到地图必须立刻进营地：否则玩家能在"新一天"继续走。
        if (RunSession.Instance?.Current?.MapState.PendingRestDay == true)
        {
            CallDeferred(nameof(OpenCamp));
        }
    }
    // ── 营地（篝火休息）与时间点显示（第 9 / 11 条） ────────────────────

    /// <summary>
    /// 打开营地（休息界面）。两条入口共用：常驻栏「结束当天」（主动结束当天）与地图的时间点不足强制转场
    /// （`MapScene.RestRequested`）。结算面板 / 放弃确认打开期间不转场（§九 输入层级）。
    /// </summary>
    private void OpenCamp()
    {
        if (camp != null && GodotObject.IsInstanceValid(camp)) return;
        RunSession run = RunSession.Instance;
        if (run?.Current == null) return;
        if (settlementUi != null && (settlementUi.IsPanelOpen || settlementUi.IsConfirmOpen)) return;
        if (!mapSelectable)
        {
            // 内容进行中（战斗 / 事件 / 结算）没有"结束当天"：时间点只在选点态跨天。
            GD.Print("[时间点] 当前内容进行中，不能进入营地休息。");
            return;
        }

        PackedScene packed = GD.Load<PackedScene>("res://Scenes/Run/CampScene.tscn");
        if (packed == null)
        {
            GD.PrintErr("[运行局] 无法加载 CampScene.tscn，放弃营地转场。");
            return;
        }

        SetWorldMapVisible(false);
        map.SetReadOnly(true);
        // 交互案「营地基础画面」：营地期间只常驻四个营地按钮 —— 常驻栏整行隐藏（含地图 / 暂停入口）。
        SetGlobalTopBarVisible(false);
        camp = packed.Instantiate<CampScene>();
        camp.EmbeddedMode = true;
        camp.RestCompleted += OnCampRestCompleted;
        campLayer.AddChild(camp);
        GD.Print($"[时间点] 进入营地：第 {run.CurrentDay} 天，剩余 {RunTimePoints.Format(run.RemainingToday)} / {RunTimePoints.Format(RunTimePoints.PointsPerDay)}。");
    }

    /// <summary>营地休息结算完成（已淡出）：销毁营地、恢复常驻栏，并把地图交回可选态（交互案「休息」第 6 步）。</summary>
    private void OnCampRestCompleted()
    {
        if (camp != null && GodotObject.IsInstanceValid(camp)) camp.QueueFree();
        camp = null;
        SetGlobalTopBarVisible(true);
        RefreshTimePointText();
        ReturnToSelectableMap();
        RunSession run = RunSession.Instance;
        GD.Print($"[时间点] 休息完成：第 {run?.CurrentDay ?? 1} 天，剩余 {RunTimePoints.Format(run?.RemainingToday ?? 0f)}。");
    }

    /// <summary>时间点显示刷新（天数 + 当天剩余，精度 0.1）；文案没变时不写 Label。</summary>
    private void RefreshTimePointText()
    {
        RunSession run = RunSession.Instance;
        string text = run?.Current == null
            ? string.Empty
            : RunTimePoints.FormatDayAndRemaining(run.Current.MapState.TimePoints);
        if (timePointLabel != null && !string.Equals(shownTimePointText, text, StringComparison.Ordinal))
        {
            shownTimePointText = text;
            timePointLabel.Text = text;
        }

        // 主动结束当天只在选点态可用（内容进行中 / 已在营地时不可点）。
        if (endDayButton != null)
        {
            bool inCamp = camp != null && GodotObject.IsInstanceValid(camp);
            endDayButton.Disabled = run?.Current == null || !mapSelectable || inCamp;
        }
    }


    // ── 地点场景：2026-10-06 撤除（方案甲）────────────────────────────────
    // 村庄专用地点场景（`VillageScene.tscn` / `VillageVisit` / `VillageLayout` / `run.village.*`）
    // 不符合「统一关卡通道」交互案，已整体撤除：
    //   · 世界地图不再有地点分流（`MapScene.PlaceRequested` 一并撤除）—— `FixedNode.csv` 里
    //     `ContentType = Village` 的行暂时走「无配置」分支（停留地图，村庄暂不可达）；
    //   · 设施规则层（`VillageLodging` / `VillageForage` / `SmithyCrafting` / `RestaurantTrade`）
    //     与结算入口（`RunSession.Place.cs`）保留，待「统一关卡通道」批挂到关卡场景上。

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

    /// <summary>
    /// 常驻顶栏的不透明底板：铺满第一行那一带（`RunUiLayout.TopRowTop → TopBarBackdropBottom`），
    /// 让战场 / 世界地图的内容不再从按钮之间透出来；底边留一条细线分隔。
    ///
    /// 层级：`RunUiLayers.TopBarBackdrop = 32` —— 压过内容层（战场）与世界地图，**低于**结算浮标 /
    /// 结算模态 / 营地 / 放弃确认 / 常驻按钮栏，所以任何弹窗都不会被它压住。
    /// 输入：`MouseFilter = Ignore`，这一带照旧把点击透给战场与地图（按钮本体在 `GlobalButton` 层接收点击）。
    /// </summary>
    private void BuildTopBarBackdrop()
    {
        topBarBackdrop = new Panel { Name = "TopBarBackdrop", MouseFilter = MouseFilterEnum.Ignore };
        topBarBackdrop.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("101820"),               // 不透明：与战场底色同料，按钮区自此不再透出地图
            BorderColor = new Color("2b3a45"),
            BorderWidthBottom = 2,
        });
        topBarBackdrop.AnchorLeft = 0f; topBarBackdrop.AnchorTop = RunUiLayout.TopRowTop;
        topBarBackdrop.AnchorRight = 1f; topBarBackdrop.AnchorBottom = RunUiLayout.TopBarBackdropBottom;
        topBarBackdrop.OffsetLeft = 0; topBarBackdrop.OffsetTop = 0;
        topBarBackdrop.OffsetRight = 0; topBarBackdrop.OffsetBottom = 0;
        topBarBackdropLayer.AddChild(topBarBackdrop);
    }

    /// <summary>常驻顶栏整体（按钮行 + 底板）显隐：营地期间两者一起让位，不能只藏按钮留下黑条。</summary>
    private void SetGlobalTopBarVisible(bool visible)
    {
        globalButtonLayer.Visible = visible;
        if (topBarBackdropLayer != null) topBarBackdropLayer.Visible = visible;
    }

    private void BuildGlobalTopBar()
    {
        // 常驻栏几何统一在 `RunUiLayout`（与战场 HUD 共用）：右组按「现有 4 + 预留 2（背包 / 装备）」的左沿起算，
        // 左组（时间点）在右组左沿之前收住，两组无论按钮怎么增删都不重叠。
        BuildTopBarBackdrop();

        // 通用按钮行：选点态也显示的常驻入口，放最上层位置。
        generalRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        generalRow.AddThemeConstantOverride("separation", (int)RunUiLayout.TopButtonSeparation);
        generalRow.AnchorLeft = RunUiLayout.GeneralRowLeft; generalRow.AnchorTop = RunUiLayout.TopRowTop;
        generalRow.AnchorRight = RunUiLayout.GeneralRowRight; generalRow.AnchorBottom = RunUiLayout.TopRowBottom;
        globalButtonLayer.AddChild(generalRow);
        mapButton = AddTopButton(generalRow, "地图", ToggleMap);
        // 「定位当前角色」按宽按钮计：右组容量断言（`--run-flow-ui-smoke`）读的就是这个宽度口径。
        focusButton = AddTopButton(generalRow, "定位当前角色", () => activeBattle?.CenterSelectedUnitFromGlobalTopBar());
        focusButton.CustomMinimumSize = new Vector2(RunUiLayout.WideTopButtonWidth, RunUiLayout.TopButtonHeight);
        debugButton = AddTopButton(generalRow, "调试", ToggleDebugPanel);
        pauseButton = AddTopButton(generalRow, "暂停", () => activeBattle?.TogglePauseFromGlobalTopBar());

        // 左侧：时间点显示（第 9 条） + 主动结束当天（地图交互 §五：尚有剩余时间点时也可进营地）。
        timeRow = new HBoxContainer();
        timeRow.AddThemeConstantOverride("separation", 10);
        timeRow.AnchorLeft = RunUiLayout.TimeRowLeft; timeRow.AnchorTop = RunUiLayout.TopRowTop;
        timeRow.AnchorRight = RunUiLayout.TimeRowRight; timeRow.AnchorBottom = RunUiLayout.TopRowBottom;
        globalButtonLayer.AddChild(timeRow);
        timePointLabel = new Label
        {
            Text = string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        timePointLabel.AddThemeFontSizeOverride("font_size", 17);
        timePointLabel.AddThemeColorOverride("font_color", new Color("f5d98c"));
        timeRow.AddChild(timePointLabel);
        // 背包入口：排在时间点文案的**右边**（用户口径 2026-10-02），在「结束当天」之前。
        bagButton = AddTopButton(timeRow, "背包", ToggleBag);
        // 装备入口：紧排 `背包` 之后（用户口径 2026-10-02：「入口放在背包入口的右边」）。
        equipButton = AddTopButton(timeRow, "装备", ToggleEquip);
        endDayButton = AddTopButton(timeRow, "结束当天", OpenCamp);

        // 剧情专属按钮：左侧 Log / 隐藏 / Auto，右侧 跳过；都在通用行下方同一带内。
        storyRow = new HBoxContainer();
        storyRow.AddThemeConstantOverride("separation", 10);
        storyRow.AnchorLeft = RunUiLayout.TimeRowLeft; storyRow.AnchorTop = RunUiLayout.StoryRowTop;
        storyRow.AnchorRight = .37f; storyRow.AnchorBottom = RunUiLayout.StoryRowBottom;
        globalButtonLayer.AddChild(storyRow);
        logButton = AddTopButton(storyRow, "Log", () => activeBattle?.ActiveStoryOverlay?.ToggleLogFromGlobalTopBar());
        hideButton = AddTopButton(storyRow, "隐藏", () => activeBattle?.ActiveStoryOverlay?.ToggleHiddenFromGlobalTopBar());
        autoButton = AddTopButton(storyRow, "Auto: 关闭", () => activeBattle?.ActiveStoryOverlay?.ToggleAutoFromGlobalTopBar());

        skipRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        skipRow.AddThemeConstantOverride("separation", (int)RunUiLayout.TopButtonSeparation);
        skipRow.AnchorLeft = RunUiLayout.GeneralRowLeft; skipRow.AnchorTop = RunUiLayout.StoryRowTop;
        skipRow.AnchorRight = RunUiLayout.GeneralRowRight; skipRow.AnchorBottom = RunUiLayout.StoryRowBottom;
        globalButtonLayer.AddChild(skipRow);
        skipButton = AddTopButton(skipRow, "跳过", () => activeBattle?.ActiveStoryOverlay?.ToggleSkipFromGlobalTopBar());
        ConfigureGlobalTopBar(null);
    }

    private static Button AddTopButton(Control parent, string text, System.Action action)
    {
        Button button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(RunUiLayout.TopButtonWidth, RunUiLayout.TopButtonHeight),
            // 纵向居中于顶栏底板带（`RunUiLayout.TopRowTop → TopBarBackdropBottom`）。
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
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
        // 背包常驻可用（选点态也能整理）：没有进行中的本局时禁用；只读原因每帧由 RefreshBagArrangeGate 重算。
        bagButton.Visible = true;
        bagButton.Disabled = RunSession.Instance?.Current == null;
        // 装备入口同口径：常驻可用，没有本局时禁用；部位格的落点受限原因与背包共用同一份闸门。
        equipButton.Visible = true;
        equipButton.Disabled = RunSession.Instance?.Current == null;
        RefreshBagArrangeGate();
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
        // 时间点显示与「结束当天」可用性：每帧只做一次廉价比较（文案变了才写 Label）。
        RefreshTimePointText();
        // 背包整理闸门（§三）：内容 / 结算 / 营地状态变了才重算（不入档，只有界面读它）。
        RefreshBagArrangeGate();
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

    /// <summary>
    /// 背包界面的开关（顶栏「背包」按钮，用户口径 2026-10-02）：与调试窗一样走 `RunUiLayers.Modal`；
    /// 结算面板 / 放弃确认 / 营地期间不开（避免两层模态抢输入，那些状态的只读口径见 §三）。
    /// 开合本身不改任何游戏状态：背包、手位、随身格的当前值原样保留。
    /// **与装备界面互斥**（装备系统交互案 §一）：打开背包就关掉装备。
    /// </summary>
    private void ToggleBag()
    {
        if (bagUi == null)
        {
            return;
        }

        if (bagUi.IsOpen)
        {
            bagUi.Close();
            SetMapInputForModal(false);
            return;
        }

        if (settlementUi != null && (settlementUi.IsPanelOpen || settlementUi.IsConfirmOpen)) return;
        if (camp != null) return;
        equipmentUi?.Close(); // 互斥：两个模态不叠着开
        bagUi.Open();
        if (!bagUi.IsOpen) return;
        SetMapInputForModal(true);
        GD.Print($"[RunFlow] 背包界面打开：负荷 {RunSession.Instance?.BagLoad:0.0} / {RunSession.Instance?.BagLoadLimit:0.0}"
            + $"（{(RunSession.Instance?.CanArrangeBag == true ? "可整理" : RunSession.Instance?.BagArrangeBlockReason)}）。");
    }

    /// <summary>
    /// 装备界面的开关（顶栏「装备」按钮，用户口径 2026-10-02「入口放在背包入口的右边」）：
    /// 与背包界面同一套层级 / 互斥 / 只读口径 —— 打开时先关背包，反之亦然；`Esc` 与 `关闭` 都能退出。
    /// 开合本身不改任何游戏状态：部位格、手位、背包的当前值原样保留。
    /// </summary>
    private void ToggleEquip()
    {
        if (equipmentUi == null)
        {
            return;
        }

        if (equipmentUi.IsOpen)
        {
            equipmentUi.Close();
            SetMapInputForModal(false);
            return;
        }

        if (settlementUi != null && (settlementUi.IsPanelOpen || settlementUi.IsConfirmOpen)) return;
        if (camp != null) return;
        bagUi?.Close(); // 互斥：两个模态不叠着开
        equipmentUi.Open();
        if (!equipmentUi.IsOpen) return;
        SetMapInputForModal(true);
        GD.Print($"[RunFlow] 装备界面打开：角色槽 {equipmentUi.ActiveSlotIndex} / 饰品格 {RunSession.Instance?.AccessorySlotCount}"
            + $"（{(RunSession.Instance?.CanArrangeBag == true ? "可换装" : RunSession.Instance?.BagArrangeBlockReason)}）。");
    }

    /// <summary>
    /// 背包整理的「无内容进行中」闸门（背包系统交互案 §三）：把当刻场景状态翻译成
    /// `RunSession.BagArrangeBlockReason`（**运行时**字段，不入档）。每帧比对一次，变了才写 + 重画已打开的界面。
    /// 「战后待领取态」按 `mapSelectable`（面板已关闭 → 地图可选）放行，与 §三 的清单一致。
    ///
    /// 2026-10-02 修复：**战斗已结束的战场不再是「内容进行中」**。之前只看 `!mapSelectable`，于是
    /// 「读档重进结算界面（面板已关）」这类形态里战场已经是战后操作态、却仍被判成战斗中，背包被一直标成只读。
    /// 现在把「战场已进战后操作态」也算作内容已完成（与 `EnterPostBattleStateForSettlement` 同一判据）。
    /// </summary>
    private void RefreshBagArrangeGate()
    {
        RunSession run = RunSession.Instance;
        if (run == null)
        {
            return;
        }

        // 战场进了战后操作态 = 这场仗已经打完（战斗途中被事件插结算时 HexBattleScene 会拒绝进入，见 SetPostSettlementMode）。
        bool battleFinished = activeBattle != null && GodotObject.IsInstanceValid(activeBattle) && activeBattle.IsPostSettlementMode;
        bool contentInProgress = host != null && host.GetChildCount() > 0 && !mapSelectable && !battleFinished;
        bool settlementPanelOpen = settlementUi != null && (settlementUi.IsPanelOpen || settlementUi.IsConfirmOpen);
        string reason = RunSession.DescribeBagArrangeBlock(
            campActive: camp != null,
            settlementPanelOpen: settlementPanelOpen,
            battleContentActive: contentInProgress && activeBattle != null,
            eventContentActive: contentInProgress && activeBattle == null);
        // 调试通道的强制放行（`debug.run.force_bag_gate` 空理由）：内容进行中也要能验证拖动规则本身。
        if (run.BagArrangeOverride)
        {
            reason = string.Empty;
        }
        if (string.Equals(run.BagArrangeBlockReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        run.BagArrangeBlockReason = reason;
        if (bagUi?.IsOpen == true)
        {
            bagUi.Refresh();
        }

        // 装备界面与背包共用同一份闸门原因：界面开着时同步重画（横幅与落点判定都读它）。
        if (equipmentUi?.IsOpen == true)
        {
            equipmentUi.Refresh();
        }
    }

    /// <summary>
    /// **单段烟测**入口（`--run-flow-ui-smoke=<段名>`，省时口径见 10 月施工文档 §17.5）：
    /// 整套 UI 烟测串了背包 / 地图 / 结算 / 战斗 / 事件 / 营地 / 读档七段（含 15 张截图与大量等帧；
    /// 2026-10-03 图形版实测整套 ≈ 15 s、单段 ≈ 6 s）。改动只落在其中一段时用这个档位只跑那一段 ——
    /// 省的不只是那几秒，更是**隔离失败**：无关段挂 / 前段残留污染都会被排除在结论之外。
    /// 建档 / 存档备份 / 收尾与整套共用同一条路径，断言口径不缩水。
    /// **新增独立段**（自带「摆场景 → 断言 → 收尾」的 `async Task` 方法）时，在下面 switch 里登记一行。
    /// </summary>
    private async void RunUiSmokeSegment(string segment)
    {
        try
        {
            await WaitFrames(2);
            switch (segment)
            {
                case "event-battle":
                    Require(RunSession.Instance?.Current != null, "单段烟测需要一局进行中的本局。");
                    await RunEventBattleChoiceSmoke(RunSession.Instance);
                    break;
                case "time-point-camp":
                    Require(RunSession.Instance?.Current != null, "单段烟测需要一局进行中的本局。");
                    await RunTimePointAndCampSmoke();
                    break;
                default:
                    throw new InvalidOperationException($"未知的单段烟测段名「{segment}」；可用段：event-battle、time-point-camp。");
            }
            GD.Print($"RUN_FLOW_UI_SMOKE_SEGMENT_PASS: {segment}");
            RestoreRunSaveFile();
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"RUN_FLOW_UI_SMOKE_SEGMENT_FAIL: {segment}: {ex}");
            RestoreRunSaveFile();
            GetTree().Quit(1);
        }
    }

    /// <summary>解析 `--run-flow-ui-smoke=&lt;段名&gt;`（取第一份，段名大小写不敏感）；没有则返回 null。</summary>
    private static string ParseUiSmokeSegment(string[] args)
    {
        const string prefix = "--run-flow-ui-smoke=";
        foreach (string arg in args)
            if (arg.StartsWith(prefix, StringComparison.Ordinal) && arg.Length > prefix.Length)
                return arg.Substring(prefix.Length).Trim().ToLowerInvariant();
        return null;
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

            // 常驻栏几何（§49.1）：右组必须容得下「现有 4 + 预留 2（装备等后续入口）」——
            // 这是「给后续局外入口留位置」的机器判据（`背包` 自 2026-10-02 起落在时间点行，见下方背包段）；
            // 左右两组不得相交；战场 HUD 必须让开顶栏两行。
            float viewportWidth = GetViewport().GetVisibleRect().Size.X;
            Require(RunUiLayout.FitsReservedCluster(viewportWidth),
                $"常驻栏右组容不下预留的「背包 / 装备」两个入口：需要 {RunUiLayout.ReservedClusterWidth:0} px，可用 {RunUiLayout.ClusterBandWidth(viewportWidth):0} px。");
            Require(RunUiLayout.TimeRowRight <= RunUiLayout.GeneralRowLeft,
                "常驻栏左右两组不得重叠：时间点行右沿必须 ≤ 通用按钮行左沿。");
            Require(RunUiLayout.BattleHudTop >= RunUiLayout.StoryRowBottom,
                "战场 HUD 最高可用比例必须让开常驻栏两行（否则关卡目标会压住时间点行）。");
            Require(Math.Abs(generalRow.GetGlobalRect().Position.X - RunUiLayout.GeneralRowLeft * viewportWidth) < 2f,
                $"常驻按钮行左沿必须取自 RunUiLayout（期望 {RunUiLayout.GeneralRowLeft * viewportWidth:0} px，实际 {generalRow.GetGlobalRect().Position.X:0} px）。");
            Require(generalRow.GetGlobalRect().Position.X >= timeRow.GetGlobalRect().End.X - 1f,
                "常驻栏左右两组不得重叠：通用按钮行必须整体位于时间点行之右。");

            // ── 背包界面（P0-17 界面半，2026-10-02 批 E）：入口位置 → 打开 → 拖动落档 → 只读原因 → 关闭 ──
            // 入口位置是用户口径：必须排在**时间点 UI 的右边**，且仍在时间点行带内（不压右侧通用按钮行）。
            Require(bagButton != null && bagButton.Visible, "常驻顶栏应提供「背包」入口。");
            Require(bagButton.GetGlobalRect().Position.X >= timePointLabel.GetGlobalRect().End.X - 1f,
                $"「背包」入口必须排在时间点文案的右边（时间点 {timePointLabel.GetGlobalRect()}/ 背包 {bagButton.GetGlobalRect()}）。");
            Require(bagButton.GetGlobalRect().End.X <= RunUiLayout.TimeRowRight * viewportWidth + 1f,
                $"「背包」入口必须落在时间点行带内（右沿 {bagButton.GetGlobalRect().End.X:0} ≤ 带右沿 {RunUiLayout.TimeRowRight * viewportWidth:0}）。");

            RunSession bagRun = RunSession.Instance;
            Require(bagRun.CanArrangeBag,
                $"地图可选态应允许整理背包，实际阻断原因「{bagRun.BagArrangeBlockReason}」。");
            RunBagEntrySave smokeSword = RunBagSystem.Add(bagRun.Current, BagCategory.Equipment, 10001, 1); // 行军短剑（单手）
            RunBagEntrySave smokePotion = RunBagSystem.Add(bagRun.Current, BagCategory.Item, 301, 1);        // 治疗药水
            ToggleBag();
            await WaitFrames(2);
            Require(bagUi.IsOpen, "点「背包」应打开背包界面。");
            Require(bagUi.BagCellCount >= 2, $"背包列表应显示刚加入的两件物品，实际 {bagUi.BagCellCount} 格。");
            Require(bagUi.LoadText.Contains("/"), $"负荷条应显示「当前 / 上限」，实际「{bagUi.LoadText}」。");
            // 均匀网格 + 页数 / 翻页（用户指令 2026-10-02 第 2 条）：固定 5 × 5 个等宽等高格，空格也画；
            // 网格下方一行给出「第 x / y 页」与 `上一页` / `下一页`（只有一页时两边都禁用）。
            Require(bagUi.BagSlotCellCount == BagUi.PageCapacity,
                $"背包格区必须是均匀网格：每页固定 {BagUi.PageCapacity} 格（空位也画格），实际 {bagUi.BagSlotCellCount} 格。");
            // 物品格必须是**正方形**（用户口径 2026-10-02 第 2 条），空位**不写「空」**（同一条）。
            Vector2 smokeSlotSize = bagUi.BagSlotSize(0);
            Require(Math.Abs(smokeSlotSize.X - smokeSlotSize.Y) < 1f && smokeSlotSize.X > 0f,
                $"背包物品格必须是正方形，实际 {smokeSlotSize.X:0} × {smokeSlotSize.Y:0}。");
            Require(bagUi.BagSlotSize(BagUi.PageCapacity - 1) == smokeSlotSize,
                "背包网格里每一格必须同尺寸（均匀网格）。");
            Require(bagUi.BagSlotText(0).Length > 0 && bagUi.BagSlotText(BagUi.PageCapacity - 1).Length == 0,
                $"有物品的格要写名字、空格子不得写「空」（实际首格「{bagUi.BagSlotText(0)}」/ 末格「{bagUi.BagSlotText(BagUi.PageCapacity - 1)}」）。");
            Require(bagUi.PageText == "第 1 / 1 页",
                $"第 1 页文案应为「第 1 / 1 页」，实际「{bagUi.PageText}」。");
            Require(bagUi.PageCount == 1 && !bagUi.CanGoPreviousPage && !bagUi.CanGoNextPage,
                $"只有一页时翻页按钮必须都禁用，实际 页数 {bagUi.PageCount} / 上页 {bagUi.CanGoPreviousPage} / 下页 {bagUi.CanGoNextPage}。");
            Require(bagUi.BannerText.Length == 0, $"可整理态不应显示横幅，实际「{bagUi.BannerText}」。");
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-bag.png");
            int smokeBagGridCells = bagUi.BagSlotCellCount;
            string smokeBagPageText = bagUi.PageText;

            // 开局武器（槽 0 = 重剑手，「双手剑」）占满两槽 → 单手装备必须被拒（玩法 §5.2 合法组合表）。
            Require(!bagUi.SimulateDrop(BagUi.BagPayload(smokeSword.InstanceId), BagUi.HandCellName(0, RunEquipmentSystem.LeftHand)),
                "初始双手装备占满两槽时，单手装备应被拒绝。");
            Require(bagUi.HintText.Contains("双手装备"), $"拒绝原因应说明双手装备冲突，实际「{bagUi.HintText}」。");
            // 规则拒绝也要有横幅（用户指令 2026-10-02 第 1 条：「应当显示横幅提示」）。
            Require(bagUi.BannerText.Contains(BagUi.DropRejectedPrefix) && bagUi.BannerText.Contains("双手装备"),
                $"落点被规则拒绝时必须给横幅提示，实际「{bagUi.BannerText}」。");
            // 腾出手位（卸下双手开局武器 → 回背包），再走正向拖动。
            Require(bagRun.TryUnequipHandToBag(0, RunEquipmentSystem.LeftHand, out string handUnequipError), handUnequipError);
            Require(RunEquipmentSystem.HandDefinition(bagRun.Current, 0, RunEquipmentSystem.LeftHand) == string.Empty
                    && RunEquipmentSystem.HandDefinition(bagRun.Current, 0, RunEquipmentSystem.RightHand) == string.Empty,
                "卸下双手开局武器应一次清空两槽。");
            float bagLoadBefore = RunBagSystem.TotalLoad(bagRun.Current);

            // 拖动 ①：背包装备 → 左手位（落档 + 从背包消失 + 负荷下降）
            Require(bagUi.SimulateDrop(BagUi.BagPayload(smokeSword.InstanceId), BagUi.HandCellName(0, RunEquipmentSystem.LeftHand)),
                $"背包 → 左手位应成功，实际提示「{bagUi.HintText}」。");
            Require(RunEquipmentSystem.HandDefinition(bagRun.Current, 0, RunEquipmentSystem.LeftHand) == smokeSword.DefinitionId,
                "左手位应写上被拖入的装备名。");
            Require(bagRun.Current.CharacterSlots[0].EquippedWeaponDefinitionId == smokeSword.DefinitionId,
                "旧字段 `EquippedWeaponDefinitionId` 必须同步为左手镜像。");
            Require(RunBagSystem.Find(bagRun.Current, smokeSword.InstanceId) == null, "装备装上手位后应从背包里消失。");
            Require(RunBagSystem.TotalLoad(bagRun.Current) < bagLoadBefore,
                "装到手位后背包负荷必须下降（背包系统交互案 §四：手位不占背包负荷）。");
            Require(RunBagSystem.TotalLoad(bagRun.Current)
                    == Math.Abs(bagRun.CurrentBagLoad - RunBagSystem.TotalLoad(bagRun.Current)) + RunBagSystem.TotalLoad(bagRun.Current),
                "会话层负荷读数应与 RunBagSystem 一致。");

            // 拖动 ②：背包道具 → 道具栏（条目的 CarrySlot 与兼容镜像同步）
            Require(bagUi.SimulateDrop(BagUi.BagPayload(smokePotion.InstanceId), BagUi.CarryCellName(0)),
                $"背包 → 道具栏应成功，实际提示「{bagUi.HintText}」。");
            Require(RunBagSystem.CarrySlotEntry(bagRun.Current, 0)?.InstanceId == smokePotion.InstanceId
                    && bagRun.Current.CarryItemSlots[0] == smokePotion.InstanceId,
                "道具栏第 1 格必须同时写「条目的 CarrySlot」与兼容镜像 CarryItemSlots。");
            Require(bagRun.GetCarrySlotText(0) == smokePotion.DefinitionId,
                $"道具栏文案应是刚放进去的道具名，实际「{bagRun.GetCarrySlotText(0)}」。");

            // 拖动 ③：内容进行中（战斗中）—— 口径改判 2026-10-02 用户指令第 1 条：
            // **能拿起**（拖动载荷照样给），但**放进道具栏 / 装备栏被拒**，并且必须有横幅提示。
            bagRun.BagArrangeBlockReason = RunSession.BagArrangeBlockBattle;
            bagUi.Refresh(); // 宿主每帧会重算闸门并重画；烟测直接写字段，这里补一次重画
            Require(bagUi.PayloadOfCell(BagUi.BagCellName(0)).StartsWith("bag|", System.StringComparison.Ordinal),
                $"内容进行中仍应能拿起背包格里的物品，实际载荷「{bagUi.PayloadOfCell(BagUi.BagCellName(0))}」。");
            Require(bagUi.BannerText.Contains("战斗中不可整理背包") && bagUi.BannerText.Contains("不能放进道具栏"),
                $"内容进行中必须常驻横幅并说明「能拿起 / 不能放进槽位」，实际「{bagUi.BannerText}」。");
            Require(!bagUi.SimulateDrop(BagUi.CarryPayload(0), BagUi.BagCellName(0)), "内容进行中拖动落点必须被拒绝。");
            Require(bagUi.HintText.Contains("战斗中不可整理背包"),
                $"落点被拒应给出原因，实际「{bagUi.HintText}」。");

            // 分页（用户指令 2026-10-02 第 2 条）：30 件食物（食物按实例存续 ⇒ 30 个独立格）撑出第 2 页。
            bagRun.Current.BagEntries.AddRange(Enumerable.Range(0, 30)
                .Select(_ => RunBagSystem.CreateEntry(bagRun.Current, BagCategory.Food, 401)));
            bagUi.Refresh();
            Require(bagUi.PageCount == 2 && bagUi.PageNumber == 1,
                $"31 件以上应分成 2 页且停在第 1 页，实际 页数 {bagUi.PageCount} / 第 {bagUi.PageNumber} 页。");
            Require(bagUi.PageText == "第 1 / 2 页", $"第 1 页文案应为「第 1 / 2 页」，实际「{bagUi.PageText}」。");
            Require(bagUi.BagCellCount == BagUi.PageCapacity && !bagUi.CanGoPreviousPage && bagUi.CanGoNextPage,
                $"第 1 页应满 {BagUi.PageCapacity} 格且只能往后翻，实际 格数 {bagUi.BagCellCount} / 上页 {bagUi.CanGoPreviousPage} / 下页 {bagUi.CanGoNextPage}。");
            bagUi.NextPage();
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-bag-page2.png");
            Require(bagUi.PageText == "第 2 / 2 页" && bagUi.CanGoPreviousPage && !bagUi.CanGoNextPage,
                $"翻到末页应显示「第 2 / 2 页」且只能往前翻，实际「{bagUi.PageText}」/ 上页 {bagUi.CanGoPreviousPage} / 下页 {bagUi.CanGoNextPage}。");
            Require(bagUi.BagCellCount > 0 && bagUi.BagCellCount < BagUi.PageCapacity,
                $"末页应只显示剩余件数（0 < 格数 < {BagUi.PageCapacity}），实际 {bagUi.BagCellCount}。");
            bagUi.PreviousPage();
            Require(bagUi.PageText == "第 1 / 2 页", $"翻回上一页应显示「第 1 / 2 页」，实际「{bagUi.PageText}」。");
            bagRun.Current.BagEntries.RemoveAll(x => x != null && x.CategoryEnum == BagCategory.Food);
            bagUi.Refresh();
            Require(bagUi.PageCount == 1 && bagUi.PageText == "第 1 / 1 页",
                $"清掉分页夹具后应回到单页，实际 页数 {bagUi.PageCount} /「{bagUi.PageText}」。");

            bagRun.BagArrangeBlockReason = string.Empty;
            bagUi.Refresh();
            Require(bagUi.BannerText.Length == 0, $"闸门解除后横幅必须消失，实际「{bagUi.BannerText}」。");
            Require(bagUi.SimulateDrop(BagUi.CarryPayload(0), BagUi.BagCellName(0)) || bagUi.BagCellCount == 0,
                $"恢复可整理后「道具栏 → 背包」应成功（或背包列表当时为空），实际提示「{bagUi.HintText}」。");

            // 关闭：`关闭` 按钮与 `Esc` 都能退出，且不留状态
            bagUi.Close();
            Require(!bagUi.IsOpen, "关闭后背包界面必须隐藏。");
            ToggleBag();
            await WaitFrames(2);
            Require(bagUi.IsOpen, "再次点「背包」应能重新打开（单实例）。");
            Require(HandleEscapeLayers() && !bagUi.IsOpen, "`Esc` 应关闭背包界面。");

            // 收尾：把夹具还原成开局形态（烟测随后要跑结算 / 营地流程，不留脏状态）——
            // 卸下刚拖入的装备 → 清掉本段加的两件 → 把开局武器「双手剑」装回左手（双手 → 两槽）。
            RunEquipmentSystem.TryUnequipHand(bagRun.Current, 0, RunEquipmentSystem.LeftHand, out _);
            RunBagSystem.TakeOneByDefinition(bagRun.Current, BagCategory.Item, 301, out _);
            RunBagSystem.TakeOneByDefinition(bagRun.Current, BagCategory.Equipment, 10001, out _);
            RunBagEntrySave initialWeapon = RunBagSystem.AllEntriesOf(bagRun.Current, BagCategory.Equipment)
                .FirstOrDefault(x => x.DefinitionId == "双手剑");
            if (initialWeapon != null)
            {
                bagRun.TryEquipBagEntryToHand(initialWeapon.InstanceId, 0, RunEquipmentSystem.LeftHand, out _);
            }

            bagRun.Save();
            GD.Print($"RUN_FLOW_UI_SMOKE_BAG: 入口 x={bagButton.GetGlobalRect().Position.X:0}（时间点右沿 {timePointLabel.GetGlobalRect().End.X:0}）"
                + $" / 格数 {bagUi.BagCellCount} / 网格 {smokeBagGridCells} 格 {smokeBagPageText} / 关闭后 IsOpen={bagUi.IsOpen}"
                + $" / 横幅「{bagUi.BannerText}」 / 负荷 {bagRun.BagLoad:0.0}/{bagRun.BagLoadLimit:0.0}"
                + $" / 槽 0 左手 {RunEquipmentSystem.HandText(bagRun.Current, 0, RunEquipmentSystem.LeftHand)}");

            // ── 装备界面（P0-18 界面半，2026-10-02 装备界面批）：入口在背包右侧 → 互斥 → 拖装部位格 → 拒绝原因 → 关闭 ──
            int smokeHead = (int)EquipmentSlotKind.Head;
            int smokeBody = (int)EquipmentSlotKind.Body;
            int smokeAccessoryKind = (int)EquipmentSlotKind.Accessory;

            Require(equipButton != null && equipButton.Visible, "常驻顶栏应提供「装备」入口。");
            Require(equipButton.GetGlobalRect().Position.X >= bagButton.GetGlobalRect().End.X - 1f,
                $"「装备」入口必须排在「背包」入口的右边（背包 {bagButton.GetGlobalRect()} / 装备 {equipButton.GetGlobalRect()}）。");
            Require(equipButton.GetGlobalRect().End.X <= RunUiLayout.TimeRowRight * viewportWidth + 1f,
                $"「装备」入口必须落在时间点行带内（右沿 {equipButton.GetGlobalRect().End.X:0} ≤ 带右沿 {RunUiLayout.TimeRowRight * viewportWidth:0}）。");

            ToggleBag();
            await WaitFrames(2);
            Require(bagUi.IsOpen, "打开背包以验证与装备界面的互斥。");
            ToggleEquip();
            await WaitFrames(2);
            Require(equipmentUi.IsOpen && !bagUi.IsOpen, "打开装备界面必须同时关闭背包（互斥，案 §一）。");
            int smokeEquipSlots = equipmentUi.BodySlotCount;
            Require(smokeEquipSlots == RunEquipmentSystem.BodySlotTotalCount,
                $"部位格数应为 3 + 饰品格数 {RunEquipmentSystem.AccessorySlotCount}，实际 {smokeEquipSlots}。");
            Require(equipmentUi.ActiveSlotIndex == 0 && equipmentUi.CharacterTabText(0).Length > 0,
                "装备界面应默认显示 0 号角色的 Tab（文案取自 CharacterSlotNaming）。");

            // 夹具：一件头部装备 + 一件饰品，都从左侧背包装备列表拖到部位格
            RunBagEntrySave smokeHelmet = RunBagSystem.Add(bagRun.Current, BagCategory.Equipment, 20001, 1);   // 布头巾（头部）
            RunBagEntrySave smokeAccessory = RunBagSystem.Add(bagRun.Current, BagCategory.Equipment, 20004, 1); // 护身符（饰品）
            equipmentUi.Refresh();
            Require(equipmentUi.BagCellCount >= 2, $"背包装备列表应显示刚加入的两件装备，实际 {equipmentUi.BagCellCount} 格。");
            float equipLoadBefore = bagRun.BagLoad;

            Require(equipmentUi.SimulateDrop(EquipmentUi.BagPayload(smokeHelmet.InstanceId), EquipmentUi.BodySlotName(smokeHead, 0)),
                $"背包 → 头部格应成功，实际提示「{equipmentUi.HintText}」。");
            Require(equipmentUi.BodySlotText(smokeHead, 0) == "布头巾",
                $"头部格应装上布头巾，实际「{equipmentUi.BodySlotText(smokeHead, 0)}」。");
            Require(bagRun.BagLoad < equipLoadBefore, "装上部位装备后背包负荷必须下降（装备已不在背包内）。");
            Require(equipmentUi.SimulateDrop(EquipmentUi.BagPayload(smokeAccessory.InstanceId), EquipmentUi.BodySlotName(smokeAccessoryKind, 0)),
                $"背包 → 饰品1 格应成功，实际提示「{equipmentUi.HintText}」。");

            // 部位不符：饰品 → 身体格；以及部位装备 ↔ 手位互不放行（案 §三 的第 4 行）
            Require(!equipmentUi.SimulateDrop(EquipmentUi.BodySlotPayload(smokeAccessoryKind, 0), EquipmentUi.BodySlotName(smokeBody, 0)),
                "饰品不得装到身体格。");
            Require(equipmentUi.HintText == RunEquipmentSystem.BodySlotMismatchError,
                $"部位不符必须给固定文案，实际「{equipmentUi.HintText}」。");
            Require(!equipmentUi.SimulateDrop(EquipmentUi.BodySlotPayload(smokeHead, 0), EquipmentUi.HandCellName(0, RunEquipmentSystem.LeftHand)),
                "部位装备不得放进手位。");

            // 卸下：饰品格 → 背包
            Require(equipmentUi.SimulateDrop(EquipmentUi.BodySlotPayload(smokeAccessoryKind, 0), EquipmentUi.BagCellName(0)),
                $"饰品格 → 背包应成功，实际提示「{equipmentUi.HintText}」。");
            Require(equipmentUi.BodySlotText(smokeAccessoryKind, 0).Length == 0, "卸下后饰品格必须为空。");

            // 只读（内容进行中）：落点被拒 + 横幅给原因（与背包同一份闸门）
            bagRun.BagArrangeBlockReason = BagArrangeGate.Battle;
            equipmentUi.Refresh();
            Require(equipmentUi.IsArrangeBlocked && equipmentUi.BannerText.Length > 0,
                $"内容进行中时装备界面必须给出横幅，实际「{equipmentUi.BannerText}」。");
            Require(!equipmentUi.SimulateDrop(EquipmentUi.BagPayload(smokeHelmet.InstanceId), EquipmentUi.BodySlotName(smokeBody, 0)),
                "只读态下不得换装。");
            bagRun.BagArrangeBlockReason = string.Empty;
            equipmentUi.Refresh();
            Require(equipmentUi.BannerText.Length == 0, $"闸门解除后横幅必须消失，实际「{equipmentUi.BannerText}」。");

            // 关闭：`关闭` 与 `Esc` 都能退出，且不留状态
            equipmentUi.Close();
            Require(!equipmentUi.IsOpen, "关闭后装备界面必须隐藏。");
            ToggleEquip();
            await WaitFrames(2);
            Require(equipmentUi.IsOpen, "再次点「装备」应能重新打开（单实例）。");
            Require(HandleEscapeLayers() && !equipmentUi.IsOpen, "`Esc` 应关闭装备界面。");

            // 收尾：卸下夹具并把两件装备移出背包（不留脏状态给后面的结算 / 营地断言）
            if (!RunEquipmentSystem.TryUnequipSlot(bagRun.Current, 0, smokeHead, 0, out string equipUnequipError))
            {
                // 负荷拒绝时直接清格：烟测只为还原夹具，不替代规则口径
                RunEquipmentSystem.SetBodySlot(bagRun.Current.CharacterSlots[0], smokeHead, 0, string.Empty);
                GD.Print($"[RunFlow] 装备烟测还原：卸下被拒（{equipUnequipError}），已直接清格。");
            }

            RunBagSystem.TakeOneByDefinition(bagRun.Current, BagCategory.Equipment, 20001, out _);
            RunBagSystem.TakeOneByDefinition(bagRun.Current, BagCategory.Equipment, 20004, out _);
            bagRun.Save();
            GD.Print($"RUN_FLOW_UI_SMOKE_EQUIP: 入口 x={equipButton.GetGlobalRect().Position.X:0}（背包右沿 {bagButton.GetGlobalRect().End.X:0}）"
                + $" / 部位格 {smokeEquipSlots}（饰品 {RunEquipmentSystem.AccessorySlotCount}）"
                + $" / 头部格 {RunEquipmentSystem.BodySlotDefinition(bagRun.Current, 0, smokeHead, 0).Length}（清空）"
                + $" / 关闭后 IsOpen={equipmentUi.IsOpen} / 负荷 {bagRun.BagLoad:0.0}/{bagRun.BagLoadLimit:0.0}");

            // 顶栏底板（`RunUiLayers.TopBarBackdrop = 32`）：不透明地铺满第一行那一带、遮住战斗地图，
            // 但层号低于结算浮标与一切模态；按钮纵向居中于该带。
            float viewportHeight = GetViewport().GetVisibleRect().Size.Y;
            Require(topBarBackdropLayer != null && topBarBackdrop != null, "常驻顶栏应构建不透明底板。");
            Require(topBarBackdropLayer.Layer > worldMapLayer.Layer && topBarBackdropLayer.Layer > contentLayer.Layer
                && topBarBackdropLayer.Layer < badgeLayer.Layer && topBarBackdropLayer.Layer < modalLayer.Layer
                && topBarBackdropLayer.Layer < globalButtonLayer.Layer,
                $"顶栏底板层号必须压过战场与世界地图、低于结算浮标与一切模态，实际 {topBarBackdropLayer.Layer}。");
            Color backdropColor = (topBarBackdrop.GetThemeStylebox("panel") as StyleBoxFlat)?.BgColor ?? new Color(0, 0, 0, 0);
            Require(backdropColor.A >= 1f - 1e-3f, $"顶栏底板必须不透明，实际 alpha {backdropColor.A}。");
            Rect2 backdropRect = topBarBackdrop.GetGlobalRect();
            Require(backdropRect.Position.Y <= 1f, $"顶栏底板必须贴屏幕顶边，实际上沿 {backdropRect.Position.Y:0}。");
            Require(Math.Abs(backdropRect.Size.Y - RunUiLayout.TopBarBackdropBottom * viewportHeight) < 2f,
                $"顶栏底板高度应为 TopBarBackdropBottom × 视口高 = {RunUiLayout.TopBarBackdropBottom * viewportHeight:0} px，实际 {backdropRect.Size.Y:0}。");
            float backdropCenter = backdropRect.GetCenter().Y;
            foreach (Button top in new[] { mapButton, focusButton, debugButton, pauseButton, endDayButton })
            {
                Require(top != null && top.SizeFlagsVertical == SizeFlags.ShrinkCenter,
                    $"顶栏按钮必须声明纵向居中（{top?.Text} 实际 {top?.SizeFlagsVertical}）。");
                // 容器**不排布隐藏子节点**（无内容时「定位当前角色」「暂停」是隐藏的，矩形会停在初始值）：
                // 所以「实际居中」只对当前可见的按钮核对，隐藏的靠上一条声明式断言兜住。
                if (!top.IsVisibleInTree()) continue;
                Require(Math.Abs(top.GetGlobalRect().GetCenter().Y - backdropCenter) <= 3f,
                    $"顶栏按钮必须纵向居中于底板（{top.Text} 中心 {top.GetGlobalRect().GetCenter().Y:0} / 底板中心 {backdropCenter:0}）。");
            }

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
            // 2026-10-01 用户改判：结算面板**一出现**就应收起战斗 HUD（不必等关闭面板）。
            // 2026-10-02 追加：底部操作区底板（`bottomHudBackdrop`）随**手牌区**一起消失。
            Require(realBattle.IsPostSettlementMode, "结算面板一出现，战场就应转入战后操作态（不必等关闭面板）。");
            Require(realBattle.IsBottomHudBackdropHidden, "结算面板一出现，底部操作区底板就应随手牌区一起收起。");
            Require(realBattle.PostSettlementUiCollapsed,
                "结算面板一出现就应收起手牌槽 / 底部底板 / 能量与额度面板 / 结束回合，且移动按钮文案不含「能量」。");
            GD.Print($"RUN_FLOW_UI_SMOKE_POST_SETTLEMENT_EARLY: post={realBattle.IsPostSettlementMode} " +
                $"collapsed={realBattle.PostSettlementUiCollapsed} backdropHidden={realBattle.IsBottomHudBackdropHidden}");
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
            // 2026-10-02 用户口径：领取后该份卡牌 Tab **直接从列表消失**（不再转灰显示「已领取」）。
            Require(FindButtonByName(modalLayer, SettlementUi.CardTabButtonNamePrefix + firstSlot)?.IsVisibleInTree() != true,
                "领取后该份卡牌 Tab 应直接从面板列表消失。");
            Require(FindButtonContaining(this, "已领取")?.IsVisibleInTree() != true,
                "结算面板不应再出现「已领取」文案（改判：领后该条直接消失）。");
            GD.Print($"RUN_FLOW_UI_SMOKE_SETTLEMENT_TABS_REMOVED: 卡牌份 Tab 领后消失 slot={firstSlot} " +
                $"剩余卡牌份 Tab={CountButtonsByNamePrefix(modalLayer, SettlementUi.CardTabButtonNamePrefix)}");
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-settlement-claimed.png");
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
                "战后应隐藏手牌槽 / 底部底板 / 能量与额度面板 / 结束回合，且移动按钮文案不含「能量」。");
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
            // 位置落档（§七 4 改口径）：移动表现播完即写档 —— 这里**有界等待**该次写档真的落地
            // （写点在表现的收尾帧，排空表现后再等 1~2 帧才读才稳定；直接读会随机读到移动前的那一份快照）。
            int moverSlot = Math.Max(0, realBattle.Session.PlayerIds.IndexOf(postMover));
            RunUnitPlacementSave moverInSave = null;
            for (int waitFrame = 0; waitFrame < 120; waitFrame++)
            {
                moverInSave = run.Current.PostBattleBattlefield?.Units.FirstOrDefault(u => u.SlotIndex == moverSlot);
                if (moverInSave != null && moverInSave.Q == postTarget.Q && moverInSave.R == postTarget.R)
                {
                    break;
                }

                await WaitFrames(1);
            }

            Require(moverInSave != null && moverInSave.Q == postTarget.Q && moverInSave.R == postTarget.R,
                $"战后移动后应把新坐标落档（期望 {postTarget.Q},{postTarget.R}，落档 {moverInSave?.Q},{moverInSave?.R}）。");
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
            // 选中槽取**当刻选中的角色**：快照里的 `SelectedSlotIndex` 是写档那刻的选中态（点人切换选中不再写档），
            // 拿写档时的槽位去比当刻坐标会随机踩到「选中已切换」的竞态。
            int selectedSlotNow = Math.Max(0, realBattle.Session.PlayerIds.IndexOf(realBattle.Session.SelectedId));
            RunUnitPlacementSave selectedInSave = postSave.Units.FirstOrDefault(u => u.SlotIndex == selectedSlotNow);
            Require(selectedInSave != null, "战后落档应含选中角色槽的位置。");
            Require(postSave.SelectedSlotIndex >= 0 && postSave.SelectedSlotIndex < run.Current.CharacterSlots.Count,
                $"战后落档的选中槽应落在角色槽范围内，实际 {postSave.SelectedSlotIndex}。");
            AxialHex selectedNow = realBattle.Session.Occupancy.Placements[realBattle.Session.SelectedId].Coord;
            Require(selectedInSave.Q == selectedNow.Q && selectedInSave.R == selectedNow.R,
                $"当刻选中角色的坐标应与落档一致（槽 {selectedSlotNow}），落档 {selectedInSave.Q},{selectedInSave.R} / 实际 {selectedNow}。");
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
            await CaptureSmoke("res://Tests/run-flow-ui-smoke-post-battle.png");
            // 点浮窗 → 重新打开结算面板，继续走领取流程。
            FindButtonContaining(this, "未领取").EmitSignal(BaseButton.SignalName.Pressed);
            await WaitFrames(3);
            Require(FindButtonByName(this, "SettlementCloseButton")?.IsVisibleInTree() == true, "点浮窗应重新打开结算面板。");
            // 领完物品 Tab：点击即领取、面板会重建，且该条**直接从列表消失**（2026-10-02 口径），
            // 因此每次都要重新查找（§四 / 验收 2）。
            int claimGuard = 0;
            while (SettlementRewardPresenter.CountUnclaimedItems(run.Current, LoadingSystem.DropTableEntries) > 0 && claimGuard++ < 12)
            {
                Button itemTab = FindUnclaimedItemTab(modalLayer);
                Require(itemTab != null, "有未领取物品 Tab 却找不到可点条目。");
                int itemTabsBefore = CountUnclaimedItemTabs(modalLayer);
                string claimedText = itemTab.Text;
                itemTab.EmitSignal(BaseButton.SignalName.Pressed);
                await WaitFrames(2);
                int itemTabsAfter = CountUnclaimedItemTabs(modalLayer);
                Require(itemTabsAfter == itemTabsBefore - 1,
                    $"领取后物品 Tab 应直接从列表消失（不再显示「已领取」），实际 {itemTabsBefore} → {itemTabsAfter}（条目「{claimedText}」）。");
            }

            GD.Print($"RUN_FLOW_UI_SMOKE_SETTLEMENT_TABS_REMOVED: 物品 Tab 领后消失（剩 {CountUnclaimedItemTabs(modalLayer)} 条）");
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

            // 危险事件的战斗选项（`next.type = Battle`）：点下**立刻**进入战斗，不再出现「前往地图」（用户口径 2026-10-03）。
            await RunEventBattleChoiceSmoke(run);

            await RunTimePointAndCampSmoke();
            RunSessionReconstructionSmoke();
            GD.Print("RUN_FLOW_UI_SMOKE_PASS: canvas layers, map modal input, debug close, map return after content, camp food/cook, run session reconstruction");
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
    /// <summary>
    /// 危险事件 `EVT-F1-016` 的战斗选项（`next.type = Battle → F1-D-001`）端到端断言（用户口径 2026-10-03）：
    /// 点下选项必须**立刻**切到战斗内容、并把待处理内容落成关卡 —— 不再经过「前往地图」那一下。
    /// 走正式的「事件内容」路径（`BeginRunEvent` + `StartEvent`），断言的就是玩家真能点出来的那一条。
    /// </summary>
    private async System.Threading.Tasks.Task RunEventBattleChoiceSmoke(RunSession run)
    {
        Require(run?.Current != null, "事件战斗烟测需要一局进行中的本局。");
        ReturnToSelectableMap();
        await WaitFrames(2);

        int sourceNodeId = run.Current.MapState.CurrentNodeId;
        run.BeginRunEvent("EVT-F1-016", sourceNodeId, MapNodeType.DangerousEvent);
        StartEvent("EVT-F1-016");
        await WaitFrames(6);
        HexBattleScene dangerEvent = FindBattle(host);
        Require(dangerEvent?.ActiveStoryOverlay?.Visible == true, "危险事件应显示剧情 UI。");
        Require(ContentKind == "event", $"危险事件内容应为 event，实际 {ContentKind}。");

        // 跳到选项面板（跳过只压缩演出，不替玩家选结果）。
        dangerEvent.ActiveStoryOverlay.ToggleSkipFromGlobalTopBar();
        await WaitFrames(2);
        Button skipConfirm = FindButton(dangerEvent, "确认跳过");
        Require(skipConfirm?.IsVisibleInTree() == true, "危险事件跳过确认面板缺少“确认跳过”。");
        skipConfirm.EmitSignal(BaseButton.SignalName.Pressed);
        await WaitFrames(3);

        Button battleChoice = FindButtonContaining(dangerEvent, "动手夺回被劫的货物");
        Require(battleChoice?.IsVisibleInTree() == true, "危险事件缺少带战斗跳转（next = Battle）的选项。");
        battleChoice.EmitSignal(BaseButton.SignalName.Pressed);
        await WaitFrames(6);

        Require(run.Current.PendingContentType == "Level" && run.Current.PendingLevelId == "F1-D-001",
            $"战斗选项点下应立即把内容落成关卡 F1-D-001，实际 {run.Current.PendingContentType} / {run.Current.PendingLevelId}。");
        Require(ContentKind == "battle", $"战斗选项点下应立即切到战斗内容，实际 {ContentKind}。");
        Require(FindButtonContaining(host, "前往地图") == null, "战斗选项点下后不得再出现「前往地图」按钮（用户口径：点下即进战）。");
        GD.Print($"RUN_FLOW_UI_SMOKE_EVENT_BATTLE: 危险事件战斗选项立刻进战 content={ContentKind} "
            + $"level={run.Current.PendingLevelId} pending={run.Current.PendingContentType}");
        await CaptureSmoke("res://Tests/run-flow-ui-smoke-event-battle.png");

        // 收尾：清掉本段留下的“待处理战斗内容”并回到空内容的地图态（GameMode = OnMap），
        // 让后面的营地烟测与「内容未开始时」一致 —— 否则 `IsOnMap` 断言会挂在本段留下的 InBattleStart 上。
        run.CompletePendingEventToMap();
        ApiBackToMap();
        await WaitFrames(2);
    }

    /// <summary>
    /// 时间点系统与营地（第 8/9/10/11 条 + 第 27/29/30 条）的端到端断言（§九 11–16）：
    /// ① 时间点不足 → 点可达格**不移动、不扣点**并转入营地；② 营地期间常驻栏隐藏、休息结算按公式回复并推进到新一天；
    /// ③ 当天已耗尽（`PendingRestDay`）→ 同样禁止移动、转营地；④ 休息后时间点文案与常驻栏恢复。
    /// </summary>
    private async System.Threading.Tasks.Task RunTimePointAndCampSmoke()
    {
        ReturnToSelectableMap();
        await WaitFrames(2);
        RunSession run = RunSession.Instance;
        Require(run?.Current != null, "营地烟测需要一局进行中的本局。");
        RunMapStateSave state = run.Current.MapState;

        // ① 时间点不足（剩余 0.2 < 移动 0.3）：点可达格不得移动、不得扣点，且转营地。
        foreach (RunCharacterSlotSave slot in run.Current.CharacterSlots) slot.CurrentHp = Math.Max(1, slot.MaxHp / 2);
        state.TimePoints = 3.8f;
        state.PendingRestDay = false;
        run.Save();
        RefreshTimePointText();
        await WaitFrames(1);
        Require(timePointLabel.Text.Contains("第 1 天") && timePointLabel.Text.Contains("0.2"),
            $"常驻栏时间点显示应为「第 1 天 · 剩余 0.2 / 4.0」，实际 {timePointLabel.Text}。");

        int nodeBefore = state.CurrentNodeId;
        Require(map.SimulateClickReachableNode(), "地图上应存在一个可达格供时间点闸门用例点击。");
        await WaitFrames(3);
        Require(state.CurrentNodeId == nodeBefore && Math.Abs(state.TimePoints - 3.8f) < 1e-4f,
            $"时间点不足时点可达格不得移动 / 不得扣时间点，实际 位置 {state.CurrentNodeId}、进程 {state.TimePoints}。");
        Require(camp != null && GodotObject.IsInstanceValid(camp), "时间点不足应转入营地（CampScene）。");
        Require(!globalButtonLayer.Visible, "营地期间常驻按钮栏必须隐藏（只常驻四个营地按钮）。");
        Require(!topBarBackdropLayer.Visible, "营地期间顶栏底板必须随常驻栏一起隐藏（不能留一条黑边）。");

        // ① 营地画面（§49.2）：等淡入**真正结束**再取色 / 截图 —— 中途截到的是半黑画面（旧产物即如此）。
        int fadeFrames = 0;
        while (!camp.FadeFinished && fadeFrames++ < 600) await WaitFrames(1);
        Require(camp.FadeFinished, "营地淡入应在 600 帧内结束（转场黑幕不得常驻）。");
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image campShot = GetViewport().GetTexture().GetImage();
        Require(MaxRedAround(campShot, camp.FireCenter, 4) > 0.6f,
            "营地篝火必须可见：自绘火芯不得被背景子节点盖住（该点取色为暗底说明篝火被盖）。");

        // ② 守夜行（§49.2）：标签单行 + 下拉框同行相邻，不得被容器压成一字一行后错位。
        FindButton(camp, "守夜").EmitSignal(BaseButton.SignalName.Pressed);
        await WaitFrames(3);
        Label watcherCaption = FindLabel(camp, "守夜角色");
        Require(watcherCaption != null, "营地守夜面板应包含「守夜角色」标签。");
        Require(watcherCaption.Size.Y <= 32f,
            $"「守夜角色」标签必须单行显示，实际高度 {watcherCaption.Size.Y:0}（说明被容器压成一字一行）。");
        OptionButton watcherBox = FindOptionButton(camp);
        Require(watcherBox != null, "营地守夜面板应包含守夜角色下拉框。");
        Require(Mathf.Abs((watcherBox.GlobalPosition.Y + watcherBox.Size.Y * .5f)
                - (watcherCaption.GlobalPosition.Y + watcherCaption.Size.Y * .5f)) < 8f,
            "「守夜角色」标签与下拉框必须同一行（竖直居中偏差 < 8 px）。");
        Require(watcherBox.GlobalPosition.X >= watcherCaption.GlobalPosition.X + watcherCaption.Size.X - 1f,
            "守夜角色下拉框必须排在标签右侧（不得与标签重叠）。");

        // ② b 食物 / 烹饪面板（2026-10-02 批 C）：草稿制（未点休息不消耗）+ 效果饱食度上限 10 +
        //     过期食物拒绝入篝火 + 本次休息烹饪 ≤ 2 次且只消耗食物。走完后面板里会留下本次休息要吃的食物。
        await RunCampFoodPanelSmoke(run, camp);

        await CaptureSmoke("res://Tests/run-flow-ui-smoke-camp.png");

        // ② 休息结算：按「10% + 20% × 剩余 0.2/4」= 11% 回复，并推进到第 2 天（进程 3.8 → 4.0）。
        int[] hpBefore = new int[run.Current.CharacterSlots.Count];
        int[] expectedHeal = new int[run.Current.CharacterSlots.Count];
        for (int i = 0; i < hpBefore.Length; i++)
        {
            hpBefore[i] = run.Current.CharacterSlots[i].CurrentHp;
            expectedHeal[i] = RunRestResolver.PreviewHeal(run.Current, i, RunWatchMode.None, 0, camp.PlannedSatiety);
        }

        FindButton(camp, "休息").EmitSignal(BaseButton.SignalName.Pressed);
        // 等营地**真的**销毁（淡出 0.35s 是秒级 Tween）：写死帧数在低帧率下会假失败（实测同一断言一次红一次绿）。
        await WaitUntilCampDestroyed();
        Require(camp == null || !GodotObject.IsInstanceValid(camp), "休息结算完成后营地必须销毁。");
        Require(globalButtonLayer.Visible, "休息结算完成后常驻按钮栏必须恢复。");
        Require(topBarBackdropLayer.Visible, "休息结算完成后顶栏底板必须随常驻栏一起恢复。");
        Require(Math.Abs(state.TimePoints - 4f) < 1e-4f && state.CurrentDay == 2,
            $"休息后应推进到第 2 天（进程 4.0），实际 {state.TimePoints} / 第 {state.CurrentDay} 天。");
        Require(!state.PendingRestDay, "休息结算后「待休息」标记必须清除。");
        bool anyHealed = false;
        for (int i = 0; i < hpBefore.Length; i++)
        {
            int expected = Math.Min(run.Current.CharacterSlots[i].MaxHp, hpBefore[i] + expectedHeal[i]);
            Require(run.Current.CharacterSlots[i].CurrentHp == expected,
                $"休息回复量应按公式（槽 {i} 期望 {expected}，实际 {run.Current.CharacterSlots[i].CurrentHp}）。");
            anyHealed |= run.Current.CharacterSlots[i].CurrentHp > hpBefore[i];
        }

        Require(anyHealed, "休息应至少回复一名有损生命的角色。");
        Require(timePointLabel.Text.Contains("第 2 天") && timePointLabel.Text.Contains("4.0"),
            $"休息后常驻栏时间点显示应更新为第 2 天满额，实际 {timePointLabel.Text}。");

        // ② c 休息结算的食物侧（批 C）：草稿清空、规划的食物离包、效果按寿命轴落档、跨天腐坏、烹饪次数归零。
        Require(campFoodPlanIds.Count > 0, "食物烟测应至少规划一件食物（上一段已放 3 件）。");
        foreach (string instanceId in campFoodPlanIds)
        {
            Require(RunBagSystem.Find(run.Current, instanceId) == null,
                $"休息结算后篝火里规划的食物实例必须离开背包：{instanceId}。");
        }

        foreach (string instanceId in campFoodExpiredIds)
        {
            Require(RunBagSystem.Find(run.Current, instanceId) == null,
                $"跨天休息后过期的食物实例必须被移除：{instanceId}。");
        }

        List<int> effectTypes = run.Current.ActiveFoodEffects.Select(x => x.EffectType).ToList();
        foreach (int expectedType in campFoodEffectTypes)
        {
            Require(effectTypes.Contains(expectedType),
                $"休息后食物效果必须落档（期望含效果 {expectedType}，实际 {string.Join("/", effectTypes)}）。");
        }

        Require(run.Current.ActiveFoodEffects.All(x => x.DurationKind == (int)FoodEffectDurationKind.BattleCount && x.Remaining == 1f),
            "三种烟测食物都是 BattleCount:1 → 落档后寿命轴剩余必须是 1（下一场战斗生效）。");
        Require(run.Current.CookedThisRest == 0, "休息结算后本次休息的烹饪次数必须归零。");

        // ③ 当天已耗尽（`PendingRestDay`，进程在战斗 / 移动中跨过日界）：同样禁止前往并转营地。
        state.TimePoints = 4.2f;
        state.PendingRestDay = true;
        state.RestRemainingTimePoints = 0f; // 跨日界那一刻记下的"这一天"剩余
        run.Save();
        int nodeAfterRest = state.CurrentNodeId;
        Require(map.SimulateClickReachableNode(), "地图上应存在一个可达格供「已耗尽」用例点击。");
        await WaitFrames(3);
        Require(state.CurrentNodeId == nodeAfterRest && Math.Abs(state.TimePoints - 4.2f) < 1e-4f,
            "当天已耗尽时点可达格不得移动 / 不得扣时间点。");
        Require(camp != null && GodotObject.IsInstanceValid(camp), "当天已耗尽应强制转入营地。");

        // ④ 耗尽来源的休息：剩余记 0 → 只走基础 10%；结算后进入第 3 天。
        int[] exhaustedBefore = new int[run.Current.CharacterSlots.Count];
        int[] exhaustedExpected = new int[run.Current.CharacterSlots.Count];
        for (int i = 0; i < exhaustedBefore.Length; i++)
        {
            exhaustedBefore[i] = run.Current.CharacterSlots[i].CurrentHp;
            exhaustedExpected[i] = RunRestResolver.PreviewHeal(run.Current, i, RunWatchMode.None, 0, camp.PlannedSatiety);
        }

        FindButton(camp, "休息").EmitSignal(BaseButton.SignalName.Pressed);
        await WaitUntilCampDestroyed();
        Require(camp == null || !GodotObject.IsInstanceValid(camp), "耗尽来源的休息结算后营地必须销毁。");
        Require(Math.Abs(state.TimePoints - 8f) < 1e-4f && state.CurrentDay == 3,
            $"耗尽来源的休息后应进入第 3 天（进程 8.0），实际 {state.TimePoints} / 第 {state.CurrentDay} 天。");
        for (int i = 0; i < exhaustedBefore.Length; i++)
        {
            int expected = Math.Min(run.Current.CharacterSlots[i].MaxHp, exhaustedBefore[i] + exhaustedExpected[i]);
            Require(run.Current.CharacterSlots[i].CurrentHp == expected,
                $"耗尽来源的休息回复量应按基础 10%（槽 {i} 期望 {expected}，实际 {run.Current.CharacterSlots[i].CurrentHp}）。");
        }

        Require(!state.PendingRestDay && run.IsOnMap,
            $"两次休息后应回到地图且不残留待休息标记（待休息={state.PendingRestDay} / 模式={run.Current.GameMode} / 内容={ContentKind} / "
            + $"进程={state.TimePoints}）。");
        GD.Print($"RUN_FLOW_UI_SMOKE_CAMP: 时间点闸门 + 营地休息两轮通过（当前 {RunTimePoints.FormatDayAndRemaining(state.TimePoints)}）。");
    }



    /// <summary>
    /// 营地食物 / 烹饪面板（2026-10-02 批 C）的端到端断言：
    /// ① 两个入口按钮已开闸（不再是「常驻禁用」）；② 草稿制 —— 放进篝火不消耗背包，取回即撤销；
    /// ③ 效果饱食度上限 10：首个越限者及其后只给饱食度（进度行与提示都要反映）；
    /// ④ 过期食物拒绝放入；⑤ 烹饪：未放行配方拒绝、上限 2 次、只消耗食物、不消耗时间点。
    /// 走出这里时篝火草稿里留 3 件食物（饱食 3+3+4 = 10），供 ② 段休息结算断言真实回复与效果落档。
    /// </summary>
    private async System.Threading.Tasks.Task RunCampFoodPanelSmoke(RunSession run, CampScene camp)
    {
        Require(run?.Current != null && camp != null && GodotObject.IsInstanceValid(camp), "食物烟测需要营地与进行中的本局。");
        Require(LoadingSystem.FoodDictionary.Count > 0 && LoadingSystem.FoodRecipeDictionary.Count > 0,
            "食物烟测需要 Food.csv / FoodRecipe.csv 已加载（LoadingSystem.EnsureAllDataLoaded）。");
        RunSaveData data = run.Current;

        // ① 入口开闸：食物 / 烹饪按钮不再是「常驻禁用」。
        Require(!camp.FoodButtonDisabled && !camp.CookButtonDisabled,
            "「添加食物」「烹饪」按钮必须可用（2026-10-02 批 C 已接入食物系统与配方）。");
        Require(!camp.FoodPanelVisible && !camp.CookPanelVisible, "营地初始不应展开食物 / 烹饪面板。");

        // 备料：3 件有效（402 饱食 3 + 403 饱食 3 + 404 饱食 4 = 10，刚好到效果上限）、
        // 1 件将腐坏（406，有效期 1 天）、1 件已过期（401 手工改成 0）。
        List<int> plannedKeys = new List<int> { 402, 403, 404 };
        foreach (int key in plannedKeys)
        {
            RunBagEntrySave entry = RunBagSystem.Add(data, BagCategory.Food, key, 1);
            Require(entry != null, $"备料失败：食物 {key} 未能进入背包。");
            campFoodPlanIds.Add(entry.InstanceId);
        }

        RunBagEntrySave spoiling = RunBagSystem.Add(data, BagCategory.Food, 406, 1);
        Require(spoiling != null, "备料失败：食物 406 未能进入背包。");
        campFoodExpiredIds.Add(spoiling.InstanceId);

        RunBagEntrySave expired = RunBagSystem.Add(data, BagCategory.Food, 401, 1);
        Require(expired != null, "备料失败：食物 401 未能进入背包。");
        expired.ExpireDaysRemaining = 0;
        run.Save();

        // ② 打开面板（真点击），过期食物被拒绝、有效食物进草稿且**不消耗**背包。
        FindButton(camp, CampScene.FoodButtonText).EmitSignal(BaseButton.SignalName.Pressed);
        await WaitFrames(2);
        Require(camp.FoodPanelVisible, "点「添加食物」应展开食物面板。");

        string expiredError = string.Empty;
        bool expiredAdded = camp.TryAddFoodToCampFire(expired.InstanceId, out expiredError);
        Require(!expiredAdded && expiredError.Contains("过期"),
            $"过期食物不得放进篝火，实际：{expiredAdded} / {expiredError}。");
        Require(camp.PlannedSatiety == 0 && RunBagSystem.CountOf(data, BagCategory.Food, 401) == 1,
            "被拒绝的过期食物不得进入草稿，也不得离开背包。");

        for (int i = 0; i < campFoodPlanIds.Count; i++)
        {
            string error = string.Empty;
            Require(camp.TryAddFoodToCampFire(campFoodPlanIds[i], out error),
                $"有效食物应能放进篝火，实际：{error}。");
            Require(RunBagSystem.Find(data, campFoodPlanIds[i]) != null,
                "草稿里的食物在点「休息」前必须仍在背包（未消耗）。");
        }

        Require(camp.PlannedEntryCount == 3 && camp.PlannedSatiety == 10 && camp.PlannedEffectiveSatiety == 10,
            $"3 件食物（3+3+4）应刚好到效果上限：实际 件数 {camp.PlannedEntryCount}、"
            + $"饱食 {camp.PlannedSatiety}、计入效果 {camp.PlannedEffectiveSatiety}。");
        Require(camp.SatietyProgressText.Contains("10 / 10") && camp.SatietyProgressText.Contains("计入效果 10"),
            $"饱食度进度行应显示 10 / 10（计入效果 10），实际 {camp.SatietyProgressText}。");

        // ③ 越限：第 4 件（405 饱食 4）→ 累计 14 > 10：只给饱食度、不给效果。
        RunBagEntrySave over = RunBagSystem.Add(data, BagCategory.Food, 405, 1);
        string overError = string.Empty;
        Require(camp.TryAddFoodToCampFire(over.InstanceId, out overError),
            $"第 4 件食物应能放进篝火，实际：{overError}。");
        Require(camp.PlannedSatiety == 14 && camp.PlannedEffectiveSatiety == 10,
            $"首个越限者及其后只给饱食度：期望 14 / 计入 10，实际 {camp.PlannedSatiety} / {camp.PlannedEffectiveSatiety}。");
        Require(camp.FoodHint.Contains("只给饱食度"), $"越限提示应说明只给饱食度，实际 {camp.FoodHint}。");

        string duplicateError = string.Empty;
        Require(!camp.TryAddFoodToCampFire(over.InstanceId, out duplicateError) && duplicateError.Contains("已经在篝火"),
            $"同一件食物不得重复放进篝火，实际：{duplicateError}。");

        string removeError = string.Empty;
        Require(camp.TryRemoveFoodFromCampFire(over.InstanceId, out removeError),
            $"应能把食物从篝火取回，实际：{removeError}。");
        Require(camp.PlannedEntryCount == 3 && camp.PlannedSatiety == 10 && camp.PlannedEffectiveSatiety == 10,
            $"取回越限食物后应回到 10 / 10，实际 {camp.PlannedSatiety} / {camp.PlannedEffectiveSatiety}。");
        Require(RunBagSystem.Find(data, over.InstanceId) != null && RunBagSystem.CountOf(data, BagCategory.Food, 405) == 1,
            "取回只撤销草稿：背包里的食物一件都不能少。");

        // ④ 烹饪面板：5 条放行 + 7 条材料配方未放行（2026-10-02 口径 ①）；上限 2 次、只消耗食物、不消耗时间点。
        FindButton(camp, CampScene.CookButtonText).EmitSignal(BaseButton.SignalName.Pressed);
        await WaitFrames(2);
        Require(camp.CookPanelVisible && !camp.FoodPanelVisible,
            "点「烹饪」应展开烹饪面板并收起食物面板（三块面板互斥）。");
        Require(CampScene.EnabledRecipes().Count == 5,
            $"FoodRecipe.csv 应放行 5 条食物配方，实际 {CampScene.EnabledRecipes().Count}。");
        Require(LoadingSystem.FoodRecipeDictionary.Count == 12,
            $"FoodRecipe.csv 应共 12 条（5 放行 + 7 条材料配方暂禁用），实际 {LoadingSystem.FoodRecipeDictionary.Count}。");

        string lockedError = string.Empty;
        Require(!camp.TryCookRecipe(1, out lockedError) && lockedError.Contains("未放行"),
            $"未放行的材料配方不得合成，实际：{lockedError}。");
        Require(camp.CookHint.Contains("未放行"), $"烹饪提示应说明材料配方未放行，实际 {camp.CookHint}。");

        float timeBefore = data.MapState.TimePoints;
        string reservedError = string.Empty;
        Require(!camp.TryCookRecipe(101, out reservedError) && reservedError.Contains("不足"),
            $"篝火草稿里的食物不能被当烹饪原料（101 需要烤蟾蜍 ×2，唯一一件已放进篝火），实际：{reservedError}。");

        RunBagSystem.Add(data, BagCategory.Food, 403, 2); // 非草稿的烤蟾蜍 ×2 → 101（403×2 → 404）可合成
        string cookError = string.Empty;
        Require(camp.TryCookRecipe(101, out cookError), $"烤蟾蜍 ×2 应能合成蜂蜜烤肉，实际：{cookError}。");
        Require(camp.CookedThisRest == 1, $"合成一次后本次休息次数应为 1，实际 {camp.CookedThisRest}。");
        Require(RunBagSystem.CountOf(data, BagCategory.Food, 403) == 1 && RunBagSystem.CountOf(data, BagCategory.Food, 404) == 2,
            $"合成应只扣非草稿输入并产出成菜（403 剩 1 = 草稿那件、404 共 2 件 = 草稿 1 + 成菜 1），"
            + $"实际 403 {RunBagSystem.CountOf(data, BagCategory.Food, 403)}、404 {RunBagSystem.CountOf(data, BagCategory.Food, 404)}。");

        RunBagSystem.Add(data, BagCategory.Food, 402, 2); // 非草稿的香草炖菜 ×2 → 102（402×2 → 405）
        Require(camp.TryCookRecipe(102, out cookError), $"香草炖菜 ×2 应能合成月光浓汤，实际：{cookError}。");
        Require(camp.CookedThisRest == 2, $"合成两次后本次休息次数应为 2，实际 {camp.CookedThisRest}。");

        foreach (string instanceId in campFoodPlanIds)
        {
            Require(RunBagSystem.Find(data, instanceId) != null, "烹饪不得消耗篝火草稿里已规划的食物实例。");
        }

        string limitError = string.Empty;
        Require(!camp.TryCookRecipe(105, out limitError) && limitError.Contains("最多合成 2 次"),
            $"第 3 次合成必须被「每次休息 ≤ 2 次」拒绝，实际：{limitError}。");
        Require(Math.Abs(data.MapState.TimePoints - timeBefore) < 1e-4f, "合成食物不得消耗时间点（交互案「烹饪」）。");
        Require(camp.PlannedEntryCount == 3 && camp.PlannedSatiety == 10,
            "烹饪不得改动篝火草稿（成菜只入背包，玩家可自己再加入）。");

        // 预期效果类型（休息后要落进 ActiveFoodEffects）：直接取配表注册表，避免烟测里写死效果号。
        foreach (int key in plannedKeys)
        {
            foreach (ItemEffectSpec spec in ItemNameResolver.FoodEffectsOf(key))
            {
                campFoodEffectTypes.Add((int)spec.Type);
            }
        }

        // 收尾：回到「添加食物」面板，让截图留下一张新面板的可视记录（草稿仍是 3 件 / 10 点饱食度）。
        FindButton(camp, CampScene.FoodButtonText).EmitSignal(BaseButton.SignalName.Pressed);
        await WaitFrames(2);
        Require(camp.FoodPanelVisible && camp.PlannedEntryCount == 3, "收尾应回到食物面板且草稿保持 3 件食物。");
        GD.Print($"RUN_FLOW_UI_SMOKE_CAMP_FOOD: 篝火饱食度 {camp.PlannedSatiety}/10（计入效果 {camp.PlannedEffectiveSatiety}）、"
            + $"本次休息已烹饪 {camp.CookedThisRest}/2、已放行配方 {CampScene.EnabledRecipes().Count} 条。");
    }

    /// <summary>
    /// `RunSession`（autoload 单例）存档面的运行时自查（2026-10-02 事故复盘：`RunSession.cs` 曾被文本改写截断，
    /// 前缀由权重重写 —— 该文件的每个成员都必须有一个「真跑一遍运行时 + 真实文件 IO」的守护）。
    /// ① `StartNewRun` 建档口径（HP / 默认卡组 / 初始武器 / 种子 / 背包初始态）；② `Save → ClearCurrent → LoadSave`
    /// 的**逐字往返**（整份 DTO 序列化字符串相等 ⇒ 没有字段在重建时丢语义）；③ `GetSlot / GetSlotDeck /
    /// AddCardToSlotDeck` 边界；④ `SetCurrentNode` 落档 + 已访问幂等 + 不改敌袭档位计数；⑤ `TryAddTimePoints /
    /// TrySpendTimePoints` 的拒绝路径与食物 `TimePoint` 寿命轴联动；⑥ `BeginRestDay / ApplyRest(plan)` 的回复 /
    /// 消耗 / 跨天腐坏 / `DayCount` 轴 / 烹饪次数归零；⑦ `AbortRun / DeleteSave` 真删文件与失败路径；
    /// ⑧ v3 旧档 → v4 迁移 + `MigrateSettlementCompat` 的旧结算候选还原（走真实 `LoadSave`）。
    /// </summary>
    private void RunSessionReconstructionSmoke()
    {
        RunSession run = RunSession.Instance;
        Require(run != null, "RunSession 自检需要 autoload 单例（project.godot 的 RunSession）。");

        // 本段要反复覆盖 user:// 存档：先备份，结束时还原（RunFlowScene 收尾还会整体还原玩家档）。
        byte[] backup = FileAccess.FileExists(RunSession.SavePath) ? FileAccess.GetFileAsBytes(RunSession.SavePath) : null;
        try
        {
            // ① StartNewRun：3 名角色 + 固定种子
            run.StartNewRun(new[] { 1002, 1003, 1004 }, 20261002);
            Require(run.HasActiveRun && run.Current != null, "StartNewRun 后必须有进行中的本局。");
            RunSaveData data = run.Current;
            Require(data.SchemaVersion == RunSaveData.CurrentSchemaVersion
                    && data.SchemaVersion.ToString() == RunSession.SaveSchemaVersion,
                "存档版本号必须同步（RunSaveData.CurrentSchemaVersion 与 RunSession.SaveSchemaVersion），"
                + $"实际 {data.SchemaVersion} / {RunSession.SaveSchemaVersion}。");
            Require(data.GameMode == RunGameModes.OnMap && data.Gold == 0 && data.Keys == 0,
                "新局应从 OnMap 起步、金币与钥匙归零。");
            Require(data.MapState.Act == 1 && data.MapState.Seed == 20261002 && data.MapState.CurrentNodeId == -1,
                $"新局应从第一层 / 给定种子 / 未落格起步，实际 Act {data.MapState.Act}、Seed {data.MapState.Seed}、"
                + $"Node {data.MapState.CurrentNodeId}。");
            Require(Math.Abs(data.MapState.TimePoints) < 1e-4f && Math.Abs(data.MapState.RemainingToday - 4f) < 1e-4f,
                $"新局时间点进程应为 0（当天剩余 4.0），实际 {data.MapState.TimePoints} / {data.MapState.RemainingToday}。");
            Require(run.CurrentDay == 1 && Math.Abs(run.RemainingToday - 4f) < 1e-4f, "新局应是第 1 天满额。");
            Require(data.BagEntries.Count == 0 && data.ActiveFoodEffects.Count == 0 && data.CookedThisRest == 0,
                "新局背包 / 食物效果 / 烹饪次数必须为空。");
            Require(data.CarryItemSlots.Count == RunBagSystem.CarryItemSlotCount, "新局随身格必须补齐 3 格。");
            Require(data.CharacterSlots.Count == 3 && data.DeckSlots.Count == 3, "新局应有 3 个角色槽与 3 副卡组。");

            Dictionary<int, string> expectedWeapons = new Dictionary<int, string>
            { [1002] = "双手剑", [1003] = "弓箭", [1004] = "法典" };
            HashSet<string> weaponIds = LoadDefinitionIds("res://DataBase/Equipment/Weapon.csv");
            for (int i = 0; i < data.CharacterSlots.Count; i++)
            {
                RunCharacterSlotSave slot = data.CharacterSlots[i];
                Require(LoadingSystem.CharacterDictionary.TryGetValue(slot.CharacterId, out var template) && template != null,
                    $"角色 {slot.CharacterId} 必须能在 Character.csv 里找到。");
                Require(slot.MaxHp == template.MAX_HP && slot.CurrentHp == template.MAX_HP && slot.MaxHp > 0,
                    $"槽 {i} 的 HP 应取 Character.csv 上限满血，实际 {slot.CurrentHp}/{slot.MaxHp}（表 {template.MAX_HP}）。");
                Require(expectedWeapons.TryGetValue(slot.CharacterId, out string weapon) && slot.EquippedWeaponDefinitionId == weapon,
                    $"槽 {i} 的初始武器应是 {weapon}，实际 {slot.EquippedWeaponDefinitionId}。");
                Require(weaponIds.Contains(slot.EquippedWeaponDefinitionId),
                    $"初始武器 {slot.EquippedWeaponDefinitionId} 必须能在 Weapon.csv 的 DefinitionId 里找到。");
                List<int> defaults = LoadingSystem.GetCharacterDefaultCardIdListByKey(
                    slot.CharacterId, LoadingSystem.CharacterDefaultDeckCsvPathKey, true);
                Require(data.DeckSlots[i].Count == defaults.Count,
                    $"槽 {i} 的默认卡组张数应等于 CharacterDefaultDeck.csv（{defaults.Count}），实际 {data.DeckSlots[i].Count}。");
                for (int c = 0; c < defaults.Count; c++)
                {
                    Require(data.DeckSlots[i][c].CardId == defaults[c] && data.DeckSlots[i][c].PermanentUpgradeLevel == 0,
                        $"槽 {i} 第 {c} 张默认卡应是 {defaults[c]}（0 级），实际 {data.DeckSlots[i][c].CardId}。");
                }
            }

            // ② Save → ClearCurrent → LoadSave：整份 DTO 逐字往返（覆盖所有字段，不靠逐字段列举）
            data.Gold = 17;
            data.Keys = 2;
            data.MapState.TimePoints = 1.7f;
            data.BagEntries.Add(RunBagSystem.CreateEntry(data, BagCategory.Food, 402, 2, "smoke-food-a"));
            // 随身格的真相是条目的 `CarrySlot`（SchemaVersion 5）：走 API 而不是直接改镜像列表，
            // 否则「落档 → 读档迁移 → 再序列化」会因迁移补齐而不再逐字一致。
            Require(RunBagSystem.TrySetCarrySlot(data, 0, "smoke-food-a", out string carryError),
                $"随身格夹具应接受该实例键：{carryError}");
            ItemEffectSpec spec = ItemNameResolver.FoodEffectsOf(402).FirstOrDefault();
            Require(spec != null, "自检需要一条可落档的食物效果（Food.csv 402 应带效果）。");
            RunFoodEffectSave effect = RunFoodSystem.BuildEffectSave(spec, "香草炖菜");
            effect.DurationKind = (int)FoodEffectDurationKind.TimePoint;
            effect.Remaining = 0.3f;
            data.ActiveFoodEffects.Add(effect);
            run.Save();
            string saved = FileAccess.GetFileAsString(RunSession.SavePath);
            Require(RunSaveJson.Deserialize(saved) != null,
                $"落档文件必须是可解析的 JSON（写坏存档是最坑的症状，实际 {saved.Length} 字节）。");
            run.ClearCurrent();
            Require(!run.HasActiveRun && run.Current == null, "ClearCurrent 后不得还有当前局。");
            Require(RunSession.HasSave(), "ClearCurrent 不得删档。");
            Require(run.LoadSave(), "LoadSave 应能从刚写下的存档读回来。");
            Require(RunSaveJson.Serialize(run.Current) == saved,
                "读档后的序列化必须与落档内容逐字一致（任何字段在重建时丢语义都会在这里暴露）。");
            Require(run.Current.Gold == 17 && run.Current.Keys == 2 && Math.Abs(run.Current.MapState.TimePoints - 1.7f) < 1e-4f,
                "读档后金币 / 钥匙 / 时间点必须原样恢复。");
            Require(RunBagSystem.AllEntriesOf(run.Current, BagCategory.Food).Sum(x => x.Count) == 2
                    && RunBagSystem.CountOf(run.Current, BagCategory.Food, 402) == 0
                    && run.Current.CarryItemSlots[0] == "smoke-food-a"
                    && RunBagSystem.CarrySlotEntry(run.Current, 0)?.InstanceId == "smoke-food-a"
                    && run.Current.ActiveFoodEffects.Count == 1
                    && Math.Abs(run.Current.ActiveFoodEffects[0].Remaining - 0.3f) < 1e-4f,
                "读档后背包实例 / 随身格归属 / 食物效果寿命轴必须原样恢复（随身格上的食物只算整包件数，不计背包内可用件数）。");

            // ③ 角色槽 / 卡组 API 边界
            Require(run.GetSlot(-1) == null && run.GetSlot(99) == null, "越界的角色槽必须返回 null。");
            int deckBefore = run.GetSlotDeck(0).Count;
            run.AddCardToSlotDeck(0, 0);        // 无效卡（≤ 0）不得入组
            run.AddCardToSlotDeck(99, 1234);    // 越界槽不得写入、不得崩溃
            Require(run.GetSlotDeck(0).Count == deckBefore, "无效卡 / 越界槽都不得改动卡组。");
            run.AddCardToSlotDeck(0, 1234, 3);
            Require(run.GetSlotDeck(0).Count == deckBefore + 1 && run.GetSlotDeck(0).Last().CardId == 1234
                    && run.GetSlotDeck(0).Last().PermanentUpgradeLevel == 3,
                "AddCardToSlotDeck 应把卡与永久升级级数追加到该槽。");
            Require(run.GetSlotDeck(99).Count == 0, "越界槽的卡组应是空表（不是 null）。");

            // ④ 位置落档 + 已访问幂等 + 不改普通敌袭档位计数（口径：战斗胜利时按 NormalCombat 增量）
            run.SetCurrentNode(7);
            Require(run.Current.MapState.CurrentNodeId == 7, "SetCurrentNode 应记录当前格点。");
            Require(RunSaveJson.Deserialize(FileAccess.GetFileAsString(RunSession.SavePath)).MapState.CurrentNodeId == 7,
                "SetCurrentNode 必须立刻落档（读文件核对）。");
            run.MarkCurrentNodeVisitedAndAdvanceEncounter();
            run.MarkCurrentNodeVisitedAndAdvanceEncounter();
            run.MarkCurrentNodeVisitedAndAdvanceEncounter();
            Require(run.Current.MapState.VisitedNodeIds.Count(x => x == 7) == 1,
                $"已访问记录必须幂等（同一格点只记一次），实际记了 {run.Current.MapState.VisitedNodeIds.Count(x => x == 7)} 次。");
            Require(run.Current.MapState.GetNormalEncounterCount(1) == 0,
                "MarkCurrentNodeVisitedAndAdvanceEncounter 不得改普通敌袭档位计数（否则会重复计档）。");
            run.Current.MapState.IncrementCurrentNormalEncounterCount();
            run.MarkCurrentNodeVisitedAndAdvanceEncounter();
            Require(run.Current.MapState.GetNormalEncounterCount(1) == 1, "敌袭档位计数应只由显式增量改动。");

            // ⑤ 时间点：拒绝负向 / 不足一个计量单位；进账与支付都必须落档，并联动食物 TimePoint 寿命轴
            run.Current.MapState.TimePoints = 0f;
            Require(!run.TryAddTimePoints(-1f, out string negativeError)
                    && Math.Abs(run.Current.MapState.TimePoints) < 1e-4f,
                $"负向写入时间点必须被拒绝且不动档，实际：{negativeError}。");
            Require(!run.TryAddTimePoints(0.04f, out _) && Math.Abs(run.Current.MapState.TimePoints) < 1e-4f,
                "不足一个计量单位（0.04 → 0.0）的增量不得进账。");
            Require(run.TryAddTimePoints(0.05f, out _) && Math.Abs(run.Current.MapState.TimePoints - 0.1f) < 1e-4f,
                "0.05 按 MidpointRounding.AwayFromZero 进到 0.1（口径：对齐 0.1 计量单位）。");
            run.Current.MapState.TimePoints = 0f;
            Require(run.TryAddTimePoints(3.9f, out _) && Math.Abs(run.Current.MapState.TimePoints - 3.9f) < 1e-4f,
                "3.9 时间点应正常进账（当天剩余 0.1）。");
            Require(run.Current.ActiveFoodEffects.All(x => x.DurationKind != (int)FoodEffectDurationKind.TimePoint),
                "3.9 时间点的进程应把 TimePoint 轴（0.3）扣尽并移除 —— 时间点变动确实接到了寿命轴上。");
            Require(run.TryAddTimePoints(0.1f, out _), "跨日界的 0.1 应进账。");
            Require(run.Current.MapState.PendingRestDay && Math.Abs(run.Current.MapState.RestRemainingTimePoints - 0.1f) < 1e-4f,
                $"跨过日界必须记下当天剩余并置「待休息」，实际 {run.Current.MapState.RestRemainingTimePoints} / "
                + $"{run.Current.MapState.PendingRestDay}。");

            RunFoodEffectSave timed = RunFoodSystem.BuildEffectSave(spec, "香草炖菜");
            timed.DurationKind = (int)FoodEffectDurationKind.TimePoint;
            timed.Remaining = 0.2f;
            run.Current.ActiveFoodEffects.Add(timed);
            run.Current.MapState.TimePoints = 3.9f; // 烟测直接改档：当天只剩 0.1，用来验证「不足则整笔拒绝」
            Require(!run.TrySpendTimePoints(0.2f, out string spendError) && timed.Remaining == 0.2f,
                $"时间点不足时必须整笔拒绝、且不得扣食物寿命轴，实际：{spendError} / 剩余 {timed.Remaining}。");
            run.Current.MapState.TimePoints = 0f; // 复位到当天满额（4.0），验证成功支付路径
            Require(run.TrySpendTimePoints(0.1f, out _) && Math.Abs(timed.Remaining - 0.1f) < 1e-4f,
                $"成功支付 0.1 时间点应把 TimePoint 轴扣到 0.1，实际 {timed.Remaining}。");
            Require(run.TrySpendTimePoints(0.1f, out _) && !run.Current.ActiveFoodEffects.Contains(timed),
                "TimePoint 轴用尽后该效果必须从存档里移除。");

            // ⑥ BeginRestDay / ApplyRest(plan)：回复按真实饱食度、草稿消耗、跨天腐坏、DayCount 轴、烹饪次数归零
            foreach (RunCharacterSlotSave slot in run.Current.CharacterSlots)
            {
                slot.CurrentHp = Math.Max(1, slot.MaxHp / 2);
            }

            // BeginRestDay 只在「尚不待休息」时记剩余（已在待休息态时保留跨日界那一刻的记录值）→ 先复位成确定态。
            run.Current.MapState.TimePoints = 1f;
            run.Current.MapState.PendingRestDay = false;
            run.BeginRestDay();
            Require(run.Current.MapState.PendingRestDay && Math.Abs(run.Current.MapState.RestRemainingTimePoints - 3f) < 1e-4f,
                "BeginRestDay 应记下当刻的当天剩余（进程 1.0 → 剩余 3.0）并置「待休息」，实际 "
                + $"{run.Current.MapState.RestRemainingTimePoints} / {run.Current.MapState.PendingRestDay}。");
            RunBagEntrySave restFood = RunBagSystem.Add(run.Current, BagCategory.Food, 404, 1);   // 饱食 4、3 天
            RunBagEntrySave spoiling = RunBagSystem.Add(run.Current, BagCategory.Food, 406, 1);  // 1 天 → 本次休息后腐坏
            RunFoodEffectSave dayEffect = RunFoodSystem.BuildEffectSave(spec, "香草炖菜");
            dayEffect.DurationKind = (int)FoodEffectDurationKind.DayCount;
            dayEffect.Remaining = 2f;
            run.Current.ActiveFoodEffects.Add(dayEffect);
            run.Current.CookedThisRest = 2;
            Require(RunFoodSystem.TryBuildPlan(run.Current, new[] { restFood.InstanceId },
                    out RunFoodSystem.CampFirePlan plan, out string planError),
                $"篝火草稿应能建起来，实际：{planError}。");
            int[] hpBefore = run.Current.CharacterSlots.Select(x => x.CurrentHp).ToArray();
            int[] healExpected = new int[hpBefore.Length];
            for (int i = 0; i < hpBefore.Length; i++)
            {
                healExpected[i] = RunRestResolver.PreviewHeal(run.Current, i, RunWatchMode.None, 0, plan.TotalSatiety);
            }

            List<int> healed = run.ApplyRest(RunWatchMode.None, 0, plan);
            Require(healed.Count == hpBefore.Length, "ApplyRest 必须按角色槽逐条返回回复量。");
            for (int i = 0; i < hpBefore.Length; i++)
            {
                int expected = Math.Min(run.Current.CharacterSlots[i].MaxHp, hpBefore[i] + healExpected[i]);
                Require(run.Current.CharacterSlots[i].CurrentHp == expected,
                    $"ApplyRest 回复量应含篝火饱食度（槽 {i} 期望 {expected}，实际 {run.Current.CharacterSlots[i].CurrentHp}）。");
            }

            Require(RunBagSystem.Find(run.Current, restFood.InstanceId) == null, "休息结算应消耗草稿里的食物实例。");
            Require(RunBagSystem.Find(run.Current, spoiling.InstanceId) == null, "跨天休息应移除有效期归零的食物。");
            Require(run.Current.ActiveFoodEffects.Any(x => x.SourceFoodId == "蜂蜜烤肉"
                    && x.DurationKind == (int)FoodEffectDurationKind.BattleCount && Math.Abs(x.Remaining - 1f) < 1e-4f),
                "吃掉的 404 应把它的 BattleCount:1 效果写进存档。");
            Require(Math.Abs(dayEffect.Remaining - 1f) < 1e-4f, $"跨天应扣 DayCount 轴（2 → 1），实际 {dayEffect.Remaining}。");
            Require(run.Current.CookedThisRest == 0, "休息结算后本次休息的烹饪次数必须归零。");
            Require(Math.Abs(run.Current.MapState.TimePoints - 4f) < 1e-4f && run.CurrentDay == 2 && !run.Current.MapState.PendingRestDay,
                $"休息后应推进到第 2 天满额并清掉「待休息」，实际 {run.Current.MapState.TimePoints} / 第 {run.CurrentDay} 天。");

            // ⑦ AbortRun / DeleteSave：真删文件；没有当前局时 Save 不得写档
            run.AbortRun();
            Require(!run.HasActiveRun && !RunSession.HasSave(), "AbortRun 必须删档并清掉当前局。");
            Require(!run.LoadSave(), "存档不存在时 LoadSave 必须返回 false。");
            Require(run.Current == null, "读档失败不得改动内存里的当前局。");
            run.Save();
            Require(!RunSession.HasSave(), "没有当前局时 Save 不得写出存档文件。");

            // ⑧ v3 旧档 → v4 迁移 + 旧结算候选按「1 份」还原（走真实 LoadSave，不绕过读档路径）
            run.StartNewRun(new[] { 1002, 1003, 1004 }, 777);
            RunSaveData legacy = RunSaveJson.Deserialize(RunSaveJson.Serialize(run.Current));
            legacy.SchemaVersion = 3;
            legacy.BagEntries.Clear();
            legacy.CarryItemSlots.Clear();
            legacy.ActiveFoodEffects.Clear();
            legacy.Materials[101] = 4;
            legacy.Items[301] = 2;
            legacy.Equipment[9001] = 1;
            legacy.SettlementCandidateCardIds = new List<int> { 501, 502, 503 };
            legacy.SettlementCardPools.Clear();
            legacy.SettlementCardClaims.Clear();
            legacy.MapState.NormalEncounterIndex = 2;
            legacy.MapState.NormalEncounterCounts.Clear();
            WriteRunSave(RunSaveJson.Serialize(legacy));

            Require(run.LoadSave(), "v3 旧档必须能读进来（不弃档）。");
            RunSaveData migrated = run.Current;
            Require(migrated.SchemaVersion == RunSaveData.CurrentSchemaVersion, "读档后必须标到当前版本。");
            Require(migrated.CarryItemSlots.Count == RunBagSystem.CarryItemSlotCount, "旧档随身格必须补齐 3 格。");
            Require(RunBagSystem.CountOf(migrated, BagCategory.Material, 101) == 4
                    && RunBagSystem.CountOf(migrated, BagCategory.Item, 301) == 2
                    && RunBagSystem.CountOf(migrated, BagCategory.Equipment, 9001) == 1,
                "旧档的三个计数字典必须原样展开成 BagEntries（不丢数）。");
            Require(migrated.SettlementCardPools.Count == 1
                    && migrated.SettlementCardPools[0].SlotIndex == SettlementRewardPresenter.LegacySlotIndex
                    && migrated.SettlementCardPools[0].CandidateCardIds.SequenceEqual(new[] { 501, 502, 503 }),
                "旧档的单份结算候选必须按「1 份」还原到 SettlementCardPools（MigrateSettlementCompat）。");
            Require(migrated.MapState.NormalEncounterIndex == 0 && migrated.MapState.GetNormalEncounterCount(1) == 2,
                "旧档全局敌袭计数必须迁入当前层并清零（P2-11）。");
            // 迁移只在内存完成（`LoadSave` 不写档）→ 落档要等下一次 `Save()`；再读回应幂等（不二次展开）。
            Require(RunSaveJson.Deserialize(FileAccess.GetFileAsString(RunSession.SavePath)).BagEntries.Count == 0,
                "读档迁移不得改写存档文件（口径：迁移在内存完成，落档等下一次 Save）。");
            run.Save();
            RunSaveData roundTripped = RunSaveJson.Deserialize(FileAccess.GetFileAsString(RunSession.SavePath));
            Require(roundTripped.BagEntries.Count == migrated.BagEntries.Count
                    && RunBagSystem.CountOf(roundTripped, BagCategory.Equipment, 9001) == 1,
                "迁移后落档应把展开出的 BagEntries 写进文件（读回数量与数量守恒一致）。");
            run.ClearCurrent();
            Require(run.LoadSave() && RunBagSystem.CountOf(run.Current, BagCategory.Material, 101) == 4,
                "已是 v4 的档再读一次不得把旧字典二次展开（迁移幂等）。");
            Require(run.Current.SettlementCardPools.Count == 1, "重读后结算候选份数不得翻倍（MigrateSettlementCompat 幂等）。");
        }
        finally
        {
            if (backup != null)
            {
                // 注意：写句柄必须**先关掉**再回读 —— FileAccess 有内部缓冲，未 Close 时另开的读句柄会读到旧内容
                // （2026-10-02 实测：在 `using` 里直接 GetFileAsString 会稳定读到上一份更长的存档，误报「存档写坏」）。
                using (FileAccess restore = FileAccess.Open(RunSession.SavePath, FileAccess.ModeFlags.Write))
                {
                    restore?.StoreBuffer(backup);
                }

                string restored = FileAccess.GetFileAsString(RunSession.SavePath);
                bool parseable = true;
                try
                {
                    parseable = RunSaveJson.Deserialize(restored) != null;
                }
                catch (Exception ex)
                {
                    parseable = false;
                    GD.PrintErr($"[RunSession自检] 备份存档解析失败：{ex.Message}");
                }

                GD.Print($"[RunSession自检] 备份存档回写 {backup.Length} 字节（可解析={parseable}）。");
                run.LoadSave();
            }
        }

        GD.Print("RUN_SESSION_SMOKE_PASS: StartNewRun 建档 / Save-LoadSave 逐字往返 / ClearCurrent 不删档 / "
            + "卡组边界 / SetCurrentNode+已访问幂等 / 时间点拒绝路径+寿命轴 / ApplyRest(plan) / AbortRun 删档");
    }


    /// <summary>读一张 CSV 的第 2 列（`DefinitionId`）集合：自检用它在不引入配表 API 的前提下校验初始武器名。</summary>
    private static HashSet<string> LoadDefinitionIds(string resPath)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in LoadCsv.LoadCSVDataLines(resPath))
        {
            string[] fields = LoadCsv.ParseCSVFields(line);
            if (fields.Length >= 2 && fields[1].Length > 0 && fields[1] != "DefinitionId")
            {
                ids.Add(fields[1]);
            }
        }

        return ids;
    }

    /// <summary>把一段 JSON 直接写成运行局存档（`RunSession` 自检要伪造一份 v3 旧档）。</summary>
    private static void WriteRunSave(string json)
    {
        using FileAccess file = FileAccess.Open(RunSession.SavePath, FileAccess.ModeFlags.Write);
        file?.StoreString(json);
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

    /// <summary>等营地休息流程把营地真的销毁（淡出是 0.35s 的秒级 Tween，不能按帧数死等）。</summary>
    private async System.Threading.Tasks.Task WaitUntilCampDestroyed()
    {
        int frames = 0;
        while (camp != null && GodotObject.IsInstanceValid(camp) && frames++ < 600) await WaitFrames(1);
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

    /// <summary>
    /// 数结算面板上**还看得见**的物品 Tab（2026-10-02 口径「领取后直接消失」的断言入口）：
    /// 领取过的条目不再渲染，所以条数应随每次领取减一。
    /// </summary>
    private static int CountUnclaimedItemTabs(Node node)
    {
        int count = node is Button button && !button.Disabled && IsRewardLineText(button.Text) ? 1 : 0;
        foreach (Node child in node.GetChildren())
        {
            count += CountUnclaimedItemTabs(child);
        }
        return count;
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

    /// <summary>找第一个下拉框（营地守夜承担者）。</summary>
    private static OptionButton FindOptionButton(Node node)
    {
        if (node is OptionButton option) return option;
        foreach (Node child in node.GetChildren())
        {
            OptionButton found = FindOptionButton(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>取某点周围 r 像素内的最大红分量：判定自绘篝火是否真的画在了屏幕上（暗底 ≈ 0.04）。</summary>
    private static float MaxRedAround(Image image, Vector2 center, int radius)
    {
        float max = 0f;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = Mathf.Clamp((int)center.X + dx, 0, image.GetWidth() - 1);
                int y = Mathf.Clamp((int)center.Y + dy, 0, image.GetHeight() - 1);
                max = Mathf.Max(max, image.GetPixel(x, y).R);
            }
        }
        return max;
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
