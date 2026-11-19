namespace Wbskt.Workflow.Engine;

// Singleton, updated only by LeaderElectionService (single writer) and read by LeadershipGate,
// EngineLeadershipCoordinator and the leadership health check. Keeping
// leadership as in-memory state means those readers never need a DB round trip per check.
public sealed class LeadershipState
{
    // Work stops this long before the lease row expires, so it stops before a standby could win it.
    private static readonly TimeSpan SafetyMargin = TimeSpan.FromSeconds(5);

    private volatile bool _isLeader;
    private volatile bool _busStarted;
    private DateTime _leaseValidUntilUtc;
    private readonly TaskCompletionSource _becameLeaderTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsLeader => _isLeader;
    public bool BusStarted => _busStarted;
    public DateTime LeaseValidUntilUtc => _leaseValidUntilUtc;

    // The moment the gate closes: the lease's end less the safety margin.
    public DateTime WorkStopsAtUtc => _leaseValidUntilUtc - SafetyMargin;

    // Completes the first time this instance becomes leader; EngineLeadershipCoordinator awaits
    // it once to know when to start the bus and run recovery.
    public Task WhenLeaderAsync() => _becameLeaderTcs.Task;

    // True while this instance leads and its lease has more than the safety margin left at nowUtc.
    public bool HoldsLeaseAt(DateTime nowUtc) => _isLeader && nowUtc < WorkStopsAtUtc;

    public void SetLeader(DateTime leaseValidUntilUtc)
    {
        _leaseValidUntilUtc = leaseValidUntilUtc;
        _isLeader = true;
        _becameLeaderTcs.TrySetResult();
    }

    public void SetStandby()
    {
        _isLeader = false;
        _busStarted = false;
    }

    public void SetBusStarted()
    {
        _busStarted = true;
    }
}
