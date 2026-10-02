// Scripts/Api/PlayerApiBattle.cs
// 玩家 AI API · 战斗域（通道 `Player`）：把 `battle.*` 映射到 `BattlefieldSession` 的**正式规则入口**。
// 权限边界（沿用 2026-09 口径）：不重置、不固定随机数、不生成单位 / 道具、不改 HP、不加卡、不指定意图、不直接推进怪物。

using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator.Battlefield;

/// <summary>战斗域 · 玩家通道路由（`battle.*`）。</summary>
public sealed class PlayerApiBattle : IApiDomain
{
    /// <summary>域名（`ApiService` 按此名替换旧句柄）。</summary>
    public const string Domain = "battle";

    /// <summary>只读指令（不吃「先切角色」的越权闸门）。</summary>
    private static readonly HashSet<string> ReadRequests = new(StringComparer.Ordinal)
    {
        "battle.state", "battle.hand", "battle.inventory", "battle.unit", "battle.board",
        "battle.inspect_cell", "battle.legal_actions", "battle.legal_move", "battle.preview_move",
        "battle.legal_card", "battle.events", "battle.wait_idle", "battle.capture",
    };

    /// <summary>指令元数据（静态表：纯 .NET 单测与 `api.catalog` 直接读，不 new 场景）。</summary>
    public static readonly ApiCommandInfo[] Table =
    {
        new("battle.state", ApiLane.Player, "完整战斗快照（detail=full 含全部单位 / 手牌 / 地面物件）。", true, "detail=full|summary"),
        new("battle.hand", ApiLane.Player, "读取指定角色（缺省当前角色）的手牌。", true, "unitId"),
        new("battle.inventory", ApiLane.Player, "读取角色的左右手与随身 3 格。", true, "unitId"),
        new("battle.unit", ApiLane.Player, "读取单个单位（含怪物意图展示）。", true, "unitId"),
        new("battle.board", ApiLane.Player, "读取以某格为中心的区域（半径 ≤ 8）。", true, "q,r,radius"),
        new("battle.inspect_cell", ApiLane.Player, "读取单格（物件 / 触发器）。", true, "q,r"),
        new("battle.legal_actions", ApiLane.Player, "当前角色可行操作：可达路径、每张手牌合法性、当前格物件。", true),
        new("battle.legal_move", ApiLane.Player, "可达终点与步数（不枚举整图路径时用）。", true),
        new("battle.preview_move", ApiLane.Player, "按终点预演：是否可走、原因、路径。", true, "q,r"),
        new("battle.legal_card", ApiLane.Player, "指定卡牌的合法性、实际费用与可选目标格。", true, "cardId"),
        new("battle.events", ApiLane.Player, "已发生的事件（消息 / 入格 / 攻击 / 胜负），支持增量拉取。", true, "afterEventId,limit"),
        new("battle.wait_idle", ApiLane.Player, "怪物回合与表现队列是否结束（轮询到 idle=true 再继续）。", true),
        new("battle.capture", ApiLane.Player, "保存当前视口截图到 Tests/ApiCaptures/。", true, "name,captureMode=compressed"),
        new("battle.select_unit", ApiLane.Player, "切换当前操作角色（玩家在界面上的选人）。", false, "unitId"),
        new("battle.move", ApiLane.Player, "当前角色按完整合法路径移动。", false, "path:[{q,r}]"),
        new("battle.play_card", ApiLane.Player, "使用手牌并可指定目标格。", false, "cardId,hasTarget,q,r"),
        new("battle.end_turn", ApiLane.Player, "结束玩家回合（正常触发怪物回合与表现队列）。", false),
        new("battle.pick_item", ApiLane.Player, "把当前格道具拾取到随身栏。", false, "instanceId,slot"),
        new("battle.use_item", ApiLane.Player, "使用随身道具或当前格道具。", false, "slot 或 fromCurrentCell+instanceId；可选 q,r"),
        new("battle.throw_item", ApiLane.Player, "向目标格使用目标型道具（必须有目标）。", false, "同 battle.use_item + q,r"),
        new("battle.equip", ApiLane.Player, "把当前格装备装到左右手。", false, "instanceId,hand=left|right"),
        new("battle.drop_equipment", ApiLane.Player, "把手位装备放到当前格。", false, "hand=left|right"),
    };

