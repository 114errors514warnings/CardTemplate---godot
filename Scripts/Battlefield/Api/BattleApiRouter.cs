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
    private readonly Func<string, string> capture;

    public BattleApiRouter(BattlefieldSession session, BattleApiSnapshot snapshot, BattleApiEventJournal journal,
        Action enemyTurnStarted, Func<bool> isIdle, Func<string, string> capture)
    { this.session = session; this.snapshot = snapshot; this.journal = journal; this.enemyTurnStarted = enemyTurnStarted; this.isIdle = isIdle; this.capture = capture; }

    public BattleCommandResult Execute(BattleCommandRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Type)) return BattleCommandResult.Fail("INVALID_REQUEST", "缺少 type。");
        if (request.Type != "battle.select_unit" && request.UnitId > 0)
            return BattleCommandResult.Fail("PLAYER_PERMISSION", "请先使用 battle.select_unit 切换角色，其他指令不接受 unitId。");
        bool ok; string error;
        switch (request.Type)
        {
            case "battle.state": return BattleCommandResult.Success("状态读取成功。", snapshot.State());
            case "battle.select_unit": return session.Select(request.UnitId) ? BattleCommandResult.Success("已切换角色。", snapshot.State()) : BattleCommandResult.Fail("INVALID_UNIT", "不是可操作的存活角色。", snapshot.State());
            case "battle.legal_actions": return BattleCommandResult.Success("合法操作读取成功。", snapshot.LegalActions());
            case "battle.events": return BattleCommandResult.Success("事件读取成功。", journal.Snapshot());
            case "battle.wait_idle": return BattleCommandResult.Success("空闲状态读取成功。", new { idle = isIdle?.Invoke() ?? session.Phase != BattlefieldSession.BattlePhase.Monsters });
            case "battle.capture":
                string capturePath = capture?.Invoke(request.Name);
                return capturePath != null ? BattleCommandResult.Success("截图已保存。", new { path = capturePath }) : BattleCommandResult.Fail("CAPTURE_FAILED", "截图失败。");
            case "battle.move": ok = session.Movement.TryMovePath(session.SelectedId, request.Path?.Select(x => new AxialHex(x.Q, x.R)).ToArray() ?? Array.Empty<AxialHex>(), out error); break;
            case "battle.play_card": ok = session.TryCastCard(request.CardId, request.HasTarget ? new AxialHex(request.Q, request.R) : session.Selected.Coord, out error); break;
            case "battle.end_turn":
                if (session.Phase != BattlefieldSession.BattlePhase.Player) return BattleCommandResult.Fail("INVALID_PHASE", "当前不是玩家回合。");
                session.EndCurrentTurn(); enemyTurnStarted?.Invoke(); return BattleCommandResult.Success("已结束回合。", snapshot.State());
            case "battle.pick_item": ok = session.TryPickItemFromCurrentCell(request.InstanceId, request.Slot, out error); break;
            case "battle.use_item":
                AxialHex? useTarget = request.HasTarget ? new AxialHex(request.Q, request.R) : null;
                ok = request.FromCurrentCell ? session.TryUseItemFromCurrentCell(request.InstanceId, useTarget, out error) : session.TryUseItemAt(request.Slot, useTarget, out error); break;
            case "battle.throw_item":
                if (!request.HasTarget) return BattleCommandResult.Fail("INVALID_TARGET", "投掷需要 q 与 r。");
                ok = request.FromCurrentCell ? session.TryUseItemFromCurrentCell(request.InstanceId, new AxialHex(request.Q, request.R), out error) : session.TryUseItemAt(request.Slot, new AxialHex(request.Q, request.R), out error); break;
            case "battle.equip": ok = session.TryEquipFromCurrentCell(request.InstanceId, ParseHand(request.Hand), out error); break;
            case "battle.drop_equipment": ok = session.TryDropEquippedWeaponOnCurrentCell(ParseHand(request.Hand), out error); break;
            default: return BattleCommandResult.Fail("UNKNOWN_COMMAND", "未知指令：" + request.Type);
        }
        return ok ? BattleCommandResult.Success("指令成功。", snapshot.State()) : BattleCommandResult.Fail("COMMAND_REJECTED", error, snapshot.State());
    }

    private static BattlefieldSession.HandSlot ParseHand(string hand) => string.Equals(hand, "right", StringComparison.OrdinalIgnoreCase) ? BattlefieldSession.HandSlot.Right : BattlefieldSession.HandSlot.Left;
}
