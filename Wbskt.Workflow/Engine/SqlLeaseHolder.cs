using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine;

// All granular lease names map to the single engine-leader lease for now (seam kept for
// per-workload leases later). IsHeldAsync is a pure in-memory read of LeadershipState with a
// safety margin before the DB row actually expires - no DB call per tick, and it flips to false
// slightly before a standby could win the row, so overlapping work stays extremely unlikely.
internal sealed class SqlLeaseHolder : ILeaseHolder
{
    private const string EngineLeaderLease = "engine-leader";
    private static readonly TimeSpan SafetyMargin = TimeSpan.FromSeconds(5);

    private readonly LeadershipState _leadershipState;
    // ILeaseProvider is Scoped (BaseSqlProvider convention); this class is a singleton, so it
    // resolves it through a scope per call rather than capturing it directly in the constructor.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostIdentity _hostIdentity;
    private readonly IClock _clock;
    private readonly TimeSpan _leaseTtl;

    public SqlLeaseHolder(
        LeadershipState leadershipState,
        IServiceScopeFactory scopeFactory,
        IHostIdentity hostIdentity,
        IClock clock,
        IOptions<WorkflowEngineOptions> options)
    {
        _leadershipState = leadershipState;
        _scopeFactory = scopeFactory;
        _hostIdentity = hostIdentity;
        _clock = clock;
        _leaseTtl = TimeSpan.FromSeconds(options.Value.LeaderElectionLeaseTtlSeconds);
    }

    public Task<bool> IsHeldAsync(string leaseName, CancellationToken ct)
    {
        // Other hosted services (BookmarkScheduler, RunReaper, ...) start at process boot and are
        // gated only by this check. Requiring BusStarted too - not just IsLeader - keeps them from
        // dispatching/publishing before EngineLeadershipCoordinator has actually started the bus.
        bool isHeld = _leadershipState.IsLeader
            && _leadershipState.BusStarted
            && _clock.UtcNow < _leadershipState.LeaseValidUntilUtc - SafetyMargin;
        return Task.FromResult(isHeld);
    }

    public async Task<bool> TryAcquireAsync(string leaseName, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var leaseProvider = scope.ServiceProvider.GetRequiredService<ILeaseProvider>();
        return await leaseProvider.TryAcquireAsync(EngineLeaderLease, _hostIdentity.HostId, _leaseTtl, ct);
    }

    public async Task ReleaseAsync(string leaseName, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var leaseProvider = scope.ServiceProvider.GetRequiredService<ILeaseProvider>();
        await leaseProvider.ReleaseAsync(EngineLeaderLease, _hostIdentity.HostId, ct);
    }
}
