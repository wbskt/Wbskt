using MassTransit;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

// The bus is never auto-started (Program.cs calls RemoveMassTransitHostedService) so a standby
// never attaches to the shared engine queues. Once this instance becomes leader, start the bus
// and recover in-flight branches - recovery only ever runs on a fresh leader, so the previous
// leader is guaranteed dead or self-fenced (see LeaderElectionService).
public sealed class EngineLeadershipCoordinator : BackgroundService
{
    private readonly LeadershipState _leadershipState;
    private readonly IBusControl _busControl;
    private readonly RunRecoveryService _runRecoveryService;
    private readonly ILogger<EngineLeadershipCoordinator> _logger;
    private readonly TimeSpan _overlapRecheckDelay;
    private readonly TimeProvider _clock;
    private BusHandle? _busHandle;

    public EngineLeadershipCoordinator(
        LeadershipState leadershipState,
        IBusControl busControl,
        RunRecoveryService runRecoveryService,
        ILogger<EngineLeadershipCoordinator> logger,
        IOptions<WorkflowEngineOptions> options,
        TimeProvider clock)
    {
        _overlapRecheckDelay = options.Value.LeaderOverlapRecheckDelay;
        _clock = clock;
        _leadershipState = leadershipState;
        _busControl = busControl;
        _runRecoveryService = runRecoveryService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _leadershipState.WhenLeaderAsync().WaitAsync(stoppingToken);

        _logger.LogInformation("Acquired engine leadership; starting the bus and recovering active runs.");
        try
        {
            // Read before the bus starts and the gate opens, while nothing here can wake a branch yet:
            // once the scheduler and consumers run, a branch they woke would look the same.
            IReadOnlyCollection<BranchExecutionRequest> woken = await _runRecoveryService.CollectWokenAsync(stoppingToken);

            // Also before the bus starts: a Pending event claim can then only be a stopped engine's.
            await _runRecoveryService.ReleaseAbandonedClaimsAsync(stoppingToken);

            _runRecoveryService.StartRecordingDispatches();
            _busHandle = await _busControl.StartAsync(stoppingToken);
            _leadershipState.SetBusStarted();

            await _runRecoveryService.RecoverAsync(woken, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // A leader that cannot recover must not keep the lease: rethrowing stops the host
            // (BackgroundServiceExceptionBehavior.StopHost), LeaderElectionService releases the lease as
            // it stops, and the restart brings this instance back as a standby. Logged as Critical
            // because a cause that persists makes that a restart loop, and the default host message
            // for a faulted BackgroundService does not say what was being attempted.
            _logger.LogCritical(
                ex,
                "Engine leadership startup failed ({Cause}); stopping the host so the lease is released and another instance can lead.",
                ex.GetBaseException().Message);
            throw;
        }

        _logger.LogInformation("Engine leadership startup complete.");

        // The previous leader may still have been stopping, and starting runs, while this one recovered.
        await Task.Delay(_overlapRecheckDelay, _clock, stoppingToken);
        try
        {
            await _runRecoveryService.RecoverOverlapAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // What it misses is left to the reaper, as before; not a reason to give up the lease.
            _logger.LogError(ex, "Could not look for Active branches the previous leader left while it was stopping.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_busHandle != null)
        {
            await _busHandle.StopAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }
}
