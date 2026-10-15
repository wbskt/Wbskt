using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

// Registered first (see Program.cs) so LeadershipState is meaningful before any other hosted
// service starts checking ILeadershipGate.IsActive. Renews the engine-leader lease on a fixed
// interval; a leader that fails to renew stops the process rather than risk two engines running
// active work at once - `restart: unless-stopped` brings it back as a standby.
public sealed class LeaderElectionService : BackgroundService
{
    private const string EngineLeaderLease = "engine-leader";

    // ILeaseProvider is Scoped (BaseSqlProvider convention); this service is a singleton, so it
    // resolves it through a scope per tick rather than capturing it directly in the constructor.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostIdentity _hostIdentity;
    private readonly LeadershipState _leadershipState;
    private readonly TimeProvider _clock;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<LeaderElectionService> _logger;
    private readonly TimeSpan _renewInterval;
    private readonly TimeSpan _leaseTtl;

    public LeaderElectionService(
        IServiceScopeFactory scopeFactory,
        IHostIdentity hostIdentity,
        LeadershipState leadershipState,
        TimeProvider clock,
        IHostApplicationLifetime appLifetime,
        ILogger<LeaderElectionService> logger,
        IOptions<WorkflowEngineOptions> options)
    {
        _scopeFactory = scopeFactory;
        _hostIdentity = hostIdentity;
        _leadershipState = leadershipState;
        _clock = clock;
        _appLifetime = appLifetime;
        _logger = logger;
        _renewInterval = options.Value.LeaderElectionRenewInterval;
        _leaseTtl = TimeSpan.FromSeconds(options.Value.LeaderElectionLeaseTtlSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_renewInterval);
        do
        {
            await TickAsync(stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Releases the lease on the way down so the standby can take over at once instead of waiting
    // out the TTL - after a clean deploy, and equally after this host stopped itself because
    // recovery failed or a renewal was lost. This service is registered first, so the host stops it
    // last: by now EngineLeadershipCoordinator has stopped the bus, and stopping the renew loop
    // before releasing means no tick can take the lease back afterwards. Best effort: the TTL is
    // the fallback, so a failure here is logged and must never fail the shutdown.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (!_leadershipState.WhenLeaderAsync().IsCompleted)
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var leaseProvider = scope.ServiceProvider.GetRequiredService<ILeaseProvider>();
            // Lease_Release only matches the current holder, so a lease another instance has
            // already taken over is left alone.
            await leaseProvider.ReleaseAsync(EngineLeaderLease, _hostIdentity.HostId, cancellationToken);
            _logger.LogInformation("Released the engine-leader lease.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release the engine-leader lease on shutdown; the standby takes over once it expires.");
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var leaseProvider = scope.ServiceProvider.GetRequiredService<ILeaseProvider>();

            bool acquired = await leaseProvider.TryAcquireAsync(EngineLeaderLease, _hostIdentity.HostId, _leaseTtl, ct);
            if (acquired)
            {
                _leadershipState.SetLeader(_clock.GetUtcNow().UtcDateTime.Add(_leaseTtl));
                return;
            }

            HandleLostOrDeniedLease();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_leadershipState.IsLeader)
            {
                _logger.LogCritical(ex, "Failed to renew the engine-leader lease; stopping so this instance rejoins as standby.");
                _appLifetime.StopApplication();
                return;
            }

            _logger.LogWarning(ex, "Failed to acquire the engine-leader lease; will retry.");
        }
    }

    private void HandleLostOrDeniedLease()
    {
        if (_leadershipState.IsLeader)
        {
            // We held leadership and just failed to renew - another instance may already be
            // acquiring it. Fail fast rather than risk two leaders running active work.
            _logger.LogCritical("Lost the engine-leader lease during renewal; stopping so this instance rejoins as standby.");
            _appLifetime.StopApplication();
            return;
        }

        _leadershipState.SetStandby();
    }
}
