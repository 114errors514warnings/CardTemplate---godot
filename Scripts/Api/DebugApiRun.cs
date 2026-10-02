// Scripts/Api/DebugApiRun.cs
// **调试 API** · 运行局域（通道 `Debug`）：全部**超出玩家范围** —— 选关 / 一键跳关 / 直接改时间点 /
// 塞物品 / 改血量与手位 / 清档 / 绕过整理闸门。只给自动化测试与排错用；
// 与玩家通道（`PlayerApiRun`）在文件、类、类型名前缀三处都分开。

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>运行局域 · 调试通道路由（`debug.run.*`）。</summary>
public sealed class DebugApiRun : IApiDomain
{
    public const string Domain = "debug.run";

    /// <summary>`debug.run.new_run` 不指定角色时的默认阵容（与 UI 烟测一致）。</summary>
    private static readonly List<int> DefaultCharacters = new() { 1002, 1003, 1004 };

    private const string RunFlowScenePath = "res://Scenes/Run/RunFlowScene.tscn";
    private const string MainMenuScenePath = "res://Scenes/MainMenu/MainMenuScene.tscn";

    /// <summary>指令元数据（静态表：纯 .NET 单测与 `api.catalog` 直接读，不 new 场景）。</summary>
    public static readonly ApiCommandInfo[] Table =
    {
        new("debug.run.new_run", ApiLane.Debug, "新建本局并进入运行局场景（**覆盖当前存档**）。", false, "characterIds,seed"),
        new("debug.run.enter", ApiLane.Debug, "进入运行局场景（沿用现有存档；没有存档时拒绝）。"),
        new("debug.run.select_level", ApiLane.Debug, "选关：直接以指定关卡开战（绕过地图、池档位、时间点与可达判定）。", false, "levelId"),
        new("debug.run.jump_event", ApiLane.Debug, "跳事件：直接进入指定事件（绕过地图与节点类型）。", false, "eventId"),
        new("debug.run.next_combat", ApiLane.Debug, "一键跳关：进入最近的**未访问战斗格**（无视可达与只读闸门）。"),
        new("debug.run.enter_node", ApiLane.Debug, "无视可达与只读闸门，直接进入指定格。", false, "nodeId"),
        new("debug.run.skip_node", ApiLane.Debug, "把当前格标成已访问并推进层内遭遇计数（不打架直接过一关）。"),
        new("debug.run.back_to_map", ApiLane.Debug, "放弃当前内容并回到可选地图（不结算、不领取；模块级验证的「随时回地图」捷径）。"),
        new("debug.run.map_state", ApiLane.Debug, "全图节点：类型 / 是否已访问 / 是否可达 / 当前所在 / 起终点。", true),
        new("debug.run.set_time_points", ApiLane.Debug, "直接设时间点（**允许负向**，可把当天耗光触发营地转场）。", false, "value"),
        new("debug.run.add_time_points", ApiLane.Debug, "加时间点（正向，走正式入口）。", false, "amount"),
        new("debug.run.set_slot_hp", ApiLane.Debug, "直接写角色槽血量（可同时改上限）。", false, "slotIndex,hp,maxHp"),
        new("debug.run.set_hand", ApiLane.Debug, "直接写左右手装备定义名（不校验部位组合）。", false, "slotIndex,hand,definitionId"),
        new("debug.run.add_bag_item", ApiLane.Debug, "直接塞一件背包物品（不做负荷校验）。", false, "category,definitionKey,count"),
        new("debug.run.clear_bag", ApiLane.Debug, "清空背包（含随身格里放着的条目）。"),
        new("debug.run.bag_entries", ApiLane.Debug, "原始背包条目（含随身归属 / 数量 / 到期天数）。", true),
        new("debug.run.set_carry_slot", ApiLane.Debug, "直接把某条目放进随身格。", false, "slot,instanceId"),
        new("debug.run.force_bag_gate", ApiLane.Debug, "强制背包闸门：reason 空 = 强制**放行**（内容进行中也能拖，供模块级验证）；非空 = 强制按该原因阻断。", false, "reason"),
        new("debug.run.set_suppress_confirm", ApiLane.Debug, "写「本局游戏内不再显示放弃确认」。", false, "suppress"),
        new("debug.run.abort_run", ApiLane.Debug, "放弃本局（清档 + 回主菜单）。"),
        new("debug.run.capture", ApiLane.Debug, "运行局截图到 Tests/ApiCaptures/（captureMode=compressed 出 1280×720）。", true, "name,captureMode"),
    };

