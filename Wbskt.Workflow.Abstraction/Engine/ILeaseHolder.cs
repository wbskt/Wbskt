namespace Wbskt.Workflow.Abstraction.Engine;

// leaseName is a seam for future per-workload leases, not a live gate today: every current
// implementation (SqlLeaseHolder, AlwaysHoldsLeaseHolder) collapses all lease names onto the
// single engine-leader lease/LeadershipState, ignoring the value passed in. Callers such as
// BookmarkScheduler, RunReaper, ScheduledFireTicker, HistoryRetentionGc, IdempotencyKeyGc, and
// PendingTriggerEventBacklogReaper each pass their own distinct name expecting independent
// gating, but they are all really just asking "are we the engine leader?" - none of them can
// currently run on a non-leader instance while another is gated, or vice versa.
public interface ILeaseHolder
{
    Task<bool> TryAcquireAsync(string leaseName, CancellationToken ct);
    Task ReleaseAsync(string leaseName, CancellationToken ct);
    Task<bool> IsHeldAsync(string leaseName, CancellationToken ct);
}
