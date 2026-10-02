// Scripts/Api/DebugApiBattle.cs
// **调试 API** · 战斗域（通道 `Debug`）：把 2026-09 起 `HexBattleDebugPanel` 的指令 1:1 搬成 HTTP 指令。
// 这些能力**超出玩家范围**（生成 / 删除单位与道具、改 HP / 护盾 / 状态、加卡、重置回合、选关），
// 只给自动化测试与排错用；与玩家通道（`PlayerApiBattle`）在文件、类、类型名前缀三处都分开。

using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator.Battlefield;

/// <summary>战斗域 · 调试通道路由（`debug.battle.*` / `debug.capture`）。</summary>
public sealed class DebugApiBattle : IApiDomain
{
    public const string Domain = "debug.battle";

    /// <summary>指令 + 参数完整清单（对应调试面板的 6 个分类）。</summary>
    public static readonly ApiCommandInfo[] Table =
    {
        new("debug.battle.jump_level", ApiLane.Debug, "选关 / 跳关：直接进入指定关卡（绕过地图、池档位与时间点）。", false, "levelId"),
        new("debug.battle.jump_event", ApiLane.Debug, "跳事件：直接进入指定事件（绕过地图与节点类型）。", false, "eventId"),
        new("debug.battle.draw_card", ApiLane.Debug, "抽牌。", false, "unitId,count"),
        new("debug.battle.add_card", ApiLane.Debug, "把指定卡牌塞进指定牌堆（0 手牌 / 1 抽牌堆 / 2 弃牌堆 / 3 消耗堆）。", false, "unitId,cardId,pile,count"),
        new("debug.battle.clear_hand", ApiLane.Debug, "把某角色手牌全部弃掉。", false, "unitId"),
        new("debug.battle.add_energy", ApiLane.Debug, "加减能量。", false, "unitId,amount"),
        new("debug.battle.add_moves", ApiLane.Debug, "加减本回合移动次数。", false, "unitId,amount"),
        new("debug.battle.reset_round", ApiLane.Debug, "重置本回合能量与移动次数（调试辅助回合）。", false),
        new("debug.battle.damage", ApiLane.Debug, "直接造成伤害（触发死亡与胜负判定）。", false, "unitId,amount"),
        new("debug.battle.heal", ApiLane.Debug, "直接治疗。", false, "unitId,amount"),
        new("debug.battle.set_hp", ApiLane.Debug, "设置生命值（夹在 0..上限）。", false, "unitId,hp"),
        new("debug.battle.add_shield", ApiLane.Debug, "加减护盾。", false, "unitId,amount"),
        new("debug.battle.add_state", ApiLane.Debug, "添加状态（stateType 为 StateType 枚举值）。", false, "unitId,stateType,stacks"),
        new("debug.battle.remove_state", ApiLane.Debug, "按状态索引删层。", false, "unitId,index,stacks"),
        new("debug.battle.spawn_item", ApiLane.Debug, "在指定格生成道具（isEquipment=true 生成装备）。", false, "q,r,definitionId,isEquipment"),
        new("debug.battle.spawn_item_to_slot", ApiLane.Debug, "往当前角色随身槽塞道具。", false, "slot,definitionId"),
        new("debug.battle.clear_slot", ApiLane.Debug, "清空当前角色随身槽。", false, "slot"),
        new("debug.battle.equip_to_hand", ApiLane.Debug, "直接往手位塞装备（不校验部位组合）。", false, "definitionId,hand=left|right"),
        new("debug.battle.place_trap", ApiLane.Debug, "在指定格放陷阱（每次进入触发）。", false, "q,r,definitionId"),
        new("debug.battle.spawn_enemy", ApiLane.Debug, "在指定格生成怪物。", false, "q,r,unitId"),
        new("debug.battle.clear_enemies", ApiLane.Debug, "清空全部敌人（= 直接判胜）。", false),
        new("debug.battle.end_turn", ApiLane.Debug, "强制结束当前回合（不走玩家回合判定）。", false),
        new("debug.capture", ApiLane.Debug, "保存当前视口截图（战斗与任意场景通用；captureMode=compressed 出 1280×720）。", true, "name,captureMode"),
    };

    private readonly ApiBattleContext context;
    private readonly Dictionary<string, Func<ApiRequest, ApiResult>> bindings;