    private readonly ApiRunContext context;
    private readonly Dictionary<string, Func<ApiRequest, ApiResult>> bindings;

    public DebugApiRun(ApiRunContext context)
    {
        this.context = context ?? new ApiRunContext();
        bindings = new Dictionary<string, Func<ApiRequest, ApiResult>>(StringComparer.Ordinal)
        {
            ["debug.run.new_run"] = NewRun,
            ["debug.run.enter"] = EnterRun,
            ["debug.run.select_level"] = SelectLevel,
            ["debug.run.jump_event"] = JumpEvent,
            ["debug.run.next_combat"] = NextCombat,
            ["debug.run.enter_node"] = EnterNode,
            ["debug.run.skip_node"] = SkipNode,
            ["debug.run.back_to_map"] = BackToMap,
            ["debug.run.map_state"] = request => Ok(request, "地图全景读取成功。", Scene.Map?.ApiMapState()),
            ["debug.run.set_time_points"] = SetTimePoints,
            ["debug.run.add_time_points"] = AddTimePoints,
            ["debug.run.set_slot_hp"] = SetSlotHp,
            ["debug.run.set_hand"] = SetHand,
            ["debug.run.add_bag_item"] = AddBagItem,
            ["debug.run.clear_bag"] = ClearBag,
            ["debug.run.bag_entries"] = request => Ok(request, "背包条目读取成功。", Session?.DebugBagEntries()),
            ["debug.run.set_carry_slot"] = SetCarrySlot,
            ["debug.run.force_bag_gate"] = SetBagGate,
            ["debug.run.set_suppress_confirm"] = SetSuppress,
            ["debug.run.abort_run"] = AbortRun,
            ["debug.run.capture"] = Capture,
        };
    }

    public string DomainName => Domain;
    public IReadOnlyList<ApiCommandInfo> Catalog => Table;
    public IReadOnlyList<string> BoundTypes => bindings.Keys.ToArray();

    public ApiResult Execute(ApiRequest request)
    {
        if (request?.Type == null || !bindings.TryGetValue(request.Type, out var handler)) return null;
        if (request.Type == "debug.run.new_run" || request.Type == "debug.run.enter")
        {
            // 这两条会把场景换掉，因此不要求已有运行局场景。
            if (Scene == null) return ApiResult.Fail(request.Type, ApiLane.Debug, "NO_SCENE", "当前没有可用的场景树（请从主菜单或运行局调用）。");
            return handler(request);
        }

        if (!context.HasScene) return ApiResult.Fail(request.Type, ApiLane.Debug, "NO_RUN_SCENE", "当前不是运行局场景（先进本局：debug.run.new_run / debug.run.enter）。");
        return handler(request);
    }

    // ── 基础设施 ───────────────────────────────────────────────

    private RunFlowScene Scene => context.Scene;
    private static RunSession Session => RunSession.Instance;

    private static ApiResult Ok(ApiRequest request, string message, object data) =>
        ApiResult.Success(request.Type, ApiLane.Debug, message, data);

    private static ApiResult Fail(ApiRequest request, string code, string message) =>
        ApiResult.Fail(request.Type, ApiLane.Debug, code, message);

