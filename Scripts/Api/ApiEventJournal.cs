// Scripts/Api/ApiEventJournal.cs
// 只读、有序的事件日志：战斗消息 / 入格 / 攻击 / 胜负，供 `api.events` 与 `battle.events` 断言。
// 由 `ApiService` 持有；战斗域注册时挂上、注销时摘掉。

using System;
using System.Collections.Concurrent;
using System.Linq;
using CardSimulator.Battlefield;

/// <summary>只读、有序事件日志（环形缓冲 500 条）。</summary>
public sealed class ApiEventJournal : IDisposable
{
    private readonly ConcurrentQueue<ApiBattleEvent> events = new();
    private readonly BattlefieldSession session;
    private readonly Action<string> messageHandler;
    private readonly Action<BattlefieldEntry> enteredHandler;
    private readonly Action<BattlefieldAttackEvent> attackHandler;
    private readonly Action<BattlefieldSession.BattlePhase> finishedHandler;
    private long nextId;

    public ApiEventJournal(BattlefieldSession session)
    {
        this.session = session;
        messageHandler = message => Add("message", new { message });
        enteredHandler = entry => Add("unit_entered", new { entry.EventId, entry.UnitId, from = entry.From, to = entry.To });
        attackHandler = attack => Add("attack", new { attack.EventId, attack.SourceUnitId, attack.TargetUnitId, attack.From, attack.To, mode = attack.Mode.ToString(), attack.Damage });
        finishedHandler = phase => Add("finished", new { phase = phase.ToString() });
        session.Message += messageHandler;
        session.UnitEntered += enteredHandler;
        session.AttackResolved += attackHandler;
        session.Finished += finishedHandler;
    }

    /// <summary>本局附加事件（运行局侧：结局 / 休息 / 时间点推进等不经过战斗会话的事件）。</summary>
    public void AddRunEvent(string kind, object data) => Add("run." + kind, data);

    public ApiBattleEvent[] Read(long afterEventId, int limit)
    {
        int actualLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 500);
        return events.Where(x => x.Id > afterEventId).Take(actualLimit).ToArray();
    }

    private void Add(string kind, object data)
    {
        events.Enqueue(new ApiBattleEvent { Id = ++nextId, Kind = kind, Data = data });
        while (events.Count > 500) events.TryDequeue(out _);
    }

    public void Dispose()
    {
        session.Message -= messageHandler;
        session.UnitEntered -= enteredHandler;
        session.AttackResolved -= attackHandler;
        session.Finished -= finishedHandler;
    }
}

public sealed class ApiBattleEvent { public long Id { get; set; } public string Kind { get; set; } public object Data { get; set; } }
