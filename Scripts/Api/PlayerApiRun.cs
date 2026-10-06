// Scripts/Api/PlayerApiRun.cs
// 玩家 AI API · 运行局域（通道 `Player`）：把 `run.*` 映射成「界面上真能点的那一下」。
// 覆盖：时间点（结束本天）/ 营地（夜间 UI）/ 背包（开合、页签、翻页、拖动物品）/ 地图选点 / 结算只读。
// 与调试通道（`DebugApiRun` 的 `debug.run.*`）在文件、类、类型名前缀三处都分开。

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>运行局域 · 玩家通道路由（`run.*`）。</summary>
public sealed class PlayerApiRun : IApiDomain
{
    public const string Domain = "run";

    /// <summary>指令元数据（静态表：纯 .NET 单测与 `api.catalog` 直接读，不 new 场景）。</summary>
    public static readonly ApiCommandInfo[] Table =
    {
        new("run.state", ApiLane.Player, "本局全景：天数 / 剩余时间点 / 模式 / 角色槽 / 负荷 / 界面形态。", true),
        new("run.bag.state", ApiLane.Player, "背包界面：页签 / 页码 / 负荷 / 横幅 / 每格格名与拖动载荷。", true),
        new("run.bag.open", ApiLane.Player, "打开背包界面（= 顶栏「背包」按钮）。"),
        new("run.bag.close", ApiLane.Player, "关闭背包界面。"),
        new("run.bag.toggle", ApiLane.Player, "开合背包界面（= 顶栏按钮的开关语义）。"),
        new("run.bag.tab", ApiLane.Player, "切换页签（全部 / 材料 / 道具 / 装备 / 食物）；换页签回到第 1 页。", false, "tab"),
        new("run.bag.character_tab", ApiLane.Player, "切换装备栏归属的角色页签。", false, "slotIndex"),
        new("run.bag.page", ApiLane.Player, "翻到指定页（1 基，越界自动夹取）。", false, "page"),
        new("run.bag.next_page", ApiLane.Player, "下一页。"),
        new("run.bag.prev_page", ApiLane.Player, "上一页。"),
        new("run.bag.drag", ApiLane.Player, "移动物品：把 fromCell 的格内容拖到 toCell（与鼠标拖放共用同一条落点判定）。", false, "fromCell,toCell"),
        new("run.equip.state", ApiLane.Player, "装备界面：角色 Tab / 部位格与手位的格名与拖动载荷 / 横幅 / 负荷。", true),
        new("run.equip.open", ApiLane.Player, "打开装备界面（= 顶栏「装备」按钮；与背包互斥）。"),
        new("run.equip.close", ApiLane.Player, "关闭装备界面。"),
        new("run.equip.toggle", ApiLane.Player, "开合装备界面（= 顶栏按钮的开关语义）。"),
        new("run.equip.character_tab", ApiLane.Player, "切换部位格 / 手位归属的角色页签。", false, "slotIndex"),
        new("run.equip.drag", ApiLane.Player, "换装：把 fromCell 的格内容拖到 toCell（背包 → 部位格 / 手位、部位格 → 背包、饰品互换）。", false, "fromCell,toCell"),
        new("run.end_day", ApiLane.Player, "点「结束当天」：进营地（内容进行中 / 结算面板打开时拒绝）。"),
        new("run.camp.state", ApiLane.Player, "营地（夜间 UI）：守夜模式 / 篝火饱食度 / 三个面板 / 按钮可用性 / 预览文案。", true),
        new("run.camp.toggle_food", ApiLane.Player, "点「添加食物」（开 / 关篝火食物面板）。"),
        new("run.camp.toggle_cook", ApiLane.Player, "点「烹饪」（开 / 关配方面板）。"),
        new("run.camp.toggle_watch", ApiLane.Player, "点「守夜」（开 / 关守夜面板）。"),
        new("run.camp.add_food", ApiLane.Player, "把背包食物放进本次篝火（点「休息」才真正消耗）。", false, "instanceId"),
        new("run.camp.remove_food", ApiLane.Player, "从篝火草稿取回一件食物。", false, "instanceId"),
        new("run.camp.cook", ApiLane.Player, "用已放行配方烹饪（每次休息 ≤ 2 次）。", false, "recipeId"),
        new("run.camp.set_watch", ApiLane.Player, "选择守夜模式（none / rotation / single）与单人守夜的角色。", false, "mode,watcherSlot"),
        new("run.camp.rest", ApiLane.Player, "点「休息」：应用回复 → 推进新一天 → 淡出回地图。"),
        new("run.map.state", ApiLane.Player, "地图：是否可选 / 当前位置 / 可达格（= 玩家这一回合能点到的格）。", true),
        new("run.map.toggle", ApiLane.Player, "开合世界地图（= 顶栏「地图」按钮）。"),
        new("run.map.enter_node", ApiLane.Player, "点一个**可达**格进入（不可达 / 未就绪一律拒绝）。", false, "nodeId"),
        new("run.map.enter_next", ApiLane.Player, "点第一个可达格（= 玩家点击的同一入口）。"),
        new("run.settlement.state", ApiLane.Player, "结算界面：面板 / 选牌 / 放弃确认 / 未领取物品清单与卡牌候选。", true),
        new("run.settlement.claim", ApiLane.Player, "领一件物品（按 claimKey；= 点列表里那一行）。", false, "claimKey"),
        new("run.settlement.claim_card", ApiLane.Player, "在某个卡牌份里选一张（= 点卡面）：入该槽卡组。", false, "slotIndex,cardId"),
        new("run.settlement.close_panel", ApiLane.Player, "点「关闭」（未领完 → 待领取态 + 浮窗；领完 → 回地图）。"),
        // 地点场景指令（`run.village.*`，15 条）：2026-10-06 随村庄专用场景撤除（方案甲）——
        // 不进本表；「统一关卡通道」批接上关卡场景时，按新案重新出指令（不照搬村庄版）。
    };