    /// <summary>调试写指令的默认回包：本局全景 + **原始背包条目**（含随身归属 / 到期天数等隐藏信息）。</summary>
    private object DebugState() => new
    {
        state = ApiRunSnapshot.State(Scene),
        bagEntries = Session?.DebugBagEntries(),
        bagGate = Session?.BagArrangeBlockReason,
    };

    private ApiResult Done(ApiRequest request, string message) => Ok(request, message, DebugState());

    private void SwitchScene(string path) => Scene.GetTree().ChangeSceneToFile(path);

    // ── 开局 / 选关 / 跳关 ─────────────────────────────────────

    private ApiResult NewRun(ApiRequest request)
    {
        List<int> ids = request.CharacterIds != null && request.CharacterIds.Count > 0 ? request.CharacterIds : DefaultCharacters;
        LoadingSystem.EnsureAllDataLoaded();
        Session.StartNewRun(ids, request.Seed > 0 ? request.Seed : (int?)null);
        SwitchScene(RunFlowScenePath);
        return Ok(request, "已新建本局并进入运行局场景。", new { characters = ids, seed = request.Seed, scene = RunFlowScenePath });
    }

    private ApiResult EnterRun(ApiRequest request)
    {
        if (Session?.Current == null) return Fail(request, "NO_RUN", "当前没有进行中的本局（先 debug.run.new_run）。");
        SwitchScene(RunFlowScenePath);
        return Ok(request, "已回到运行局场景。", new { scene = RunFlowScenePath });
    }

    /// <summary>选关：直接以指定关卡开战（绕过地图、池档位、时间点与可达判定）。</summary>
    private ApiResult SelectLevel(ApiRequest request)
    {
        if (!Scene.DebugSelectLevel(request.LevelId, out string error)) return Fail(request, "SELECT_LEVEL_REJECTED", error);
        return Done(request, $"已进入关卡 {request.LevelId}。");
    }

    private ApiResult JumpEvent(ApiRequest request)
    {
        if (!Scene.DebugSelectEvent(request.EventId, out string error)) return Fail(request, "JUMP_EVENT_REJECTED", error);
        return Done(request, $"已进入事件 {request.EventId}。");
    }

    /// <summary>一键跳关：进最近的未访问战斗格（无视可达与只读闸门，时间点照常结算）。</summary>
    private ApiResult NextCombat(ApiRequest request)
    {
        if (!Scene.DebugNextCombat(out int nodeId, out string error)) return Fail(request, "NEXT_COMBAT_REJECTED", error);
        return Done(request, $"已跳到战斗格 {nodeId}。");
    }

    private ApiResult EnterNode(ApiRequest request)
    {
        if (Scene.Map == null || !Scene.Map.TryForceEnterNode(request.NodeId))
            return Fail(request, "ENTER_NODE_REJECTED", $"格 {request.NodeId} 不存在（节点表见 debug.run.map_state）。");
        return Done(request, $"已（无视闸门）进入格 {request.NodeId}。");
    }

    private ApiResult SkipNode(ApiRequest request)
    {
        int nodeId = Session.DebugCompleteCurrentNode();
        return Done(request, $"已把格 {nodeId} 标成已完成并推进层内遭遇计数。");
    }

    /// <summary>放弃当前内容回到可选地图（调试捷径：模块级验证不必先打完一场）。</summary>
    private ApiResult BackToMap(ApiRequest request)
    {
        Scene.ApiBackToMap();
        return Done(request, "已回到可选地图（当前内容被放弃，未结算）。");
    }

    // ── 数值 / 背包夹具 ─────────────────────────────────────────

    private ApiResult SetTimePoints(ApiRequest request)
    {
        if (!Session.DebugSetTimePoints(request.Value, out string error)) return Fail(request, "SET_TIME_REJECTED", error);
        return Done(request, $"时间点已设为 {request.Value}（第 {Session.CurrentDay} 天，剩余 {RunTimePoints.Format(Session.RemainingToday)}）。");
    }

