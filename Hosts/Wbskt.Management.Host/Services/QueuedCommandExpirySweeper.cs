using Wbskt.Infrastructure.Hosting;
using Wbskt.Infrastructure.Devices;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Ends the commands whose expiry passed while their device stayed offline, each with a failed command
/// event ("expired before the device came back"), so the console stops showing them as waiting and a
/// workflow step waiting on one leaves by its failure path.
/// </summary>
public sealed class QueuedCommandExpirySweeper : PeriodicWorker
{
    private const int BatchSize = 200;

    public QueuedCommandExpirySweeper(IServiceScopeFactory scopeFactory, ILogger<QueuedCommandExpirySweeper> logger)
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
    {
    }

    protected override string Name => "Queued command expiry sweep";

    protected override TimeSpan Interval { get; } = TimeSpan.FromSeconds(15);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var queue = services.GetRequiredService<DeviceCommandQueue>();

        // A full batch means there may be more; keep going rather than leave them for the next tick.
        int expired;
        int total = 0;
        do
        {
            expired = await queue.ExpireAsync(BatchSize, ct);
            total += expired;
        }
        while (expired == BatchSize && !ct.IsCancellationRequested);

        if (total > 0)
        {
            Logger.LogInformation("Expired {Count} queued commands whose devices did not come back in time.", total);
        }
    }
}
