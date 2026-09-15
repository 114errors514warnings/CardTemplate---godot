using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using CardSimulator.Battlefield;

/// <summary>Godot-main-thread bridge and API lifecycle owner.</summary>
public sealed class BattleApiHost : IDisposable
{
    private sealed class Pending { public BattleCommandRequest Request; public TaskCompletionSource<BattleCommandResult> Completion; }
    private readonly ConcurrentQueue<Pending> pending = new();
    private readonly BattleApiEventJournal journal;
    private readonly BattleApiRouter router;
    private readonly BattleApiHttpServer server;

    public BattleApiHost(BattlefieldSession session, Action enemyTurnStarted, Func<bool> isIdle, Func<string, string> capture, int port)
    {
        var snapshot = new BattleApiSnapshot(session);
        journal = new BattleApiEventJournal(session);
        router = new BattleApiRouter(session, snapshot, journal, enemyTurnStarted, isIdle, capture);
        server = new BattleApiHttpServer(Enqueue, port);
    }

    public void Start() => server.Start();
    public void ProcessPending()
    {
        while (pending.TryDequeue(out Pending item))
        {
            try { item.Completion.TrySetResult(router.Execute(item.Request)); }
            catch (Exception ex) { item.Completion.TrySetResult(BattleCommandResult.Fail("INTERNAL", ex.Message)); }
        }
    }

    private Task<BattleCommandResult> Enqueue(BattleCommandRequest request)
    {
        var completion = new TaskCompletionSource<BattleCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Enqueue(new Pending { Request = request, Completion = completion });
        return completion.Task;
    }

    public void Dispose() { server.Dispose(); journal.Dispose(); }
}