    private readonly ApiRunContext context;
    private readonly Dictionary<string, Func<ApiRequest, ApiResult>> bindings;

    public PlayerApiRun(ApiRunContext context)
    {
        this.context = context ?? new ApiRunContext();
        bindings = new Dictionary<string, Func<ApiRequest, ApiResult>>(StringComparer.Ordinal)
        {
            ["run.state"] = request => Ok(request, "本局状态读取成功。", ApiRunSnapshot.State(Scene)),
            ["run.bag.state"] = request => Ok(request, "背包状态读取成功。", ApiRunSnapshot.Bag(Scene)),
            ["run.bag.open"] = OpenBag,
            ["run.bag.close"] = CloseBag,
            ["run.bag.toggle"] = ToggleBag,
            ["run.bag.tab"] = Tab,
            ["run.bag.character_tab"] = CharacterTab,
            ["run.bag.page"] = GoToPage,
            ["run.bag.next_page"] = request => PageStep(request, 1),
            ["run.bag.prev_page"] = request => PageStep(request, -1),
            ["run.bag.drag"] = Drag,
            ["run.equip.state"] = request => Ok(request, "装备状态读取成功。", ApiRunSnapshot.Equip(Scene)),
            ["run.equip.open"] = OpenEquip,
            ["run.equip.close"] = CloseEquip,
            ["run.equip.toggle"] = ToggleEquipUi,
            ["run.equip.character_tab"] = EquipCharacterTab,
            ["run.equip.drag"] = EquipDrag,
            ["run.end_day"] = EndDay,
            ["run.camp.state"] = request => CampOk(request, "营地状态读取成功。"),
            ["run.camp.toggle_food"] = request => CampAction(request, camp => camp.ToggleFoodPanel(), "已开合「添加食物」面板。"),
            ["run.camp.toggle_cook"] = request => CampAction(request, camp => camp.ToggleCookPanel(), "已开合「烹饪」面板。"),
            ["run.camp.toggle_watch"] = request => CampAction(request, camp => camp.ToggleWatchPanel(), "已开合「守夜」面板。"),
            ["run.camp.add_food"] = AddFood,
            ["run.camp.remove_food"] = RemoveFood,
            ["run.camp.cook"] = Cook,
            ["run.camp.set_watch"] = SetWatch,
            ["run.camp.rest"] = Rest,
            ["run.map.state"] = request => Ok(request, "地图状态读取成功。", ApiRunSnapshot.Map(Scene)),
            ["run.map.toggle"] = ToggleMap,
            ["run.map.enter_node"] = EnterNode,
            ["run.map.enter_next"] = EnterNext,
            ["run.settlement.state"] = request => Ok(request, "结算状态读取成功。", ApiRunSnapshot.Settlement(Scene)),
            ["run.settlement.claim"] = ClaimSettlementItem,
            ["run.settlement.claim_card"] = ClaimSettlementCard,
            ["run.settlement.close_panel"] = CloseSettlementPanel,
        };
    }

