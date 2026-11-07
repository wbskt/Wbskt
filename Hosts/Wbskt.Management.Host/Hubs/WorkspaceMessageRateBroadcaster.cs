using Microsoft.AspNetCore.SignalR;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// Every <see cref="WorkspaceMessageCounter.Window"/>, sends each busy workspace's message rate to its
/// group as <see cref="NotificationHub.WorkspaceMessageRateMethod"/>. A quiet workspace gets one zero
/// and then nothing until it is busy again.
/// </summary>
public sealed class WorkspaceMessageRateBroadcaster : PeriodicWorker
{
    private readonly WorkspaceMessageCounter _counter;
    private readonly IHubContext<NotificationHub> _hub;

    public WorkspaceMessageRateBroadcaster(
        WorkspaceMessageCounter counter,
        IHubContext<NotificationHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkspaceMessageRateBroadcaster> logger)
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
    {
        _counter = counter;
        _hub = hub;
    }

    protected override string Name => "Workspace message rate";

    protected override TimeSpan Interval => WorkspaceMessageCounter.Window;

    protected override bool RunOnStart => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        foreach (var (workspaceId, rate) in _counter.Flush())
        {
            await _hub.Clients.Group(NotificationHub.GroupName(workspaceId)).SendAsync(NotificationHub.WorkspaceMessageRateMethod, rate, ct);
        }
    }
}