    public DebugApiBattle(ApiBattleContext context)
    {
        this.context = context ?? new ApiBattleContext();
        bindings = new Dictionary<string, Func<ApiRequest, ApiResult>>(StringComparer.Ordinal)
        {
            ["debug.battle.jump_level"] = request => Jump(request, context.JumpLevel, request.LevelId, "关卡"),
            ["debug.battle.jump_event"] = request => Jump(request, context.JumpEvent, request.EventId, "事件"),
            ["debug.battle.draw_card"] = Void(request => Session.DebugDraw(request.UnitId, Math.Max(1, request.Count))),
            ["debug.battle.add_card"] = Wrap(request => (Session.DebugAddCard(request.UnitId, request.CardId, request.Pile, Math.Max(1, request.Count), out string error), error)),
            ["debug.battle.clear_hand"] = Void(request => Session.DebugClearHand(request.UnitId)),
            ["debug.battle.add_energy"] = Void(request => Session.DebugAddEnergy(request.UnitId, request.Amount)),
            ["debug.battle.add_moves"] = Void(request => Session.DebugAddMoves(request.UnitId, request.Amount)),
            ["debug.battle.reset_round"] = Void(request => Session.DebugResetRound()),
            ["debug.battle.damage"] = Wrap(request => (Session.DebugDamage(request.UnitId, request.Amount, out string error), error)),
            ["debug.battle.heal"] = Wrap(request => (Session.DebugHeal(request.UnitId, request.Amount, out string error), error)),
            ["debug.battle.set_hp"] = Wrap(request => (Session.DebugSetHp(request.UnitId, request.Hp, out string error), error)),
            ["debug.battle.add_shield"] = Wrap(request => (Session.DebugAddShield(request.UnitId, request.Amount, out string error), error)),
            ["debug.battle.add_state"] = Wrap(request => (Session.DebugAddState(request.UnitId, request.StateType, request.Stacks, out string error), error)),
            ["debug.battle.remove_state"] = Wrap(request => (Session.DebugRemoveState(request.UnitId, request.Index, request.Stacks, out string error), error)),
            ["debug.battle.spawn_item"] = Wrap(request => (Session.DebugSpawnItemAtCell(request.Q, request.R, request.DefinitionId, request.IsEquipment, out string error), error)),
            ["debug.battle.spawn_item_to_slot"] = Wrap(request => (Session.DebugSpawnItemToSlot(request.Slot, request.DefinitionId, out string error), error)),
            ["debug.battle.clear_slot"] = Wrap(request => (Session.DebugClearSlot(request.Slot, out string error), error)),
            ["debug.battle.equip_to_hand"] = Wrap(request => (Session.DebugEquipToHand(request.DefinitionId, HandIndex(request.Hand), out string error), error)),
            ["debug.battle.place_trap"] = Wrap(request => (Session.DebugPlaceTrap(request.Q, request.R, request.DefinitionId, out string error), error)),
            ["debug.battle.spawn_enemy"] = Wrap(request => (Session.DebugSpawnEnemy(request.Q, request.R, request.UnitId, out string error), error)),
            ["debug.battle.clear_enemies"] = Void(request => Session.DebugClearEnemies()),
            ["debug.battle.end_turn"] = Void(request => { Session.DebugEndTurn(); context.RunMonsterQueue?.Invoke(); }),
            ["debug.capture"] = Capture,
        };
    }

    public string DomainName => Domain;
    public IReadOnlyList<ApiCommandInfo> Catalog => Table;
    public IReadOnlyList<string> BoundTypes => bindings.Keys.ToArray();

    public ApiResult Execute(ApiRequest request)
    {
        if (request?.Type == null || !bindings.TryGetValue(request.Type, out var handler)) return null;
        if (Session == null && request.Type != "debug.capture")
            return ApiResult.Fail(request.Type, ApiLane.Debug, "NO_BATTLE", "当前没有战斗，该调试指令不可用。");
        return handler(request);
    }

    private BattlefieldSession Session => context.Session;

    private Func<ApiRequest, ApiResult> Wrap(Func<ApiRequest, (bool Ok, string Error)> action) =>
        request =>
        {
            (bool ok, string error) = action(request);
            return Finish(request, ok, error);
        };

    private Func<ApiRequest, ApiResult> Void(Action<ApiRequest> action) =>
        request => { action(request); return Finish(request, true, string.Empty); };

    private ApiResult Finish(ApiRequest request, bool ok, string error)
    {
        if (!ok) return ApiResult.Fail(request.Type, ApiLane.Debug, "DEBUG_REJECTED", error);
        return ApiResult.Success(request.Type, ApiLane.Debug, "调试指令成功。",
            string.Equals(request.ResponseMode, "none", StringComparison.OrdinalIgnoreCase) ? null : Session?.DebugSummary());
    }

    private ApiResult Jump(ApiRequest request, Action<string> jump, string id, string what)
    {
        if (string.IsNullOrWhiteSpace(id)) return ApiResult.Fail(request.Type, ApiLane.Debug, "MISSING_ID", $"请给出 {what} Id。");
        if (jump == null) return ApiResult.Fail(request.Type, ApiLane.Debug, "NO_HOST", "当前宿主不支持跳转。");
        jump(id);
        return ApiResult.Success(request.Type, ApiLane.Debug, $"已请求跳转{what}：{id}。");
    }

    private ApiResult Capture(ApiRequest request)
    {
        string path = context.Capture?.Invoke(request.Name, request.CaptureMode);
        return path != null
            ? ApiResult.Success("debug.capture", ApiLane.Debug, "截图已保存。", new { path })
            : ApiResult.Fail("debug.capture", ApiLane.Debug, "CAPTURE_FAILED", "截图失败（无战斗宿主或视口不可读）。");
    }

    private static int HandIndex(string hand) => string.Equals(hand, "right", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
}
