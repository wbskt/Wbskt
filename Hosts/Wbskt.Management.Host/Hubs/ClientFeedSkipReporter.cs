using Microsoft.AspNetCore.SignalR;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// Tells a client's watchers, once a second, how many of its messages <see cref="ClientFeedThrottle"/>
/// dropped, so the page says so even when the client then goes quiet.
/// </summary>
public sealed class ClientFeedSkipReporter : PeriodicWorker
{
    private readonly ClientFeedThrottle _throttle;
    private readonly IHubContext<NotificationHub> _hub;

    public ClientFeedSkipReporter(
        ClientFeedThrottle throttle,
        IHubContext<NotificationHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<ClientFeedSkipReporter> logger,
        TimeProvider clock)
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger, clock)
    {
        _throttle = throttle;
        _hub = hub;
    }

    protected override string Name => "Client feed skips";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(1);

    protected override bool RunOnStart => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        foreach (var (clientId, skipped) in _throttle.TakeSkipped())
        {
            await _hub.Clients.Group(NotificationHub.ClientGroupName(clientId)).SendAsync(NotificationHub.ClientFeedSkippedMethod, skipped, ct);
        }
    }
}
