using MassTransit;
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
    private BusHandle? _busHandle;

    public EngineLeadershipCoordinator(
        LeadershipState leadershipState,
        IBusControl busControl,
        RunRecoveryService runRecoveryService,
        ILogger<EngineLeadershipCoordinator> logger)
    {
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
            _busHandle = await _busControl.StartAsync(stoppingToken);
            _leadershipState.SetBusStarted();

            await _runRecoveryService.RecoverAsync(stoppingToken);
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
