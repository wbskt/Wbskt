using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine;

// A pure in-memory read of LeadershipState, with a safety margin before the lease row actually
// expires: no DB call per tick, and it turns false slightly before a standby could win the row, so
// two instances doing the same work stays extremely unlikely.
internal sealed class LeadershipGate(LeadershipState leadershipState, IClock clock) : ILeadershipGate
{
    private static readonly TimeSpan SafetyMargin = TimeSpan.FromSeconds(5);

    // Hosted services (BookmarkScheduler, RunReaper, ...) start at process boot and are gated only by
    // this check. Requiring BusStarted too - not just IsLeader - keeps them from dispatching or
    // publishing before EngineLeadershipCoordinator has actually started the bus.
    public bool IsActive =>
        leadershipState.IsLeader
        && leadershipState.BusStarted
        && clock.UtcNow < leadershipState.LeaseValidUntilUtc - SafetyMargin;
}

// Management and tests: they never run the engine's hosted services, so there is no one to stand by for.
internal sealed class AlwaysActiveLeadershipGate : ILeadershipGate
{
    public bool IsActive => true;
}