    public string DomainName => Domain;
    public IReadOnlyList<ApiCommandInfo> Catalog => Table;
    public IReadOnlyList<string> BoundTypes => bindings.Keys.ToArray();

    public ApiResult Execute(ApiRequest request)
    {
        if (request?.Type == null || !bindings.TryGetValue(request.Type, out var handler)) return null;
        if (!context.HasScene) return ApiResult.Fail(request.Type, ApiLane.Player, "NO_RUN_SCENE", "当前不是运行局场景（请先进本局）。");
        return handler(request);
    }

    // ── 基础设施 ───────────────────────────────────────────────

    private RunFlowScene Scene => context.Scene;
    private BagUi Bag => context.Scene?.Bag;
    private static RunSession Session => RunSession.Instance;

    private static ApiResult Ok(ApiRequest request, string message, object data) =>
        ApiResult.Success(request.Type, ApiLane.Player, message, data);

    private static ApiResult Fail(ApiRequest request, string code, string message) =>
        ApiResult.Fail(request.Type, ApiLane.Player, code, message);

    private ApiResult RequireOpenBag(ApiRequest request, out BagUi bag)
    {
        bag = Bag;
        if (bag == null) return Fail(request, "NO_BAG_UI", "本场景没有背包界面。");
        return bag.IsOpen ? null : Fail(request, "NO_BAG", "背包界面未打开（先 run.bag.open）。");
    }

    // ── 背包 ───────────────────────────────────────────────────

    private ApiResult OpenBag(ApiRequest request)
    {
        if (Bag == null) return Fail(request, "NO_BAG_UI", "本场景没有背包界面。");
        if (!Bag.IsOpen && !Scene.ToggleBagUi())
            return Fail(request, "BAG_BLOCKED", "背包界面打不开（结算面板 / 放弃确认打开或已在营地期间不开）。");
        return Ok(request, "背包界面已打开。", ApiRunSnapshot.Bag(Scene));
    }

    private ApiResult CloseBag(ApiRequest request)
    {
        if (Bag?.IsOpen != true) return Ok(request, "背包界面本来就是关着的。", ApiRunSnapshot.Bag(Scene));
        Scene.ToggleBagUi();
        return Ok(request, "背包界面已关闭。", ApiRunSnapshot.Bag(Scene));
    }

    private ApiResult ToggleBag(ApiRequest request)
    {
        if (Bag == null) return Fail(request, "NO_BAG_UI", "本场景没有背包界面。");
        bool open = Scene.ToggleBagUi();
        return Ok(request, open ? "背包界面已打开。" : "背包界面已关闭。", ApiRunSnapshot.Bag(Scene));
    }

    private ApiResult Tab(ApiRequest request)
    {
        ApiResult failure = RequireOpenBag(request, out BagUi bag);
        if (failure != null) return failure;
        if (!bag.SelectTab(request.Tab))
            return Fail(request, "INVALID_TAB", "页签无效：请用 全部 / 材料 / 道具 / 装备 / 食物（或 all / material / item / equipment / food）。");
        return Ok(request, $"已切到页签「{bag.ActiveTabLabel}」。", ApiRunSnapshot.Bag(Scene));
    }

    private ApiResult CharacterTab(ApiRequest request)
    {
        ApiResult failure = RequireOpenBag(request, out BagUi bag);
        if (failure != null) return failure;
        if (!bag.SelectCharacterTab(request.SlotIndex))
            return Fail(request, "INVALID_SLOT", $"角色槽 {request.SlotIndex} 不存在（当前角色数见 run.state）。");
        return Ok(request, $"装备栏已切到角色槽 {request.SlotIndex}。", ApiRunSnapshot.Bag(Scene));
    }

    private ApiResult GoToPage(ApiRequest request)
    {
        ApiResult failure = RequireOpenBag(request, out BagUi bag);
        if (failure != null) return failure;
        if (request.Page <= 0) return Fail(request, "INVALID_PAGE", "页码从 1 起（越界会自动夹取）。");
        bag.GoToPage(request.Page - 1);
        return Ok(request, "已翻页。", ApiRunSnapshot.Bag(Scene));
    }

    private ApiResult PageStep(ApiRequest request, int delta)
    {
        ApiResult failure = RequireOpenBag(request, out BagUi bag);
        if (failure != null) return failure;
        if (delta > 0) bag.NextPage(); else bag.PreviousPage();
        return Ok(request, "已翻页。", ApiRunSnapshot.Bag(Scene));
    }

