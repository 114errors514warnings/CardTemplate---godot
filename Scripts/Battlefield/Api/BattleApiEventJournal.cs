using System;
using System.Collections.Concurrent;
using CardSimulator.Battlefield;

/// <summary>Read-only, ordered event journal for API-driven test assertions.</summary>
public sealed class BattleApiEventJournal : IDisposable
{
    private readonly ConcurrentQueue<object> events = new();
    private readonly BattlefieldSession session;
    private readonly Action<string> messageHandler;
    private readonly Action<BattlefieldEntry> enteredHandler;
    private readonly Action<BattlefieldAttackEvent> attackHandler;
    private readonly Action<BattlefieldSession.BattlePhase> finishedHandler;
    private long nextId;

    public BattleApiEventJournal(BattlefieldSession session)
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

    public object[] Snapshot() => events.ToArray();
    private void Add(string kind, object data) => events.Enqueue(new { id = ++nextId, kind, data });
    public void Dispose()
    {
        session.Message -= messageHandler;
        session.UnitEntered -= enteredHandler;
        session.AttackResolved -= attackHandler;
        session.Finished -= finishedHandler;
    }
}
