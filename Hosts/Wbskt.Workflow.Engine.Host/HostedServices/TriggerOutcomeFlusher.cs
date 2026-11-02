using Wbskt.Infrastructure.Hosting;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Writes the buffered trigger outcomes (<see cref="TriggerOutcomeBuffer"/>) every few seconds, in one
/// call. On every instance, not only the leader, so an engine that just lost the lease still writes
/// what it buffered. Best effort: a failed write drops that batch, as a failover drops what was not
/// yet written; these are diagnostics.
/// </summary>
public sealed class TriggerOutcomeFlusher(TriggerOutcomeBuffer buffer, IServiceScopeFactory scopeFactory, ILogger<TriggerOutcomeFlusher> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
{
    protected override string Name => "Trigger outcome flush";

    protected override TimeSpan Interval { get; } = TimeSpan.FromSeconds(2);

    protected override bool RunOnStart => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var rows = buffer.Drain();
        if (rows.Count == 0)
        {
            return;
        }

        try
        {
            await services.GetRequiredService<ITriggerDispatchOutcomeProvider>().RecordManyAsync(rows, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning("Could not write {Count} trigger outcomes; they are dropped. {Message}", rows.Count, ex.Message);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        // What was buffered since the last tick.
        await TickAsync(cancellationToken);
    }
}