    /// <summary>移动物品：与鼠标拖放共用 `BagUi.ApplyDrop`，被拒时把提示行原文当原因返回。</summary>
    private ApiResult Drag(ApiRequest request)
    {
        ApiResult failure = RequireOpenBag(request, out BagUi bag);
        if (failure != null) return failure;
        if (string.IsNullOrWhiteSpace(request.FromCell) || string.IsNullOrWhiteSpace(request.ToCell))
            return Fail(request, "MISSING_CELL", "请给出 fromCell 与 toCell（格名见 run.bag.state 的 bagCells / carryCells / handCells）。");
        if (bag.PayloadOfCell(request.FromCell).Length == 0)
            return Fail(request, "EMPTY_SOURCE", $"格 {request.FromCell} 没有可拿起的物品。");
        if (bag.DragCell(request.FromCell, request.ToCell))
            return Ok(request, $"已移动：{request.FromCell} → {request.ToCell}（{bag.HintText}）。", ApiRunSnapshot.Bag(Scene));
        return ApiResult.Fail(request.Type, ApiLane.Player, "DROP_REJECTED",
            bag.HintText.Length > 0 ? bag.HintText : "落点被拒（原因见 run.bag.state 的 hint / banner）。", ApiRunSnapshot.Bag(Scene));
    }

    // ── 装备界面（P0-18 界面半） ────────────────────────────────

    /// <summary>装备界面实例（场景没挂 = null，调用方按 NO_EQUIP_UI 拒绝）。</summary>
    private EquipmentUi Equip => context.Scene?.Equipment;

    /// <summary>装备界面的共用闸门：没界面 / 没打开各给一条稳定错误码。</summary>
    private ApiResult RequireOpenEquip(ApiRequest request, out EquipmentUi ui)
    {
        ui = Equip;
        if (ui == null) return Fail(request, "NO_EQUIP_UI", "本场景没有装备界面。");
        return ui.IsOpen ? null : Fail(request, "NO_EQUIP", "装备界面未打开（先 run.equip.open）。");
    }

    private ApiResult OpenEquip(ApiRequest request)
    {
        if (Equip == null) return Fail(request, "NO_EQUIP_UI", "本场景没有装备界面。");
        if (!Equip.IsOpen && !Scene.ToggleEquipUi())
            return Fail(request, "EQUIP_BLOCKED", "装备界面打不开（结算面板 / 放弃确认打开或已在营地期间不开）。");
        return Ok(request, "装备界面已打开。", ApiRunSnapshot.Equip(Scene));
    }

    private ApiResult CloseEquip(ApiRequest request)
    {
        if (Equip?.IsOpen != true) return Ok(request, "装备界面本来就是关着的。", ApiRunSnapshot.Equip(Scene));
        Scene.ToggleEquipUi();
        return Ok(request, "装备界面已关闭。", ApiRunSnapshot.Equip(Scene));
    }

    private ApiResult ToggleEquipUi(ApiRequest request)
    {
        if (Equip == null) return Fail(request, "NO_EQUIP_UI", "本场景没有装备界面。");
        bool open = Scene.ToggleEquipUi();
        return Ok(request, open ? "装备界面已打开。" : "装备界面已关闭。", ApiRunSnapshot.Equip(Scene));
    }

    private ApiResult EquipCharacterTab(ApiRequest request)
    {
        ApiResult failure = RequireOpenEquip(request, out EquipmentUi ui);
        if (failure != null) return failure;
        if (!ui.SelectCharacterTab(request.SlotIndex))
            return Fail(request, "INVALID_SLOT", $"角色槽 {request.SlotIndex} 不存在（当前角色数见 run.state）。");
        return Ok(request, $"装备界面已切到角色槽 {request.SlotIndex}。", ApiRunSnapshot.Equip(Scene));
    }

    /// <summary>换装：与鼠标拖放共用 `EquipmentUi.ApplyDrop`（规则拒绝时把提示行原文当原因返回）。</summary>
    private ApiResult EquipDrag(ApiRequest request)
    {
        ApiResult failure = RequireOpenEquip(request, out EquipmentUi ui);
        if (failure != null) return failure;
        if (string.IsNullOrWhiteSpace(request.FromCell) || string.IsNullOrWhiteSpace(request.ToCell))
            return Fail(request, "MISSING_CELL", "请给出 fromCell 与 toCell（格名见 run.equip.state 的 bagCells / bodyCells / handCells）。");
        if (ui.PayloadOfCell(request.FromCell).Length == 0)
            return Fail(request, "EMPTY_SOURCE", $"格 {request.FromCell} 没有可拿起的装备。");
        if (ui.DragCell(request.FromCell, request.ToCell))
            return Ok(request, $"已移动：{request.FromCell} → {request.ToCell}（{ui.HintText}）。", ApiRunSnapshot.Equip(Scene));
        return ApiResult.Fail(request.Type, ApiLane.Player, "DROP_REJECTED",
            ui.HintText.Length > 0 ? ui.HintText : "落点被拒（原因见 run.equip.state 的 hint / banner）。", ApiRunSnapshot.Equip(Scene));
    }

