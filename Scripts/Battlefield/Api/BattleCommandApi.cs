using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CardSimulator.Battlefield;

/// <summary>Development-only localhost JSON API. HTTP work is queued and executed on Godot's main thread.</summary>
public sealed class BattleCommandApi : IDisposable
{
    private sealed class Pending { public BattleCommandRequest Request; public TaskCompletionSource<BattleCommandResult> Completion; }
    private readonly ConcurrentQueue<Pending> pending = new();
    private readonly BattlefieldSession session;
    private readonly Action enemyTurnStarted;
    private readonly Func<bool> isIdle;
    private readonly Func<string, string> capture;
    private readonly ConcurrentQueue<object> events = new();
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource cancellation = new();
    private Task serverTask;
    private static readonly JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };

    public BattleCommandApi(BattlefieldSession session, Action enemyTurnStarted, Func<bool> isIdle, Func<string, string> capture, int port = 17880)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.enemyTurnStarted = enemyTurnStarted;
        this.isIdle = isIdle;
        this.capture = capture;
        listener.Prefixes.Add($"http://127.0.0.1:{port}/api/game/");
        session.Message += OnMessage;
        session.UnitEntered += entry => events.Enqueue(new { kind = "unit_entered", entry.EventId, entry.UnitId, from = entry.From, to = entry.To });
        session.AttackResolved += attack => events.Enqueue(new { kind = "attack", attack.EventId, attack.SourceUnitId, attack.TargetUnitId, attack.From, attack.To, mode = attack.Mode.ToString(), attack.Damage });
        session.Finished += phase => events.Enqueue(new { kind = "finished", phase = phase.ToString() });
    }

    public void Start()
    {
        if (listener.IsListening) return;
        listener.Start();
        serverTask = Task.Run(ServerLoop);
    }

    public void ProcessPending()
    {
        while (pending.TryDequeue(out Pending item))
        {
            try { item.Completion.TrySetResult(Execute(item.Request)); }
            catch (Exception ex) { item.Completion.TrySetResult(BattleCommandResult.Fail("INTERNAL", ex.Message)); }
        }
    }

    private BattleCommandResult Execute(BattleCommandRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Type)) return BattleCommandResult.Fail("INVALID_REQUEST", "缺少 type。");
        if (request.UnitId > 0 && !session.Select(request.UnitId)) return BattleCommandResult.Fail("INVALID_UNIT", "不是可操作的存活角色。");
        bool ok; string error;
        switch (request.Type)
        {
            case "battle.state": return BattleCommandResult.Success("状态读取成功。", Snapshot());
            case "battle.select_unit":
                return session.Select(request.UnitId) ? BattleCommandResult.Success("已切换角色。", Snapshot()) : BattleCommandResult.Fail("INVALID_UNIT", "不是可操作的存活角色。", Snapshot());
            case "battle.legal_actions": return BattleCommandResult.Success("合法操作读取成功。", LegalActions());
            case "battle.events": return BattleCommandResult.Success("事件读取成功。", events.ToArray());
            case "battle.wait_idle": return BattleCommandResult.Success("空闲状态读取成功。", new { idle = isIdle?.Invoke() ?? session.Phase != BattlefieldSession.BattlePhase.Monsters });
            case "battle.capture":
                string capturePath = capture?.Invoke(request.Name);
                return capturePath != null ? BattleCommandResult.Success("截图已保存。", new { path = capturePath }) : BattleCommandResult.Fail("CAPTURE_FAILED", "截图失败。");
            case "battle.move":
                ok = session.Movement.TryMovePath(session.SelectedId, ToPath(request.Path), out error); break;
            case "battle.play_card":
                AxialHex cardTarget = request.HasTarget ? new AxialHex(request.Q, request.R) : session.Selected.Coord;
                ok = session.TryCastCard(request.CardId, cardTarget, out error); break;
            case "battle.end_turn":
                if (session.Phase != BattlefieldSession.BattlePhase.Player) return BattleCommandResult.Fail("INVALID_PHASE", "当前不是玩家回合。");
                session.EndCurrentTurn(); enemyTurnStarted?.Invoke(); return BattleCommandResult.Success("已结束回合。", Snapshot());
            case "battle.pick_item":
                ok = session.TryPickItemFromCurrentCell(request.InstanceId, request.Slot, out error); break;
            case "battle.use_item":
                AxialHex? useTarget = request.HasTarget ? new AxialHex(request.Q, request.R) : null;
                ok = request.FromCurrentCell
                    ? session.TryUseItemFromCurrentCell(request.InstanceId, useTarget, out error)
                    : session.TryUseItemAt(request.Slot, useTarget, out error); break;
            case "battle.throw_item":
                if (!request.HasTarget) return BattleCommandResult.Fail("INVALID_TARGET", "投掷需要 q 与 r。");
                ok = request.FromCurrentCell
                    ? session.TryUseItemFromCurrentCell(request.InstanceId, new AxialHex(request.Q, request.R), out error)
                    : session.TryUseItemAt(request.Slot, new AxialHex(request.Q, request.R), out error); break;
            case "battle.equip":
                ok = session.TryEquipFromCurrentCell(request.InstanceId, ParseHand(request.Hand), out error); break;
            case "battle.drop_equipment":
                ok = session.TryDropEquippedWeaponOnCurrentCell(ParseHand(request.Hand), out error); break;
            default: return BattleCommandResult.Fail("UNKNOWN_COMMAND", "未知指令：" + request.Type);
        }
        return ok ? BattleCommandResult.Success("指令成功。", Snapshot()) : BattleCommandResult.Fail("COMMAND_REJECTED", error, Snapshot());
    }

    private void OnMessage(string message) => events.Enqueue(new { kind = "message", message });

    private static IReadOnlyList<AxialHex> ToPath(List<ApiHex> path) => path?.Select(x => new AxialHex(x.Q, x.R)).ToArray() ?? Array.Empty<AxialHex>();
    private static BattlefieldSession.HandSlot ParseHand(string hand) => string.Equals(hand, "right", StringComparison.OrdinalIgnoreCase) ? BattlefieldSession.HandSlot.Right : BattlefieldSession.HandSlot.Left;
    private object Snapshot() => new
    {
        phase = session.Phase.ToString(), round = session.Round, selectedUnitId = session.SelectedId,
        units = session.Occupancy.Placements.Values.Select(p => new
        {
            id = p.UnitId, name = p.Name, role = p.Role.ToString(), hp = p.Unit.HP, maxHp = p.Unit.Max_HP,
            shield = p.Unit.Shield, energy = p.Unit.Energy, q = p.Coord.Q, r = p.Coord.R, presence = p.Presence.ToString(),
            intent = p.Role == BattlefieldRole.Enemy ? session.GetEnemyIntentDisplay(p.UnitId) : null,
        }),
        selected = BuildPlayerInventory(session.SelectedId),
        hands = session.PlayerIds.Select(id => new
        {
            unitId = id,
            cards = session.GetHand(id).Select(c => new { instanceId = c.UniqueInGameId, cardId = c.CardId, name = c.CardName, cost = c.EnergyCost }),
        }),
        groundObjects = session.Board.Cells.Values.Where(c => c.Items.Count > 0 || c.Trigger != null).Select(c => new
        {
            q = c.Coord.Q, r = c.Coord.R,
            items = c.Items.Select(BuildObject), trigger = c.Trigger == null ? null : BuildObject(c.Trigger),
        }),
    };

    private object BuildPlayerInventory(int playerId)
    {
        var loadout = session.GetLoadout(playerId);
        return new
        {
            unitId = playerId,
            leftHand = loadout?.LeftHand == null ? null : BuildObject(loadout.LeftHand),
            rightHand = loadout?.RightHand == null ? null : BuildObject(loadout.RightHand),
            itemSlots = loadout?.Items.Select(item => item == null ? null : BuildObject(item)),
        };
    }

    private object LegalActions()
    {
        int unitId = session.SelectedId;
        return new
        {
            selectedUnitId = unitId,
            moveDestinations = session.Board.Cells.Keys.Select(cell => new { cell.Q, cell.R, path = session.Movement.FindPath(unitId, cell).Select(x => new { x.Q, x.R }) }).Where(x => x.path.Any()),
            cards = session.GetHand(unitId).Select(card => new { cardId = card.CardId, instanceId = card.UniqueInGameId, targets = session.GetCastCandidates(card.CardId).Select(x => new { x.Q, x.R }) }),
            currentCell = new { q = session.Selected.Coord.Q, r = session.Selected.Coord.R, items = session.Board.Cells[session.Selected.Coord].Items.Select(BuildObject) },
        };
    }

    private static object BuildObject(GroundObject item) => new
    {
        instanceId = item.InstanceId, definitionId = item.DefinitionId, kind = item.Kind.ToString(), handsRequired = item.HandsRequired,
        attackRange = item.AttackRange, moveBonus = item.MoveBonus, healAmount = item.HealAmount, damageAmount = item.DamageAmount,
        needsTarget = item.NeedsTarget, spatialShape = item.SpatialShape.ToString(), maxRange = item.ItemMaxRange,
    };

    private async Task ServerLoop()
    {
        while (!cancellation.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch when (cancellation.IsCancellationRequested) { break; }
            catch { continue; }
            _ = Task.Run(() => Handle(context));
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        BattleCommandRequest request;
        try
        {
            if (context.Request.HttpMethod == "GET") request = new BattleCommandRequest { Type = "battle.state" };
            else
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                request = JsonSerializer.Deserialize<BattleCommandRequest>(await reader.ReadToEndAsync(), json);
            }
            var completion = new TaskCompletionSource<BattleCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Enqueue(new Pending { Request = request, Completion = completion });
            BattleCommandResult result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Write(context.Response, result);
        }
        catch (Exception ex) { await Write(context.Response, BattleCommandResult.Fail("REQUEST_ERROR", ex.Message)); }
    }

    private static async Task Write(HttpListenerResponse response, BattleCommandResult result)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
        response.StatusCode = result.Ok ? 200 : 400; response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length; await response.OutputStream.WriteAsync(bytes); response.Close();
    }

    public void Dispose()
    {
        cancellation.Cancel();
        session.Message -= OnMessage;
        if (listener.IsListening) listener.Stop();
        listener.Close(); cancellation.Dispose();
    }
}

public sealed class BattleCommandRequest
{
    public string Type { get; set; }
    public int UnitId { get; set; }
    public int CardId { get; set; }
    public int Q { get; set; }
    public int R { get; set; }
    public bool HasTarget { get; set; }
    public List<ApiHex> Path { get; set; }
    public string InstanceId { get; set; }
    public int Slot { get; set; }
    public string Hand { get; set; }
    public bool FromCurrentCell { get; set; }
    public string Name { get; set; }
}
public sealed class ApiHex { public int Q { get; set; } public int R { get; set; } }
public sealed class BattleCommandResult
{
    public bool Ok { get; set; } public string ErrorCode { get; set; } public string Message { get; set; } public object Data { get; set; }
    public static BattleCommandResult Success(string message, object data) => new() { Ok = true, Message = message, Data = data };
    public static BattleCommandResult Fail(string code, string message, object data = null) => new() { Ok = false, ErrorCode = code, Message = message, Data = data };
}