    private ApiResult AddTimePoints(ApiRequest request)
    {
        if (!Session.TryAddTimePoints(request.Amount, out string error)) return Fail(request, "ADD_TIME_REJECTED", error);
        return Done(request, $"已加 {request.Amount} 时间点（剩余 {RunTimePoints.Format(Session.RemainingToday)}）。");
    }

    private ApiResult SetSlotHp(ApiRequest request)
    {
        if (!Session.DebugSetSlotHp(request.SlotIndex, request.Hp, request.MaxHp, out string error)) return Fail(request, "SET_HP_REJECTED", error);
        return Done(request, $"角色槽 {request.SlotIndex} 血量已写。");
    }

    private ApiResult SetHand(ApiRequest request)
    {
        if (!Session.DebugSetHand(request.SlotIndex, HandIndex(request.Hand), request.DefinitionId, out string error))
            return Fail(request, "SET_HAND_REJECTED", error);
        return Done(request, $"角色槽 {request.SlotIndex} 的 {request.Hand} 手位已写为「{request.DefinitionId}」。");
    }

    private ApiResult AddBagItem(ApiRequest request)
    {
        if (!TryParseCategory(request.Category, out BagCategory category))
            return Fail(request, "INVALID_CATEGORY", "分类无效：请用 material / item / equipment / food。");
        if (request.DefinitionKey <= 0) return Fail(request, "MISSING_DEFINITION", "请给出 definitionKey（材料 / 道具 / 食物 / 装备的定义主键）。");
        RunBagEntrySave entry = Session.DebugAddBagItem(category, request.DefinitionKey, request.Count);
        if (entry == null) return Fail(request, "ADD_ITEM_REJECTED", "物品没能入包（定义键或分类可能不存在）。");
        return Done(request, $"已塞入 {entry.DefinitionId} ×{entry.Count}（实例 {entry.InstanceId}）。");
    }

    private ApiResult ClearBag(ApiRequest request)
    {
        int removed = Session.DebugClearBag();
        return Done(request, $"已清空背包（移除 {removed} 条）。");
    }

    private ApiResult SetCarrySlot(ApiRequest request)
    {
        if (!Session.TrySetCarrySlot(request.Slot, request.InstanceId, out string error)) return Fail(request, "SET_CARRY_REJECTED", error);
        return Done(request, $"随身格 {request.Slot} 已放上 {request.InstanceId}。");
    }

    private ApiResult SetBagGate(ApiRequest request)
    {
        Session.DebugSetBagGate(request.Reason);
        return Done(request, string.IsNullOrEmpty(request.Reason) ? "背包闸门已强制放行。" : $"背包闸门已强制阻断：「{request.Reason}」。");
    }

    private ApiResult SetSuppress(ApiRequest request)
    {
        Session.DebugSetSuppressConfirm(request.Suppress);
        return Done(request, $"「本局游戏内不再显示放弃确认」已写为 {request.Suppress}。");
    }

    private ApiResult AbortRun(ApiRequest request)
    {
        Session.AbortRun();
        SwitchScene(MainMenuScenePath);
        return Ok(request, "已放弃本局（清档）并回主菜单。", new { scene = MainMenuScenePath });
    }

    private ApiResult Capture(ApiRequest request)
    {
        string path = Scene.CaptureApiScreenshot(request.Name, request.CaptureMode);
        return path != null ? Ok(request, "截图已保存。", new { path }) : Fail(request, "CAPTURE_FAILED", "截图失败（headless / 无窗口）。");
    }

    private static int HandIndex(string hand) => string.Equals(hand, "right", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    private static bool TryParseCategory(string text, out BagCategory category)
    {
        category = BagCategory.Item;
        switch ((text ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "item":
            case "道具": category = BagCategory.Item; return true;
            case "material":
            case "材料": category = BagCategory.Material; return true;
            case "equipment":
            case "装备": category = BagCategory.Equipment; return true;
            case "food":
            case "食物": category = BagCategory.Food; return true;
            default: return false;
        }
    }
}