    // ── 时间点 / 营地（夜间 UI） ────────────────────────────────
    /// <summary>点「结束当天」：进营地（与顶栏按钮同一入口，内容进行中 / 结算面板打开时拒绝）。</summary>
    private ApiResult EndDay(ApiRequest request)
    {
        if (Scene.IsCampOpen) return Ok(request, "已经在营地里。", ApiRunSnapshot.Camp(Scene));
        if (!Scene.TryEndDay()) return Fail(request, "END_DAY_BLOCKED", "现在不能结束当天（内容进行中、结算面板打开或地图不可选）。");
        return Ok(request, "已进入营地（结束当天）。", ApiRunSnapshot.Camp(Scene));
    }

    private ApiResult CampOk(ApiRequest request, string message) => Ok(request, message, ApiRunSnapshot.Camp(Scene));

    private ApiResult CampAction(ApiRequest request, Action<CampScene> action, string message)
    {
        if (!Scene.IsCampOpen) return Fail(request, "NO_CAMP", "当前不在营地（夜间 UI 未打开；先 run.end_day）。");
        action(Scene.Camp);
        return CampOk(request, message);
    }

    private ApiResult AddFood(ApiRequest request)
    {
        if (!Scene.IsCampOpen) return Fail(request, "NO_CAMP", "当前不在营地（夜间 UI 未打开；先 run.end_day）。");
        if (string.IsNullOrWhiteSpace(request.InstanceId)) return Fail(request, "MISSING_INSTANCE", "请给出食物实例 instanceId（清单见 run.bag.state）。");
        if (!Scene.Camp.TryAddFoodToCampFire(request.InstanceId, out string error)) return Fail(request, "ADD_FOOD_REJECTED", error);
        return CampOk(request, $"已把食物放进篝火：{request.InstanceId}。");
    }

    private ApiResult RemoveFood(ApiRequest request)
    {
        if (!Scene.IsCampOpen) return Fail(request, "NO_CAMP", "当前不在营地（夜间 UI 未打开；先 run.end_day）。");
        if (!Scene.Camp.TryRemoveFoodFromCampFire(request.InstanceId, out string error)) return Fail(request, "REMOVE_FOOD_REJECTED", error);
        return CampOk(request, $"已从篝火取回：{request.InstanceId}。");
    }

    private ApiResult Cook(ApiRequest request)
    {
        if (!Scene.IsCampOpen) return Fail(request, "NO_CAMP", "当前不在营地（夜间 UI 未打开；先 run.end_day）。");
        if (request.RecipeId <= 0) return Fail(request, "MISSING_RECIPE", "请给出配方 Id（可用配方见 DataBase/Item/FoodRecipe.csv）。");
        if (!Scene.Camp.TryCookRecipe(request.RecipeId, out string error)) return Fail(request, "COOK_REJECTED", error);
        return CampOk(request, $"已烹饪配方 {request.RecipeId}。");
    }

    /// <summary>守夜模式 + 单人守夜的角色（与点勾选框 / 选下拉框同一条通路）。</summary>
    private ApiResult SetWatch(ApiRequest request)
    {
        if (!Scene.IsCampOpen) return Fail(request, "NO_CAMP", "当前不在营地（夜间 UI 未打开；先 run.end_day）。");
        CampScene camp = Scene.Camp;
        if (!TryParseWatch(request.Mode, out RunWatchMode mode))
            return Fail(request, "INVALID_WATCH_MODE", "守夜模式无效：请用 none / rotation / single。");
        if (!camp.SetWatchMode(mode)) return Fail(request, "CAMP_LOCKED", "营地正在结算，不能再改守夜设置。");
        if (mode == RunWatchMode.Single && request.WatcherSlot > 0 && !camp.SetWatcherSlot(request.WatcherSlot))
            return Fail(request, "INVALID_WATCHER", $"守夜角色槽 {request.WatcherSlot} 不存在。");
        return CampOk(request, $"守夜模式已设为 {mode}。");
    }