    private readonly ApiBattleContext context;
    private readonly ApiBattleSnapshot snapshot;
    private readonly Dictionary<string, Func<ApiRequest, ApiResult>> bindings;
    private long stateVersion;

    public PlayerApiBattle(ApiBattleContext context)
    {
        this.context = context ?? new ApiBattleContext();
        snapshot = this.context.Session == null ? null : new ApiBattleSnapshot(this.context.Session);
        bindings = new Dictionary<string, Func<ApiRequest, ApiResult>>(StringComparer.Ordinal)
        {
            ["battle.state"] = request => Read("battle.state", "状态读取成功。",
                string.Equals(request.Detail, "full", StringComparison.OrdinalIgnoreCase) ? snapshot.State() : snapshot.Summary()),
            ["battle.hand"] = request => Read("battle.hand", "手牌读取成功。", snapshot.Hand(request.UnitId > 0 ? request.UnitId : Session.SelectedId)),
            ["battle.inventory"] = request => Read("battle.inventory", "物品栏读取成功。", snapshot.Inventory(request.UnitId > 0 ? request.UnitId : Session.SelectedId)),
            ["battle.unit"] = request => Read("battle.unit", "单位读取成功。", snapshot.Unit(request.UnitId)),
            ["battle.board"] = request => Read("battle.board", "战场区域读取成功。", snapshot.Board(new AxialHex(request.Q, request.R), request.Radius)),
            ["battle.inspect_cell"] = request => Read("battle.inspect_cell", "格点读取成功。", snapshot.Board(new AxialHex(request.Q, request.R), 0)),
            ["battle.legal_actions"] = request => Read("battle.legal_actions", "合法操作读取成功。", snapshot.LegalActions()),
            ["battle.legal_move"] = request => Read("battle.legal_move", "移动摘要读取成功。", snapshot.LegalMove()),
            ["battle.preview_move"] = request => Read("battle.preview_move", "移动预览读取成功。", snapshot.PreviewMove(new AxialHex(request.Q, request.R))),
            ["battle.legal_card"] = request => Read("battle.legal_card", "卡牌合法性读取成功。", snapshot.LegalCard(request.CardId)),
            ["battle.events"] = request => Read("battle.events", "事件读取成功。", ApiService.Instance?.Journal?.Read(request.AfterEventId, request.Limit)),
            ["battle.wait_idle"] = request => Read("battle.wait_idle", "空闲状态读取成功。",
                new { idle = context.IsIdle?.Invoke() ?? Session.Phase != BattlefieldSession.BattlePhase.Monsters }),
            ["battle.capture"] = Capture,
            ["battle.select_unit"] = SelectUnit,
            ["battle.move"] = Move,
            ["battle.play_card"] = PlayCard,
            ["battle.end_turn"] = EndTurn,
            ["battle.pick_item"] = request => Result(request, Session.TryPickItemFromCurrentCell(request.InstanceId, request.Slot, out string error), error),
            ["battle.use_item"] = UseItem,
            ["battle.throw_item"] = ThrowItem,
            ["battle.equip"] = request => Result(request, Session.TryEquipFromCurrentCell(request.InstanceId, ParseHand(request.Hand), out string error), error),
            ["battle.drop_equipment"] = request => Result(request, Session.TryDropEquippedWeaponOnCurrentCell(ParseHand(request.Hand), out string error), error),
        };
    }

    public string DomainName => Domain;
    public IReadOnlyList<ApiCommandInfo> Catalog => Table;
    public IReadOnlyList<string> BoundTypes => bindings.Keys.ToArray();

