using System;
using System.Linq;
using CardSimulator.Battlefield;

/// <summary>Maps player-permitted API commands to BattlefieldSession's public rule entry points.</summary>
public sealed class BattleApiRouter
{
    private readonly BattlefieldSession session;
    private readonly BattleApiSnapshot snapshot;
    private readonly BattleApiEventJournal journal;
    private readonly Action enemyTurnStarted;
    private readonly Func<bool> isIdle;
    private readonly Func<string, string, string> capture;
    private long stateVersion;

    public BattleApiRouter(BattlefieldSession session, BattleApiSnapshot snapshot, BattleApiEventJournal journal,
        Action enemyTurnStarted, Func<bool> isIdle, Func<string, string, string> capture)
    { this.session = session; this.snapshot = snapshot; this.journal = journal; this.enemyTurnStarted = enemyTurnStarted; this.isIdle = isIdle; this.capture = capture; }

    public BattleCommandResult Execute(BattleCommandRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Type)) return Reply(false, "INVALID_REQUEST", "缺少 type。");
        bool readRequest = request.Type is "battle.state" or "battle.hand" or "battle.inventory" or "battle.unit" or "battle.board" or "battle.inspect_cell" or "battle.legal_actions" or "battle.legal_move" or "battle.preview_move" or "battle.legal_card" or "battle.events" or "battle.wait_idle" or "battle.capture";
        if (!readRequest && request.Type != "battle.select_unit" && request.UnitId > 0)
            return Reply(false, "PLAYER_PERMISSION", "请先使用 battle.select_unit 切换角色，其他指令不接受 unitId。");
        bool ok; string error;
        switch (request.Type)
        {
            case "battle.state": return Reply(true, null, "状态读取成功。", string.Equals(request.Detail, "full", StringComparison.OrdinalIgnoreCase) ? snapshot.State() : snapshot.Summary());
            case "battle.hand": return Reply(true, null, "手牌读取成功。", snapshot.Hand(request.UnitId > 0 ? request.UnitId : session.SelectedId));
            case "battle.inventory": return Reply(true, null, "物品栏读取成功。", snapshot.Inventory(request.UnitId > 0 ? request.UnitId : session.SelectedId));
            case "battle.unit": return Reply(true, null, "单位读取成功。", snapshot.Unit(request.UnitId));
            case "battle.board": return Reply(true, null, "战场区域读取成功。", snapshot.Board(new AxialHex(request.Q, request.R), request.Radius));
            case "battle.inspect_cell": return Reply(true, null, "格点读取成功。", snapshot.Board(new AxialHex(request.Q, request.R), 0));
            case "battle.select_unit": return session.Select(request.UnitId) ? WriteReply(request, "已切换角色。") : Reply(false, "INVALID_UNIT", "不是可操作的存活角色。");
            case "battle.legal_actions": return Reply(true, null, "合法操作读取成功。", snapshot.LegalActions());
            case "battle.legal_move": return Reply(true, null, "移动摘要读取成功。", snapshot.LegalMove());
            case "battle.preview_move": return Reply(true, null, "移动预览读取成功。", snapshot.PreviewMove(new AxialHex(request.Q, request.R)));
            case "battle.legal_card": return Reply(true, null, "卡牌合法性读取成功。", snapshot.LegalCard(request.CardId));
            case "battle.events": return Reply(true, null, "事件读取成功。", journal.Read(request.AfterEventId, request.Limit));
            case "battle.wait_idle": return Reply(true, null, "空闲状态读取成功。", new { idle = isIdle?.Invoke() ?? session.Phase != BattlefieldSession.BattlePhase.Monsters });
            case "battle.capture":
                string capturePath = capture?.Invoke(request.Name, request.CaptureMode);
                return capturePath != null ? Reply(true, null, "截图已保存。", new { path = capturePath }) : Reply(false, "CAPTURE_FAILED", "截图失败。");
            case "battle.move": ok = session.Movement.TryMovePath(session.SelectedId, request.Path?.Select(x => new AxialHex(x.Q, x.R)).ToArray() ?? Array.Empty<AxialHex>(), out error); break;
            case "battle.play_card": ok = session.TryCastCard(request.CardId, request.HasTarget ? new AxialHex(request.Q, request.R) : session.Selected.Coord, out error); break;
            case "battle.end_turn":
                if (session.Phase != BattlefieldSession.BattlePhase.Player) return Reply(false, "INVALID_PHASE", "当前不是玩家回合。");
                session.EndCurrentTurn(); enemyTurnStarted?.Invoke(); return WriteReply(request, "已结束回合。");
            case "battle.pick_item": ok = session.TryPickItemFromCurrentCell(request.InstanceId, request.Slot, out error); break;
            case "battle.use_item":
                AxialHex? useTarget = request.HasTarget ? new AxialHex(request.Q, request.R) : null;
                ok = request.FromCurrentCell ? session.TryUseItemFromCurrentCell(request.InstanceId, useTarget, out error) : session.TryUseItemAt(request.Slot, useTarget, out error); break;
            case "battle.throw_item":
                if (!request.HasTarget) return Reply(false, "INVALID_TARGET", "投掷需要 q 与 r。");
                ok = request.FromCurrentCell ? session.TryUseItemFromCurrentCell(request.InstanceId, new AxialHex(request.Q, request.R), out error) : session.TryUseItemAt(request.Slot, new AxialHex(request.Q, request.R), out error); break;
            case "battle.equip": ok = session.TryEquipFromCurrentCell(request.InstanceId, ParseHand(request.Hand), out error); break;
            case "battle.drop_equipment": ok = session.TryDropEquippedWeaponOnCurrentCell(ParseHand(request.Hand), out error); break;
            default: return Reply(false, "UNKNOWN_COMMAND", "未知指令：" + request.Type);
        }
        return ok ? WriteReply(request, "指令成功。") : Reply(false, "COMMAND_REJECTED", error, snapshot.Summary());
    }

    private BattleCommandResult WriteReply(BattleCommandRequest request, string message)
    {
        stateVersion++;
        object data = string.Equals(request.ResponseMode, "full", StringComparison.OrdinalIgnoreCase) ? snapshot.State()
            : string.Equals(request.ResponseMode, "none", StringComparison.OrdinalIgnoreCase) ? null : snapshot.Summary();
        return Reply(true, null, message, data);
    }
    private BattleCommandResult Reply(bool ok, string code, string message, object data = null)
    {
        var result = ok ? BattleCommandResult.Success(message, data) : BattleCommandResult.Fail(code, message, data);
        result.StateVersion = stateVersion;
        return result;
    }

    private static BattlefieldSession.HandSlot ParseHand(string hand) => string.Equals(hand, "right", StringComparison.OrdinalIgnoreCase) ? BattlefieldSession.HandSlot.Right : BattlefieldSession.HandSlot.Left;
}
