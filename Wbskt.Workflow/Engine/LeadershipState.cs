namespace Wbskt.Workflow.Engine;

// Singleton, updated only by LeaderElectionService (single writer) and read by SqlLeaseHolder,
// EngineLeadershipCoordinator, LeaderOnlyMiddleware and the /healthz/ready endpoint. Keeping
// leadership as in-memory state means those readers never need a DB round trip per check.
public sealed class LeadershipState
{
    private volatile bool _isLeader;
    private volatile bool _busStarted;
    private DateTime _leaseValidUntilUtc;
    private readonly TaskCompletionSource _becameLeaderTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsLeader => _isLeader;
    public bool BusStarted => _busStarted;
    public DateTime LeaseValidUntilUtc => _leaseValidUntilUtc;

    // Completes the first time this instance becomes leader; EngineLeadershipCoordinator awaits
    // it once to know when to start the bus and run recovery.
    public Task WhenLeaderAsync() => _becameLeaderTcs.Task;

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