    public ApiResult Execute(ApiRequest request)
    {
        if (request?.Type == null || !bindings.TryGetValue(request.Type, out var handler)) return null;
        if (Session == null) return Fail(request.Type, "NO_BATTLE", "当前没有战斗（本局不在战斗中，或战场尚未建好）。");
        // 越权闸门：只有 battle.select_unit 能带 unitId 改当前角色，其余写指令一律用当前角色（与玩家界面同口径）。
        if (!ReadRequests.Contains(request.Type) && request.Type != "battle.select_unit" && request.UnitId > 0)
            return Fail(request.Type, "PLAYER_PERMISSION", "请先用 battle.select_unit 切换角色，其他指令不接受 unitId。");
        return handler(request);
    }

    private BattlefieldSession Session => context.Session;

    private ApiResult Read(string type, string message, object data)
    {
        stateVersion++;
        ApiResult result = ApiResult.Success(type, ApiLane.Player, message, data);
        result.StateVersion = stateVersion;
        return result;
    }

    /// <summary>写指令的统一回包：`responseMode` 决定带多少快照（默认摘要）。</summary>
    private ApiResult Write(ApiRequest request, string message)
    {
        stateVersion++;
        object data = string.Equals(request.ResponseMode, "full", StringComparison.OrdinalIgnoreCase) ? snapshot.State()
            : string.Equals(request.ResponseMode, "none", StringComparison.OrdinalIgnoreCase) ? null
            : snapshot.Summary();
        ApiResult result = ApiResult.Success(request.Type, ApiLane.Player, message, data);
        result.StateVersion = stateVersion;
        return result;
    }

    /// <summary>写指令的统一回包：成功 / 失败都带当刻状态与规则给的原因。</summary>
    private ApiResult Result(ApiRequest request, bool ok, string error) =>
        ok ? Write(request, "指令成功。") : Fail(request.Type, "COMMAND_REJECTED", error, snapshot.Summary());

    private static ApiResult Fail(string type, string code, string message, object data = null) =>
        ApiResult.Fail(type, ApiLane.Player, code, message, data);

    private ApiResult Capture(ApiRequest request)
    {
        string path = context.Capture?.Invoke(request.Name, request.CaptureMode);
        return path != null
            ? Read("battle.capture", "截图已保存。", new { path })
            : Fail("battle.capture", "CAPTURE_FAILED", "截图失败。");
    }

    private ApiResult SelectUnit(ApiRequest request) =>
        Session.Select(request.UnitId) ? Write(request, "已切换角色。") : Fail(request.Type, "INVALID_UNIT", "不是可操作的存活角色。");

    private ApiResult Move(ApiRequest request)
    {
        var path = request.Path?.Select(x => new AxialHex(x.Q, x.R)).ToArray() ?? Array.Empty<AxialHex>();
        return Result(request, Session.Movement.TryMovePath(Session.SelectedId, path, out string error), error);
    }

    private ApiResult PlayCard(ApiRequest request) =>
        Result(request, Session.TryCastCard(request.CardId, request.HasTarget ? new AxialHex(request.Q, request.R) : Session.Selected.Coord, out string error), error);

    private ApiResult EndTurn(ApiRequest request)
    {
        if (Session.Phase != BattlefieldSession.BattlePhase.Player) return Fail(request.Type, "INVALID_PHASE", "当前不是玩家回合。");
        Session.EndCurrentTurn();
        context.RunMonsterQueue?.Invoke();
        return Write(request, "已结束回合。");
    }

    private ApiResult UseItem(ApiRequest request)
    {
        AxialHex? target = request.HasTarget ? new AxialHex(request.Q, request.R) : null;
        bool ok = request.FromCurrentCell
            ? Session.TryUseItemFromCurrentCell(request.InstanceId, target, out string error)
            : Session.TryUseItemAt(request.Slot, target, out error);
        return Result(request, ok, error);
    }

    private ApiResult ThrowItem(ApiRequest request)
    {
        if (!request.HasTarget) return Fail(request.Type, "INVALID_TARGET", "投掷需要 q 与 r。");
        return UseItem(request);
    }

    private static BattlefieldSession.HandSlot ParseHand(string hand) =>
        string.Equals(hand, "right", StringComparison.OrdinalIgnoreCase) ? BattlefieldSession.HandSlot.Right : BattlefieldSession.HandSlot.Left;
}