    /// <summary>点「休息」：走完整结算（应用回复 → 推进新一天 → 淡出回地图，淡出由营地自己收尾）。</summary>
    private ApiResult Rest(ApiRequest request)
    {
        if (!Scene.IsCampOpen) return Fail(request, "NO_CAMP", "当前不在营地（夜间 UI 未打开；先 run.end_day）。");
        if (!Scene.Camp.RequestRest()) return Fail(request, "CAMP_LOCKED", "营地正在结算中，请稍后再试。");
        return CampOk(request, "已开始休息结算（回复 → 新一天 → 淡出回地图）。");
    }

    private static bool TryParseWatch(string text, out RunWatchMode mode)
    {
        mode = RunWatchMode.None;
        switch ((text ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "none": mode = RunWatchMode.None; return true;
            case "rotation": mode = RunWatchMode.Rotation; return true;
            case "single": mode = RunWatchMode.Single; return true;
            default: return false;
        }
    }

    // ── 地图（玩家口径） ───────────────────────────────────────

    private ApiResult ToggleMap(ApiRequest request)
    {
        Scene.ToggleMapUi();
        return Ok(request, Scene.IsMapVisible ? "世界地图已打开。" : "世界地图已关闭。", ApiRunSnapshot.Map(Scene));
    }

    private ApiResult EnterNode(ApiRequest request)
    {
        if (!Scene.TryEnterReachableNode(request.NodeId))
            return Fail(request, "NODE_NOT_REACHABLE", $"格 {request.NodeId} 当前不可达（可达格见 run.map.state 的 reachableNodes）。");
        return Ok(request, $"已进入格 {request.NodeId}。", ApiRunSnapshot.Map(Scene));
    }

    private ApiResult EnterNext(ApiRequest request)
    {
        if (!Scene.TryEnterNextNode()) return Fail(request, "NO_REACHABLE_NODE", "当前没有可达格（地图不可选或没有相邻格）。");
        return Ok(request, "已进入第一个可达格。", ApiRunSnapshot.Map(Scene));
    }

    // ── 结算（玩家口径：领取 / 关闭；2026-10-05 补 P3-18） ─────────

    private ApiResult ClaimSettlementItem(ApiRequest request)
    {
        SettlementUi ui = Scene?.Settlement;
        if (ui == null) return Fail(request, "NO_SETTLEMENT_UI", "本场景没有结算界面。");
        if (string.IsNullOrWhiteSpace(request.ClaimKey))
            return Fail(request, "MISSING_CLAIM_KEY", "请给出 claimKey（未领取清单见 run.settlement.state 的 items）。");
        if (!ui.TryClaimItem(request.ClaimKey, out string error)) return Fail(request, "CLAIM_REJECTED", error);
        return Ok(request, $"已领取物品「{request.ClaimKey}」。", ApiRunSnapshot.Settlement(Scene));
    }

    private ApiResult ClaimSettlementCard(ApiRequest request)
    {
        SettlementUi ui = Scene?.Settlement;
        if (ui == null) return Fail(request, "NO_SETTLEMENT_UI", "本场景没有结算界面。");
        if (!ui.TryClaimCard(request.SlotIndex, request.CardId, out int ownerSlot, out string error))
            return Fail(request, "CLAIM_CARD_REJECTED", error);
        return Ok(request, $"已领取卡牌 {request.CardId}（入槽位 {ownerSlot}）。", ApiRunSnapshot.Settlement(Scene));
    }

    private ApiResult CloseSettlementPanel(ApiRequest request)
    {
        SettlementUi ui = Scene?.Settlement;
        if (ui == null) return Fail(request, "NO_SETTLEMENT_UI", "本场景没有结算界面。");
        if (!ui.TryClosePanel(out string error)) return Fail(request, "CLOSE_REJECTED", error);
        return Ok(request, "已关闭结算面板。", ApiRunSnapshot.Settlement(Scene));
    }

    // ── 村庄（地点场景）指令：2026-10-06 撤除（方案甲）────────────────
    // `run.village.*` 的 15 条指令连同村庄专用场景（`VillageScene` / `VillageVisit` / `VillageLayout`）
    // 一并撤除：交互案改走「统一关卡通道」。设施界面（锻铁铺 `SmithyUi` / 餐厅 `RestaurantUi`）与
    // 设施规则层保留，待统一关卡通道批以关卡内形态重新接指令（处理器按新案重写，不照搬村庄版）。

}
